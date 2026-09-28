using System.Collections.Concurrent;

namespace Adisyon.Api.Printing;

public enum TicketKind
{
    /// <summary>Mutfak fişi: sipariş gönderilince.</summary>
    Kitchen,
    /// <summary>Hesap fişi (adisyon pusulası): müşteriye verilen.</summary>
    Bill,
}

/// <summary>Yazdırılacak fiş: türü, hangi şubenin yazıcısına gideceği, başlığı ve kâğıda basılacak metin.</summary>
public record PrintJob(TicketKind Kind, Guid BranchId, string Title, string Text);

/// <summary>Fişi yazdıran yer. Faz 4'te gerçek termal yazıcı (ESC/POS) bu arayüzü uygulayacak.</summary>
public interface IPrinter
{
    Task PrintAsync(PrintJob job, CancellationToken cancellationToken);
}

/// <summary>Önizlemesi gösterilen, basılmış bir fiş.</summary>
public record PrintedTicket(Guid Id, TicketKind Kind, Guid BranchId, string Title, DateTimeOffset PrintedAt, string Text);

/// <summary>
/// Sanal yazıcı: yazıcı yokken fişleri bellekte tutar, "Fişler" sayfası bunları gösterir.
/// Sunucu yeniden başlarsa liste sıfırlanır; asıl kayıt veritabanındaki siparişler ve ödemelerdir.
/// </summary>
public class PreviewPrinter(TimeProvider timeProvider) : IPrinter
{
    private const int MaxTickets = 100;
    private readonly ConcurrentQueue<PrintedTicket> _tickets = new();

    public Task PrintAsync(PrintJob job, CancellationToken cancellationToken)
    {
        _tickets.Enqueue(new PrintedTicket(Guid.CreateVersion7(), job.Kind, job.BranchId, job.Title,
            timeProvider.GetUtcNow(), job.Text));
        while (_tickets.Count > MaxTickets && _tickets.TryDequeue(out _))
        {
        }
        return Task.CompletedTask;
    }

    /// <summary>Bir şubenin son fişleri, en yenisi önce.</summary>
    public List<PrintedTicket> Recent(Guid branchId) =>
        _tickets.Where(t => t.BranchId == branchId).Reverse().ToList();
}

/// <summary>Fiş metni yazarken ortak yardımcılar.</summary>
public static class TicketFormat
{
    /// <summary>80 mm termal kâğıtta standart yazı tipiyle bir satıra sığan karakter sayısı.</summary>
    public const int PaperWidth = 48;

    /// <summary>Fiş saatleri restoranın saatine göre basılır.</summary>
    public static readonly TimeZoneInfo RestaurantTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static readonly System.Globalization.CultureInfo Turkish = new("tr-TR");

    public static string DoubleLine => new('=', PaperWidth);
    public static string Line => new('-', PaperWidth);

    public static string Center(string value) =>
        value.Length >= PaperWidth ? value[..PaperWidth] : value.PadLeft((PaperWidth + value.Length) / 2).PadRight(PaperWidth);

    /// <summary>Solda metin, sağda değer: "Adana Kebap .......... 760,00" gibi, satır genişliğinde.</summary>
    public static string LeftRight(string left, string right)
    {
        var space = PaperWidth - right.Length - 1;
        if (left.Length > space) left = left[..space];
        return left.PadRight(PaperWidth - right.Length) + right;
    }

    /// <summary>760 → "760,00" (fişte ₺ işareti yazıcılarda sorun çıkarabildiği için yazılmaz).</summary>
    public static string Money(decimal value) => value.ToString("N2", Turkish);

    public static string Upper(string value) => value.ToUpper(Turkish);

    public static DateTimeOffset Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, RestaurantTimeZone);
}
