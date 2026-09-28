using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Adisyon.Api.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>
/// Mutfak ekranı ve sipariş durumları: Yeni → Hazırlanıyor → Hazır → Servis edildi.
/// </summary>
[ApiController]
[Route("api")]
public class KitchenController(AdisyonDbContext db, BranchNotifier notifier) : ControllerBase
{
    /// <summary>
    /// İzin verilen durum geçişleri ve kimin yapabileceği. Geriye dönüş yok; iptal Faz 3'te ayrı işlem olacak.
    /// </summary>
    private static readonly Dictionary<(OrderStatus From, OrderStatus To), string[]> Transitions = new()
    {
        [(OrderStatus.New, OrderStatus.Preparing)] = KitchenRoles,
        [(OrderStatus.New, OrderStatus.Ready)] = KitchenRoles, // ör. içecek: hazırlamaya gerek yok
        [(OrderStatus.Preparing, OrderStatus.Ready)] = KitchenRoles,
        [(OrderStatus.Ready, OrderStatus.Served)] = [.. KitchenRoles, RoleNames.Waiter, RoleNames.Cashier],
    };

    private static string[] KitchenRoles => [RoleNames.Owner, RoleNames.Manager, RoleNames.Kitchen];

    /// <summary>Mutfakta bekleyen siparişler (yeni, hazırlanıyor, hazır), en eskisi önce.</summary>
    [HttpGet("kitchen/orders")]
    public Task<List<KitchenOrderDto>> ActiveOrders(CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        // Sıralama, karta dönüştürmeden önce yapılmalı; yoksa EF bunu SQL'e çeviremez.
        return KitchenOrderDto.Query(db,
                db.Orders
                    .Where(o => o.TableSession!.BranchId == branchId
                        // Hesabı kapanmış masanın siparişi mutfakta beklemez.
                        && o.TableSession.Status == TableSessionStatus.Open
                        && (o.Status == OrderStatus.New || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.Ready))
                    .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id))
            .ToListAsync(cancellationToken);
    }

    [HttpPost("orders/{id:guid}/status")]
    public async Task<ActionResult<KitchenOrderDto>> ChangeStatus(Guid id, ChangeOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id && o.TableSession!.BranchId == branchId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status == request.Status)
        {
            // Aynı düğmeye iki kez basıldıysa (veya iki ekran aynı anda bastıysa) sorun değil.
            return await KitchenOrderDto.LoadAsync(db, order.Id, cancellationToken);
        }
        if (!Transitions.TryGetValue((order.Status, request.Status), out var allowedRoles))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: $"Sipariş '{order.Status}' durumundan '{request.Status}' durumuna geçemez.");
        }
        if (!allowedRoles.Any(User.IsInRole))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Bu durum değişikliği için yetkiniz yok.");
        }

        order.Status = request.Status;
        await db.SaveChangesAsync(cancellationToken);

        var dto = await KitchenOrderDto.LoadAsync(db, order.Id, cancellationToken);
        await notifier.OrderUpdatedAsync(branchId, dto);
        return dto;
    }
}

public record ChangeOrderStatusRequest(OrderStatus Status);

/// <summary>Mutfak kartı: hangi masa, ne zaman, kim girdi, neler var.</summary>
public record KitchenOrderDto(
    Guid Id,
    Guid SessionId,
    Guid TableId,
    string TableName,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    string? CreatedByName,
    List<OrderItemDto> Items)
{
    /// <summary>Siparişleri tek SQL sorgusunda mutfak kartına dönüştürür.</summary>
    public static IQueryable<KitchenOrderDto> Query(AdisyonDbContext db, IQueryable<Order> orders) =>
        orders.Select(o => new KitchenOrderDto(
            o.Id,
            o.TableSessionId,
            o.TableSession!.TableId,
            o.TableSession.Table!.Name,
            o.Status,
            o.CreatedAt,
            db.Users.Where(u => u.Id == o.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefault(),
            o.Items.OrderBy(i => i.Position)
                .Select(i => new OrderItemDto(i.Id, i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.Note))
                .ToList()));

    public static Task<KitchenOrderDto> LoadAsync(AdisyonDbContext db, Guid orderId, CancellationToken cancellationToken) =>
        Query(db, db.Orders.Where(o => o.Id == orderId)).SingleAsync(cancellationToken);
}
