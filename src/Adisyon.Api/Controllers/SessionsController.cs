using System.ComponentModel.DataAnnotations;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Adisyon.Api.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Adisyon.Api.Controllers;

/// <summary>
/// Adisyon akışı: masayı aç → sipariş ekle (bir veya birçok kez) → hesabı kapat.
/// Ödeme alma, iptal/ikram ve masa taşıma Faz 3'te eklenecek.
/// </summary>
[ApiController]
[Route("api")]
public class SessionsController(AdisyonDbContext db, TimeProvider timeProvider, BranchNotifier notifier) : ControllerBase
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

        // Mutfak ekranı yeni kartı anında görsün, masa planları yeni tutarı göstersin.
        await notifier.OrderCreatedAsync(session.BranchId, await KitchenOrderDto.LoadAsync(db, order.Id, cancellationToken));
        await notifier.TablesChangedAsync(session.BranchId);

        return (await LoadDtoAsync(session.Id, cancellationToken))!;
    }

    /// <summary>
    /// Hesabı kapatır. İstemci adisyonu hangi sürümde gördüyse onu gönderir; o arada yeni sipariş
    /// eklendiyse kapanış reddedilir ve kasiyer güncel tutarı görmek zorunda kalır.
    /// </summary>
    [HttpPost("sessions/{id:guid}/close")]
    [Authorize(Roles = RoleNames.Checkout)]
    public async Task<ActionResult<SessionDto>> Close(Guid id, CloseSessionRequest request, CancellationToken cancellationToken)
    {
        var session = await FindInMyBranchAsync(id, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }
        if (session.Status != TableSessionStatus.Open)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Bu adisyon zaten kapatılmış.");
        }

        // EF'e "bu satırı istemcinin gördüğü sürümle güncelle" diyoruz. Veritabanındaki sürüm farklıysa
        // UPDATE hiçbir satırı etkilemez ve EF DbUpdateConcurrencyException fırlatır.
        db.Entry(session).Property(s => s.Version).OriginalValue = request.Version;
        var now = timeProvider.GetUtcNow();
        session.Status = TableSessionStatus.Closed;
        session.ClosedAt = now;
        session.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Adisyon siz bakarken değişti. Güncel hâlini görüp tekrar deneyin.");
        }

        await notifier.TablesChangedAsync(session.BranchId);
        return (await LoadDtoAsync(session.Id, cancellationToken))!;
    }

    private Task<TableSession?> FindInMyBranchAsync(Guid id, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        return db.TableSessions.SingleOrDefaultAsync(s => s.Id == id && s.BranchId == branchId, cancellationToken);
    }

    private async Task<SessionDto?> LoadDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        var session = await db.TableSessions.AsNoTracking()
            .Include(s => s.Table)
            .Include(s => s.Orders).ThenInclude(o => o.Items)
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

public record SessionDto(
    Guid Id,
    Guid TableId,
    string TableName,
    TableSessionStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal Total,
    uint Version,
    List<OrderDto> Orders)
{
    public static SessionDto From(TableSession s)
    {
        var orders = s.Orders.OrderBy(o => o.CreatedAt).Select(OrderDto.From).ToList();
        var total = orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.Total);
        return new SessionDto(s.Id, s.TableId, s.Table!.Name, s.Status, s.OpenedAt, s.ClosedAt, total, s.Version, orders);
    }
}

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
