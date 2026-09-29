using System.ComponentModel.DataAnnotations;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Adisyon.Api.Printing;
using Adisyon.Api.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>
/// Adisyon akışı: masayı aç → sipariş ekle (bir veya birçok kez) → hesabı kapat.
/// Sipariş gönderilince mutfak fişi yazdırılır. Ödeme tamamlanınca adisyon kendiliğinden kapanır.
/// İptal, ikram, masa taşıma ve birleştirme de buradadır.
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
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
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

        var ticket = await PrintKitchenTicketAsync(order.Id, cancellationToken);
        await notifier.TablesChangedAsync(session.BranchId);

        // Sipariş kaydedildi; fiş basılamadıysa garson hemen görsün (mutfak siparişi sessizce kaybolmasın).
        return (await LoadDtoAsync(session.Id, cancellationToken))! with
        {
            PrintWarning = WarningFor(ticket),
            FailedTicketId = ticket?.Status == PrintStatus.Failed ? ticket.Id : null,
        };
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

    /// <summary>
    /// Ürünü iptal eder: hesaptan düşer ama silinmez (kim, ne zaman kaydedilir). Mutfağa "İPTAL" fişi gider.
    /// Quantity ile satırın bir kısmı iptal edilebilir ("2 x Kebap"tan 1'i).
    /// </summary>
    [HttpPost("sessions/{id:guid}/items/{itemId:guid}/void")]
    [Authorize(Roles = RoleNames.Checkout)]
    public Task<ActionResult<SessionDto>> Void(Guid id, Guid itemId, AdjustItemRequest request, CancellationToken cancellationToken) =>
        AdjustItemAsync(id, itemId, request, isVoid: true, cancellationToken);

    /// <summary>Ürünü ikram eder: fişte görünür ama ücreti alınmaz.</summary>
    [HttpPost("sessions/{id:guid}/items/{itemId:guid}/comp")]
    [Authorize(Roles = RoleNames.Checkout)]
    public Task<ActionResult<SessionDto>> Comp(Guid id, Guid itemId, AdjustItemRequest request, CancellationToken cancellationToken) =>
        AdjustItemAsync(id, itemId, request, isVoid: false, cancellationToken);

    private async Task<ActionResult<SessionDto>> AdjustItemAsync(
        Guid id, Guid itemId, AdjustItemRequest request, bool isVoid, CancellationToken cancellationToken)
    {
        var session = await FindWithMoneyAsync(id, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }
        if (session.Status != TableSessionStatus.Open)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Bu adisyon kapatılmış.");
        }
        if (session.Version != request.Version)
        {
            return StaleSession();
        }

        var order = session.Orders.SingleOrDefault(o => o.Items.Any(i => i.Id == itemId));
        var item = order?.Items.Single(i => i.Id == itemId);
        if (order is null || item is null)
        {
            return NotFound();
        }
        if (!item.IsCharged)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Bu ürün zaten iptal veya ikram edilmiş.");
        }
        if (request.Quantity > item.Quantity)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: $"Bu satırda yalnızca {item.Quantity} adet var.");
        }

        // "2 x Kebap"tan 1'i: satır bölünür, ayrılan kısım işaretlenir.
        var part = item.SplitOff(request.Quantity);
        if (part != item)
        {
            order.Items.Add(part);
        }
        var now = timeProvider.GetUtcNow();
        if (isVoid)
        {
            (part.VoidedAt, part.VoidedByUserId) = (now, User.GetUserId());
        }
        else
        {
            (part.CompedAt, part.CompedByUserId) = (now, User.GetUserId());
        }

        if (SessionMoney.Total(session) < SessionMoney.Paid(session))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Bu masadan alınan ödeme, işlem sonrası toplamdan fazla olur. İşlem yapılamaz.");
        }

        session.UpdatedAt = now;
        if (!await TrySaveWithVersionAsync(session, request.Version, cancellationToken))
        {
            return StaleSession();
        }

        if (isVoid)
        {
            await PrintAsync(KitchenNotice.Void(session.BranchId, session.Table!.Name, now, part.Quantity, part.ProductName, User.Identity?.Name));
        }
        await notifier.TablesChangedAsync(session.BranchId);
        return (await LoadDtoAsync(session.Id, cancellationToken))!;
    }

    /// <summary>
    /// Adisyonu başka masaya taşır. Hedef masa boşsa adisyon oraya geçer; doluysa iki adisyon birleşir
    /// (siparişler ve alınmış ödemeler hedefe aktarılır, bu adisyon kapanır). Mutfağa masa değişikliği fişi gider.
    /// </summary>
    [HttpPost("sessions/{id:guid}/move")]
    [Authorize(Roles = RoleNames.FrontOfHouse)]
    public async Task<ActionResult<SessionDto>> Move(Guid id, MoveSessionRequest request, CancellationToken cancellationToken)
    {
        var source = await FindWithMoneyAsync(id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }
        if (source.Status != TableSessionStatus.Open)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Bu adisyon kapatılmış.");
        }
        if (source.Version != request.Version)
        {
            return StaleSession();
        }

        var target = await db.Tables.SingleOrDefaultAsync(t => t.Id == request.TargetTableId && t.BranchId == source.BranchId, cancellationToken);
        if (target is null || !target.IsActive || target.Id == source.TableId)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Geçerli başka bir masa seçin.");
        }

        var targetSession = await db.TableSessions
            .Include(s => s.Orders)
            .Include(s => s.Payments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.TableId == target.Id && s.Status == TableSessionStatus.Open, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var fromName = source.Table!.Name;
        Guid resultId;
        if (targetSession is null)
        {
            // Boş masaya taşıma: aynı adisyon, yeni masa.
            (source.TableId, source.Table, source.UpdatedAt) = (target.Id, target, now);
            resultId = source.Id;
        }
        else
        {
            // Dolu masayla birleştirme: her şey hedef adisyona geçer, bu adisyon kapanır.
            foreach (var order in source.Orders.ToList())
            {
                order.TableSessionId = targetSession.Id;
            }
            foreach (var payment in source.Payments.ToList())
            {
                payment.TableSessionId = targetSession.Id;
            }
            (source.Status, source.ClosedAt, source.UpdatedAt, source.MergedIntoSessionId) =
                (TableSessionStatus.Closed, now, now, targetSession.Id);
            targetSession.UpdatedAt = now; // hedefin sürümü değişir; orada açık ödeme ekranı varsa eskimiş olur
            resultId = targetSession.Id;
        }

        try
        {
            if (!await TrySaveWithVersionAsync(source, request.Version, cancellationToken))
            {
                return StaleSession();
            }
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: $"{target.Name} az önce açıldı. Tekrar deneyin.");
        }

        await PrintAsync(KitchenNotice.TableChanged(source.BranchId, fromName, target.Name, now, merged: targetSession is not null, User.Identity?.Name));
        await notifier.TablesChangedAsync(source.BranchId);
        return (await LoadDtoAsync(resultId, cancellationToken))!;
    }

    /// <summary>
    /// Yazdırır; yazıcı hatası işlemi geri almaz (kayıt zaten yapıldı). Başarısız fiş Fişler sayfasında
    /// "tekrar yazdır" ile görünür.
    /// </summary>
    private Task<PrintedTicket> PrintAsync(PrintJob job) => printer.PrintAsync(job, CancellationToken.None);

    private static string? WarningFor(PrintedTicket? ticket) =>
        ticket?.Status == PrintStatus.Failed
            ? $"Mutfak fişi yazdırılamadı! {ticket.Error} Sipariş kaydedildi; yazıcıyı kontrol edip tekrar yazdırın."
            : null;

    /// <summary>Müşteriye verilecek hesap fişini (adisyon pusulası) yazdırır.</summary>
    [HttpPost("sessions/{id:guid}/print-bill")]
    [Authorize(Roles = RoleNames.FrontOfHouse)]
    public async Task<ActionResult<PrintResult>> PrintBill(Guid id, CancellationToken cancellationToken)
    {
        var session = await FindWithMoneyAsync(id, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        var branch = await db.Branches.SingleAsync(b => b.Id == session.BranchId, cancellationToken);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == session.TenantId, cancellationToken);
        var ticket = await printer.PrintAsync(BillTicket.Create(tenant.Name, branch.Name, session, timeProvider.GetUtcNow()), cancellationToken);
        return PrintResult.From(ticket);
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

    /// <summary>Mutfak fişini yazdırır. Yazıcı hatası siparişi geri almaz (sipariş zaten kaydedildi).</summary>
    private async Task<PrintedTicket?> PrintKitchenTicketAsync(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            return await printer.PrintAsync((await KitchenTicket.LoadAsync(db, orderId, cancellationToken)).ToPrintJob(), cancellationToken);
        }
        catch (Exception ex)
        {
            // Beklenmeyen hata (ör. fiş oluşturulamadı): siparişi bozma, günlüğe yaz.
            logger.LogError(ex, "Mutfak fişi hazırlanamadı (sipariş {OrderId})", orderId);
            return null;
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

/// <summary>Satırdan kaç adet iptal/ikram edileceği.</summary>
public record AdjustItemRequest([Range(1, 99)] int Quantity, uint Version);

public record MoveSessionRequest(Guid TargetTableId, uint Version);

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
    List<PaymentDto> Payments,
    string? PrintWarning = null,
    Guid? FailedTicketId = null)
{
    public static SessionDto From(TableSession s) => new(
        s.Id, s.TableId, s.Table!.Name, s.Status, s.OpenedAt, s.ClosedAt,
        SessionMoney.Total(s), SessionMoney.Paid(s), SessionMoney.Remaining(s), s.Version,
        s.Orders.OrderBy(o => o.CreatedAt).Select(OrderDto.From).ToList(),
        s.Payments.OrderBy(p => p.CreatedAt).Select(p => new PaymentDto(p.Id, p.Method, p.Amount, p.CreatedAt)).ToList());
}

/// <summary>Yazdırma sonucu: Status Printed/Preview/Failed; Failed ise Error kullanıcıya gösterilir.</summary>
public record PrintResult(Guid TicketId, PrintStatus Status, string? Error)
{
    public static PrintResult From(PrintedTicket ticket) => new(ticket.Id, ticket.Status, ticket.Error);
}

public record PaymentDto(Guid Id, PaymentMethod Method, decimal Amount, DateTimeOffset CreatedAt);

public record OrderDto(Guid Id, OrderSource Source, OrderStatus Status, DateTimeOffset CreatedAt, decimal Total, List<OrderItemDto> Items)
{
    public static OrderDto From(Order o)
    {
        // Satırlar garsonun girdiği sırayla (Position) gösterilir.
        var items = o.Items.OrderBy(i => i.Position).ThenBy(i => i.Id)
            .Select(i => new OrderItemDto(i.Id, i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.Note,
                IsVoided: i.VoidedAt is not null, IsComped: i.CompedAt is not null))
            .ToList();
        var total = items.Where(i => !i.IsVoided && !i.IsComped).Sum(i => i.UnitPrice * i.Quantity);
        return new OrderDto(o.Id, o.Source, o.Status, o.CreatedAt, total, items);
    }
}

public record OrderItemDto(
    Guid Id, Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, string? Note,
    bool IsVoided = false, bool IsComped = false);
