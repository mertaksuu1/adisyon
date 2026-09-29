using System.Collections.Concurrent;

namespace Adisyon.Api.Printing;

public enum TicketKind
{
    /// <summary>Mutfak fişi: sipariş gönderilince.</summary>
    Kitchen,
    /// <summary>Hesap fişi (adisyon pusulası): müşteriye verilen.</summary>
    Bill,
    /// <summary>Gün sonu (Z) raporu.</summary>
    Report,
}

/// <summary>Yazdırılacak fiş: türü, hangi şubenin yazıcısına gideceği, başlığı ve kâğıda basılacak metin.</summary>
public record PrintJob(TicketKind Kind, Guid BranchId, string Title, string Text);

public enum PrintStatus
{
    /// <summary>Bu fiş türü için yazıcı ayarlanmamış; yalnızca ekranda (Fişler sayfası) görünür.</summary>
    Preview,
    /// <summary>Yazıcıya gönderildi.</summary>
    Printed,
    /// <summary>Yazıcıya ulaşılamadı (kapalı, kağıt bitti, ağ yok). Fişler sayfasından tekrar yazdırılabilir.</summary>
    Failed,
}

/// <summary>Fişi yazdıran yer: şubenin ayarına göre gerçek yazıcıya gönderir ya da yalnızca önizler.</summary>
public interface IPrinter
{
    /// <summary>Hiçbir zaman hata fırlatmaz: sonucu (Printed/Preview/Failed) döndürür, fiş her durumda kaydedilir.</summary>
    Task<PrintedTicket> PrintAsync(PrintJob job, CancellationToken cancellationToken);
}

/// <summary>Kaydedilmiş bir fiş ve yazdırma sonucu.</summary>
public record PrintedTicket(
    Guid Id, TicketKind Kind, Guid BranchId, string Title, DateTimeOffset PrintedAt, string Text,
    PrintStatus Status = PrintStatus.Preview, string? Error = null);

/// <summary>
/// Son fişlerin kaydı (bellekte, şube başına en fazla 100). "Fişler" sayfası ve tekrar yazdırma bunu kullanır.
/// Sunucu yeniden başlarsa liste sıfırlanır; asıl kayıt veritabanındaki siparişler ve ödemelerdir.
/// </summary>
public class TicketLog
{
    private const int MaxTickets = 100;
    private readonly ConcurrentDictionary<Guid, PrintedTicket> _tickets = new();

    public void Save(PrintedTicket ticket)
    {
        _tickets[ticket.Id] = ticket;
        foreach (var old in _tickets.Values.Where(t => t.BranchId == ticket.BranchId)
                     .OrderByDescending(t => t.PrintedAt).ThenByDescending(t => t.Id).Skip(MaxTickets))
        {
            _tickets.TryRemove(old.Id, out _);
        }
    }

    public PrintedTicket? Find(Guid id, Guid branchId) =>
        _tickets.TryGetValue(id, out var ticket) && ticket.BranchId == branchId ? ticket : null;

    /// <summary>Bir şubenin son fişleri, en yenisi önce.</summary>
    public List<PrintedTicket> Recent(Guid branchId) =>
        _tickets.Values.Where(t => t.BranchId == branchId).OrderByDescending(t => t.PrintedAt).ThenByDescending(t => t.Id).ToList();
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
