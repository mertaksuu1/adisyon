using System.Collections.Concurrent;

namespace Adisyon.Api.Printing;

/// <summary>Mutfak fişini yazdıran yer. Faz 4'te gerçek termal yazıcı (ESC/POS) bu arayüzü uygulayacak.</summary>
public interface IKitchenPrinter
{
    Task PrintAsync(KitchenTicket ticket, CancellationToken cancellationToken);
}

/// <summary>Önizlemesi gösterilen bir fiş.</summary>
public record PrintedTicket(Guid OrderId, Guid BranchId, string TableName, DateTimeOffset PrintedAt, string Text);

/// <summary>
/// Sanal yazıcı: yazıcı yokken fişleri bellekte tutar, "Mutfak fişleri" sayfası bunları gösterir.
/// Sunucu yeniden başlarsa liste sıfırlanır; asıl kayıt veritabanındaki siparişlerdir.
/// </summary>
public class PreviewPrinter(TimeProvider timeProvider) : IKitchenPrinter
{
    private const int MaxTickets = 100;
    private readonly ConcurrentQueue<PrintedTicket> _tickets = new();

    /// <summary>Fiş saatleri restoranın saatine göre basılır.</summary>
    public static readonly TimeZoneInfo RestaurantTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public Task PrintAsync(KitchenTicket ticket, CancellationToken cancellationToken)
    {
        _tickets.Enqueue(new PrintedTicket(ticket.OrderId, ticket.BranchId, ticket.TableName,
            timeProvider.GetUtcNow(), ticket.ToText(RestaurantTimeZone)));
        while (_tickets.Count > MaxTickets && _tickets.TryDequeue(out _))
        {
        }
        return Task.CompletedTask;
    }

    /// <summary>Bir şubenin son fişleri, en yenisi önce.</summary>
    public List<PrintedTicket> Recent(Guid branchId) =>
        _tickets.Where(t => t.BranchId == branchId).Reverse().ToList();
}
