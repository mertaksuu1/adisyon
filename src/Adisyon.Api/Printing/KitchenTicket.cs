using System.Text;
using Adisyon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Printing;

/// <summary>
/// Mutfak fişi: hangi masa, saat kaç, kim girdi, neler hazırlanacak. Fiyat yok; mutfağın işine yaramaz.
/// </summary>
public record KitchenTicket(
    Guid OrderId,
    Guid BranchId,
    string TableName,
    DateTimeOffset CreatedAt,
    string? WaiterName,
    List<KitchenTicketLine> Lines)
{
    /// <summary>80 mm termal kâğıtta standart yazı tipiyle bir satıra sığan karakter sayısı.</summary>
    public const int PaperWidth = 48;

    public static Task<KitchenTicket> LoadAsync(AdisyonDbContext db, Guid orderId, CancellationToken cancellationToken) =>
        db.Orders
            .Where(o => o.Id == orderId)
            .Select(o => new KitchenTicket(
                o.Id,
                o.TableSession!.BranchId,
                o.TableSession.Table!.Name,
                o.CreatedAt,
                db.Users.Where(u => u.Id == o.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefault(),
                o.Items.OrderBy(i => i.Position)
                    .Select(i => new KitchenTicketLine(i.Quantity, i.ProductName, i.Note))
                    .ToList()))
            .SingleAsync(cancellationToken);

    /// <summary>
    /// Fişin kâğıda basılacak düz metin hâli. Hem önizleme hem (Faz 4'te) gerçek yazıcı bunu kullanır.
    /// </summary>
    public string ToText(TimeZoneInfo timeZone)
    {
        var line = new string('-', PaperWidth);
        var time = TimeZoneInfo.ConvertTime(CreatedAt, timeZone).ToString("HH:mm");
        var text = new StringBuilder()
            .AppendLine(new string('=', PaperWidth))
            .AppendLine(Center("MUTFAK FİŞİ"))
            .AppendLine(new string('=', PaperWidth))
            .AppendLine(TableName.ToUpper(new System.Globalization.CultureInfo("tr-TR")).PadRight(PaperWidth - time.Length) + time);
        if (WaiterName is not null)
        {
            text.AppendLine($"Garson: {WaiterName}");
        }
        text.AppendLine(line);

        foreach (var item in Lines)
        {
            text.AppendLine($"{item.Quantity} x {item.ProductName}");
            if (item.Note is not null)
            {
                // Notlar dikkat çeksin diye ayrı satırda ve ">>" ile.
                text.AppendLine($"    >> {item.Note}");
            }
        }

        return text.AppendLine(new string('=', PaperWidth)).ToString();
    }

    private static string Center(string value) =>
        value.PadLeft((PaperWidth + value.Length) / 2).PadRight(PaperWidth);
}

public record KitchenTicketLine(int Quantity, string ProductName, string? Note);
