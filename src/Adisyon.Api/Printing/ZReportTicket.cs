using System.Text;
using Adisyon.Api.Reports;
using static Adisyon.Api.Printing.TicketFormat;

namespace Adisyon.Api.Printing;

/// <summary>Z raporunun kâğıt hâli. Yazar kasa (ÖKC) Z raporu değildir; işletme içi kontrol içindir.</summary>
public static class ZReportTicket
{
    public static PrintJob Create(string restaurantName, string branchName, ZReport report, DateTimeOffset printedAt, Guid branchId)
    {
        var text = new StringBuilder()
            .AppendLine(DoubleLine)
            .AppendLine(Center("GÜN SONU RAPORU"))
            .AppendLine(Center(Upper(restaurantName)))
            .AppendLine(Center(branchName))
            .AppendLine(DoubleLine)
            .AppendLine(LeftRight("İş günü", report.Date.ToString("dd.MM.yyyy")))
            .AppendLine(LeftRight("Aralık", $"{Local(report.From):dd.MM HH:mm} - {Local(report.To):dd.MM HH:mm}"))
            .AppendLine(LeftRight("Yazdırma", Local(printedAt).ToString("dd.MM.yyyy HH:mm")))
            .AppendLine(Line)
            .AppendLine("TAHSİLAT")
            .AppendLine(LeftRight("  Nakit", Money(report.CashTotal)))
            .AppendLine(LeftRight("  Kart", Money(report.CardTotal)))
            .AppendLine(LeftRight("  TOPLAM", Money(report.PaymentsTotal)))
            .AppendLine(Line)
            .AppendLine("SATIŞ")
            .AppendLine(LeftRight("  Kapanan adisyon", report.ClosedSessionCount.ToString()))
            .AppendLine(LeftRight("  Satış toplamı", Money(report.SalesTotal)))
            .AppendLine(LeftRight("  Ortalama hesap", Money(report.AverageBill)))
            .AppendLine(Line);

        AppendAdjustments(text, "İPTALLER", report.Voids, report.VoidsTotal);
        AppendAdjustments(text, "İKRAMLAR", report.Comps, report.CompsTotal);

        text.AppendLine("EN ÇOK SATANLAR");
        foreach (var p in report.TopProducts)
        {
            text.AppendLine(LeftRight($"  {p.Quantity} x {p.ProductName}", Money(p.Amount)));
        }
        if (report.TopProducts.Count == 0)
        {
            text.AppendLine("  (satış yok)");
        }

        if (report.OpenTableCount > 0)
        {
            text.AppendLine(DoubleLine)
                .AppendLine(Center("!!! DİKKAT !!!"))
                .AppendLine(LeftRight($"Açık masa: {report.OpenTableCount}", $"Ödenmemiş {Money(report.OpenTablesTotal)}"));
        }

        text.AppendLine(DoubleLine)
            .AppendLine(Center("MALİ DEĞERİ YOKTUR"));
        return new PrintJob(TicketKind.Report, branchId, $"Z {report.Date:dd.MM.yyyy}", text.ToString());
    }

    private static void AppendAdjustments(StringBuilder text, string title, List<AdjustmentLine> lines, decimal total)
    {
        text.AppendLine(LeftRight($"{title} ({lines.Count})", Money(total)));
        foreach (var l in lines)
        {
            text.AppendLine(LeftRight($"  {Local(l.At):HH:mm} {l.TableName} {l.Quantity} x {l.ProductName}", Money(l.Amount)));
            if (l.ByName is not null)
            {
                text.AppendLine($"        {l.ByName}");
            }
        }
        text.AppendLine(Line);
    }
}
