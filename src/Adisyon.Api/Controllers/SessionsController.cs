using System.ComponentModel.DataAnnotations;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Adisyon.Api.Printing;
using Adisyon.Api.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Adisyon.Api.Controllers;

/// <summary>
/// Adisyon akışı: masayı aç → sipariş ekle (bir veya birçok kez) → hesabı kapat.
/// Sipariş gönderilince mutfak fişi yazdırılır. Ödeme tamamlanınca adisyon kendiliğinden kapanır.
/// İptal/ikram ve masa taşıma Faz 3b'de eklenecek.
/// </summary>
[ApiController]
[Route("api")]
public class SessionsController(
    AdisyonDbContext db,
    TimeProvider timeProvider,
    BranchNotifier notifier,
    IPrinter printer,
    ILogger<SessionsController> logger) : ControllerBase
{
    [HttpPost("tables/{tableId:guid}/session")]
    [Authorize(Roles = RoleNames.FrontOfHouse)]
    public async Task<ActionResult<SessionDto>> Open(Guid tableId, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        var table = await db.Tables.SingleOrDefaultAsync(t => t.Id == tableId && t.BranchId == branchId, cancellationToken);
        if (table is null)
        {
            return NotFound();
        }
        if (!table.IsActive)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bu masa kullanım dışı.");
        }

        var now = timeProvider.GetUtcNow();
        var session = new TableSession { BranchId = branchId, TableId = table.Id, OpenedAt = now, UpdatedAt = now };
        db.TableSessions.Add(session);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Veritabanındaki "masada tek açık adisyon" kuralı devreye girdi. İki garson aynı anda
            // açmaya çalıştıysa ikincisi buraya düşer; mevcut adisyonu ona gösteriyoruz.
            var existing = await db.TableSessions.AsNoTracking()
                .SingleAsync(s => s.TableId == table.Id && s.Status == TableSessionStatus.Open, cancellationToken);
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Bu masada zaten açık bir adisyon var.",
                extensions: new Dictionary<string, object?> { ["sessionId"] = existing.Id });
        }

        await notifier.TablesChangedAsync(branchId);
        return CreatedAtAction(nameof(Get), new { id = session.Id }, await LoadDtoAsync(session.Id, cancellationToken));
    }

    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<SessionDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var dto = await LoadDtoAsync(id, cancellationToken);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost("sessions/{id:guid}/orders")]
    [Authorize(Roles = RoleNames.FrontOfHouse)]
    public async Task<ActionResult<SessionDto>> AddOrder(Guid id, AddOrderRequest request, CancellationToken cancellationToken)
    {
        var session = await FindInMyBranchAsync(id, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }
        if (session.Status != TableSessionStatus.Open)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Bu adisyon kapatılmış.");
        }

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        if (products.Count != productIds.Count)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Siparişteki bir ürün bulunamadı veya menüden kaldırılmış.");
        }

        var now = timeProvider.GetUtcNow();
        var order = new Order
        {
            TableSessionId = session.Id,
            Source = OrderSource.Staff,
            CreatedAt = now,
            CreatedByUserId = User.GetUserId(),
        };
        foreach (var (item, position) in request.Items.Select((item, index) => (item, index)))
        {
            var product = products[item.ProductId];
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                Position = position,
                // Ad ve fiyat sipariş anında kopyalanır (bkz. OrderItem açıklaması).
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = item.Quantity,
                Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim(),
            });
        }

        db.Orders.Add(order);
        session.UpdatedAt = now; // Sürümü (xmin) değiştirir; açık kapanış ekranları eskimiş olur.
        await db.SaveChangesAsync(cancellationToken);

        await PrintKitchenTicketAsync(order.Id, cancellationToken);
        await notifier.TablesChangedAsync(session.BranchId);

        return (await LoadDtoAsync(session.Id, cancellationToken))!;
    }

    /// <summary>
    /// Ödeme alır. Amount boşsa kalan tutarın tamamı ödenir. Kalan sıfırlanınca adisyon kapanır ve masa boşalır.
    /// İstemci adisyonu hangi sürümde gördüyse onu gönderir; o arada yeni sipariş eklendiyse ödeme reddedilir
    /// ve kasiyer güncel tutarı görmek zorunda kalır.
    /// </summary>
    [HttpPost("sessions/{id:guid}/payments")]
    [Authorize(Roles = RoleNames.Checkout)]
    public async Task<ActionResult<SessionDto>> Pay(Guid id, PayRequest request, CancellationToken cancellationToken)
    {
        var session = await FindWithMoneyAsync(id, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }
        if (session.Status != TableSessionStatus.Open)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Bu adisyon zaten kapatılmış.");
        }
        if (session.Version != request.Version)
        {
            return StaleSession();
        }

        var remaining = SessionMoney.Remaining(session);
        var amount = decimal.Round(request.Amount ?? remaining, 2);
        if (amount <= 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ödeme tutarı sıfırdan büyük olmalı.");
        }
        if (amount > remaining)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"Ödeme, kalan tutardan ({remaining:N2} ₺) fazla olamaz. Para üstünü ekrandan hesaplayın.");
        }

        var now = timeProvider.GetUtcNow();
        session.Payments.Add(new Payment
        {
            TableSessionId = session.Id,
            Method = request.Method,
            Amount = amount,
            CreatedAt = now,
            CreatedByUserId = User.GetUserId(),
        });
        session.UpdatedAt = now;
        if (amount == remaining)
        {
            // Hesap tamamen ödendi: masa boşalır.
            session.Status = TableSessionStatus.Closed;
            session.ClosedAt = now;
        }

        if (!await TrySaveWithVersionAsync(session, request.Version, cancellationToken))
        {
            return StaleSession();
        }

        await notifier.TablesChangedAsync(session.BranchId);
        return (await LoadDtoAsync(session.Id, cancellationToken))!;
    }

    /// <summary>
    /// Ödemesi tamamlanmış ya da hiç ürünü olmayan adisyonu kapatır (ör. müşteri sipariş vermeden kalktı).
    /// Ödenmemiş tutar varsa kapatılamaz; önce ödeme alınmalı.
    /// </summary>
    [HttpPost("sessions/{id:guid}/close")]
    [Authorize(Roles = RoleNames.Checkout)]
    public async Task<ActionResult<SessionDto>> Close(Guid id, CloseSessionRequest request, CancellationToken cancellationToken)
    {
        var session = await FindWithMoneyAsync(id, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }
        if (session.Status != TableSessionStatus.Open)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Bu adisyon zaten kapatılmış.");
        }
        if (session.Version != request.Version)
        {
            return StaleSession();
        }
        if (SessionMoney.Remaining(session) > 0)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: $"Ödenmemiş {SessionMoney.Remaining(session):N2} ₺ var. Önce ödeme alın.");
        }

        var now = timeProvider.GetUtcNow();
        session.Status = TableSessionStatus.Closed;
        session.ClosedAt = now;
        session.UpdatedAt = now;
        if (!await TrySaveWithVersionAsync(session, request.Version, cancellationToken))
        {
            return StaleSession();
        }

        await notifier.TablesChangedAsync(session.BranchId);
        return (await LoadDtoAsync(session.Id, cancellationToken))!;
    }

    /// <summary>Müşteriye verilecek hesap fişini (adisyon pusulası) yazdırır.</summary>
    [HttpPost("sessions/{id:guid}/print-bill")]
    [Authorize(Roles = RoleNames.FrontOfHouse)]
    public async Task<IActionResult> PrintBill(Guid id, CancellationToken cancellationToken)
    {
        var session = await FindWithMoneyAsync(id, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        var branch = await db.Branches.SingleAsync(b => b.Id == session.BranchId, cancellationToken);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == session.TenantId, cancellationToken);
        await printer.PrintAsync(BillTicket.Create(tenant.Name, branch.Name, session, timeProvider.GetUtcNow()), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// "Bu satırı istemcinin gördüğü sürümle güncelle": veritabanındaki sürüm farklıysa UPDATE hiçbir satırı
    /// etkilemez ve EF DbUpdateConcurrencyException fırlatır. İki kasiyer aynı anda ödeme alırsa biri reddedilir.
    /// </summary>
    private async Task<bool> TrySaveWithVersionAsync(TableSession session, uint version, CancellationToken cancellationToken)
    {
        db.Entry(session).Property(s => s.Version).OriginalValue = version;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private ObjectResult StaleSession() =>
        Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Adisyon siz bakarken değişti. Güncel hâlini görüp tekrar deneyin.");

    /// <summary>
    /// Mutfak fişini yazdırır. Yazıcı hatası siparişi geri almaz (sipariş zaten kaydedildi);
    /// Faz 4'te "fiş yazdırılamadı, tekrar dene" uyarısı eklenecek.
    /// </summary>
    private async Task PrintKitchenTicketAsync(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            await printer.PrintAsync((await KitchenTicket.LoadAsync(db, orderId, cancellationToken)).ToPrintJob(), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mutfak fişi yazdırılamadı (sipariş {OrderId})", orderId);
        }
    }

    private Task<TableSession?> FindInMyBranchAsync(Guid id, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        return db.TableSessions.SingleOrDefaultAsync(s => s.Id == id && s.BranchId == branchId, cancellationToken);
    }

    /// <summary>Tutar hesabı gereken işlemler için siparişleri ve ödemeleriyle birlikte yükler.</summary>
    private Task<TableSession?> FindWithMoneyAsync(Guid id, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        return db.TableSessions
            .Include(s => s.Table)
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .Include(s => s.Payments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == id && s.BranchId == branchId, cancellationToken);
    }

    private async Task<SessionDto?> LoadDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        var session = await db.TableSessions.AsNoTracking()
            .Include(s => s.Table)
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .Include(s => s.Payments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == id && s.BranchId == branchId, cancellationToken);
        return session is null ? null : SessionDto.From(session);
    }
}

public record AddOrderRequest([Required, MinLength(1), MaxLength(50)] List<AddOrderItem> Items);

public record AddOrderItem(
    Guid ProductId,
    [Range(1, 99)] int Quantity,
    [MaxLength(500)] string? Note = null);

public record CloseSessionRequest(uint Version);

/// <summary>Amount boşsa kalan tutarın tamamı ödenir.</summary>
public record PayRequest(PaymentMethod Method, uint Version, [Range(0.01, 99_999_999)] decimal? Amount = null);

public record SessionDto(
    Guid Id,
    Guid TableId,
    string TableName,
    TableSessionStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal Total,
    decimal Paid,
    decimal Remaining,
    uint Version,
    List<OrderDto> Orders,
    List<PaymentDto> Payments)
{
    public static SessionDto From(TableSession s) => new(
        s.Id, s.TableId, s.Table!.Name, s.Status, s.OpenedAt, s.ClosedAt,
        SessionMoney.Total(s), SessionMoney.Paid(s), SessionMoney.Remaining(s), s.Version,
        s.Orders.OrderBy(o => o.CreatedAt).Select(OrderDto.From).ToList(),
        s.Payments.OrderBy(p => p.CreatedAt).Select(p => new PaymentDto(p.Id, p.Method, p.Amount, p.CreatedAt)).ToList());
}

public record PaymentDto(Guid Id, PaymentMethod Method, decimal Amount, DateTimeOffset CreatedAt);

public record OrderDto(Guid Id, OrderSource Source, OrderStatus Status, DateTimeOffset CreatedAt, decimal Total, List<OrderItemDto> Items)
{
    public static OrderDto From(Order o)
    {
        // Satırlar garsonun girdiği sırayla (Position) gösterilir.
        var items = o.Items.OrderBy(i => i.Position)
            .Select(i => new OrderItemDto(i.Id, i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.Note)).ToList();
        return new OrderDto(o.Id, o.Source, o.Status, o.CreatedAt, items.Sum(i => i.UnitPrice * i.Quantity), items);
    }
}

public record OrderItemDto(Guid Id, Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, string? Note);
