using System.Text;
using Adisyon.Api.Data;
using Microsoft.EntityFrameworkCore;
using static Adisyon.Api.Printing.TicketFormat;

namespace Adisyon.Api.Printing;

/// <summary>
/// Mutfak fişi: hangi masa, saat kaç, kim girdi, neler hazırlanacak. Fiyat yok; mutfağın işine yaramaz.
/// </summary>
public record KitchenTicket(
    Guid BranchId,
    string TableName,
    DateTimeOffset CreatedAt,
    string? WaiterName,
    List<KitchenTicketLine> Lines)
{
    public static Task<KitchenTicket> LoadAsync(AdisyonDbContext db, Guid orderId, CancellationToken cancellationToken) =>
        db.Orders
            .Where(o => o.Id == orderId)
            .Select(o => new KitchenTicket(
                o.TableSession!.BranchId,
                o.TableSession.Table!.Name,
                o.CreatedAt,
                db.Users.Where(u => u.Id == o.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefault(),
                o.Items.OrderBy(i => i.Position)
                    .Select(i => new KitchenTicketLine(i.Quantity, i.ProductName, i.Note))
                    .ToList()))
            .SingleAsync(cancellationToken);

    public PrintJob ToPrintJob() => new(TicketKind.Kitchen, BranchId, TableName, ToText());

    /// <summary>Fişin kâğıda basılacak düz metin hâli.</summary>
    public string ToText()
    {
        var text = new StringBuilder()
            .AppendLine(DoubleLine)
            .AppendLine(Center("MUTFAK FİŞİ"))
            .AppendLine(DoubleLine)
            .AppendLine(LeftRight(Upper(TableName), Local(CreatedAt).ToString("HH:mm")));
        if (WaiterName is not null)
        {
            text.AppendLine($"Garson: {WaiterName}");
        }
        text.AppendLine(Line);

        foreach (var item in Lines)
        {
            text.AppendLine($"{item.Quantity} x {item.ProductName}");
            if (item.Note is not null)
            {
                // Notlar dikkat çeksin diye ayrı satırda ve ">>" ile.
                text.AppendLine($"    >> {item.Note}");
            }
        }

        return text.AppendLine(DoubleLine).ToString();
    }
}

public record KitchenTicketLine(int Quantity, string ProductName, string? Note);

/// <summary>
/// Mutfağa giden kısa uyarı fişleri: iptal ("boşuna pişirmeyin") ve masa değişikliği ("yemeği yeni masaya götürün").
/// </summary>
public static class KitchenNotice
{
    public static PrintJob Void(Guid branchId, string tableName, DateTimeOffset at, int quantity, string productName, string? byName) =>
        Create(branchId, "*** İPTAL ***", tableName, at, [$"{quantity} x {productName}", "HAZIRLAMAYIN / İPTAL EDİLDİ"], byName);

    public static PrintJob TableChanged(Guid branchId, string fromTable, string toTable, DateTimeOffset at, bool merged, string? byName) =>
        Create(branchId, merged ? "MASALAR BİRLEŞTİ" : "MASA DEĞİŞTİ", toTable, at,
            [$"{fromTable}  ->  {toTable}", merged ? $"{fromTable} siparişleri artık bu masada." : "Siparişleri yeni masaya götürün."], byName);

    private static PrintJob Create(Guid branchId, string heading, string tableName, DateTimeOffset at, string[] body, string? byName)
    {
        var text = new StringBuilder()
            .AppendLine(DoubleLine)
            .AppendLine(Center(heading))
            .AppendLine(DoubleLine)
            .AppendLine(LeftRight(Upper(tableName), Local(at).ToString("HH:mm")));
        if (byName is not null)
        {
            text.AppendLine($"Yapan: {byName}");
        }
        text.AppendLine(Line);
        foreach (var line in body)
        {
            text.AppendLine(line);
        }
        return new PrintJob(TicketKind.Kitchen, branchId, tableName, text.AppendLine(DoubleLine).ToString());
    }
}
