using System.Text;
using Adisyon.Api.Domain;
using static Adisyon.Api.Printing.TicketFormat;

namespace Adisyon.Api.Printing;

/// <summary>
/// Hesap fişi (adisyon pusulası): müşteriye verilen, fiyatlı döküm. Yazar kasa (ÖKC) fişi değildir;
/// bu yüzden altında "MALİ DEĞERİ YOKTUR" yazar.
/// </summary>
public static class BillTicket
{
    /// <param name="session">Orders(Items), Payments ve Table yüklenmiş adisyon.</param>
    public static PrintJob Create(string restaurantName, string branchName, TableSession session, DateTimeOffset printedAt)
    {
        var text = new StringBuilder()
            .AppendLine(DoubleLine)
            .AppendLine(Center(Upper(restaurantName)))
            .AppendLine(Center(branchName))
            .AppendLine(DoubleLine)
            .AppendLine(LeftRight(Upper(session.Table!.Name), Local(printedAt).ToString("dd.MM.yyyy HH:mm")))
            .AppendLine(Line);

        // Aynı ürün birden çok siparişte girildiyse fişte tek satırda toplanır ("3 x Ayran").
        // İptaller fişte görünmez; ikramlar "(İKRAM)" olarak 0,00 ile görünür.
        var lines = session.Orders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items)
            .Where(i => i.VoidedAt is null)
            .GroupBy(i => (i.ProductName, i.UnitPrice, Comped: i.CompedAt is not null))
            .Select(g => (g.Key.ProductName, g.Key.UnitPrice, g.Key.Comped, Quantity: g.Sum(i => i.Quantity)));
        foreach (var (name, unitPrice, comped, quantity) in lines)
        {
            text.AppendLine(comped
                ? LeftRight($"{quantity} x {name} (İKRAM)", Money(0))
                : LeftRight($"{quantity} x {name}", Money(unitPrice * quantity)));
        }

        var total = SessionMoney.Total(session);
        text.AppendLine(Line).AppendLine(LeftRight("TOPLAM", Money(total)));
        foreach (var payment in session.Payments.OrderBy(p => p.CreatedAt))
        {
            text.AppendLine(LeftRight($"Ödenen ({(payment.Method == PaymentMethod.Cash ? "Nakit" : "Kart")})", Money(payment.Amount)));
        }
        var remaining = SessionMoney.Remaining(session);
        if (session.Payments.Count > 0 && remaining > 0)
        {
            text.AppendLine(LeftRight("KALAN", Money(remaining)));
        }

        text.AppendLine(DoubleLine)
            .AppendLine(Center("MALİ DEĞERİ YOKTUR"))
            .AppendLine(Center("Afiyet olsun!"));

        return new PrintJob(TicketKind.Bill, session.BranchId, session.Table.Name, text.ToString());
    }
}

/// <summary>Adisyonun para hesapları tek yerde: toplam, ödenen, kalan.</summary>
public static class SessionMoney
{
    public static decimal Total(TableSession session) =>
        session.Orders.Where(o => o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items).Where(i => i.IsCharged).Sum(i => i.UnitPrice * i.Quantity);

    public static decimal Paid(TableSession session) => session.Payments.Sum(p => p.Amount);

    public static decimal Remaining(TableSession session) => Total(session) - Paid(session);
}
