using System.Text;
using Adisyon.Api.Data;
using Microsoft.EntityFrameworkCore;
using static Adisyon.Api.Printing.TicketFormat;

namespace Adisyon.Api.Printing;

/// <summary>
/// Fişleri şubenin yazıcı ayarına göre yazdırır ve her fişi (sonucuyla) kaydeder:
///   mutfak fişleri (mutfak, iptal, masa değişikliği) → mutfak yazıcısı
///   hesap fişi ve Z raporu → kasa yazıcısı; o yoksa mutfak yazıcısı
///   ilgili yazıcı ayarlanmamışsa → yalnızca önizleme (Fişler sayfası)
/// Yazıcı hatası hiçbir zaman işlemi (sipariş, ödeme) geri almaz; sonuç "Failed" olarak döner ve fiş
/// Fişler sayfasından tekrar yazdırılabilir.
/// </summary>
public class PrintService(
    AdisyonDbContext db,
    TicketLog log,
    NetworkPrinterClient network,
    TimeProvider timeProvider,
    ILogger<PrintService> logger) : IPrinter
{
    public async Task<PrintedTicket> PrintAsync(PrintJob job, CancellationToken cancellationToken)
    {
        var ticket = new PrintedTicket(Guid.CreateVersion7(), job.Kind, job.BranchId, job.Title, timeProvider.GetUtcNow(), job.Text);
        return await SendAndSaveAsync(ticket, cancellationToken);
    }

    /// <summary>Kayıtlı bir fişi yeniden gönderir (ör. yazıcının kağıdı bitmişti). Bulunamazsa null.</summary>
    public async Task<PrintedTicket?> ReprintAsync(Guid ticketId, Guid branchId, CancellationToken cancellationToken)
    {
        var ticket = log.Find(ticketId, branchId);
        return ticket is null ? null : await SendAndSaveAsync(ticket, cancellationToken);
    }

    private async Task<PrintedTicket> SendAndSaveAsync(PrintedTicket ticket, CancellationToken cancellationToken)
    {
        // Yazıcı ayarı şubede; istek hangi kiracı adına gelirse gelsin fişin kendi şubesine bakıyoruz.
        var branch = await db.Branches.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == ticket.BranchId, cancellationToken);
        var address = AddressFor(branch, ticket.Kind);

        var result = ticket with { Status = PrintStatus.Preview, Error = null };
        if (address is not null)
        {
            try
            {
                var bytes = EscPos.Encode(new PrintJob(ticket.Kind, ticket.BranchId, ticket.Title, ticket.Text), branch!.PrinterCodePage);
                await network.SendAsync(address, bytes, cancellationToken);
                result = result with { Status = PrintStatus.Printed };
            }
            catch (PrinterUnavailableException ex)
            {
                logger.LogWarning(ex, "Fiş yazdırılamadı: {Title} → {Address}", ticket.Title, address);
                result = result with { Status = PrintStatus.Failed, Error = ex.Message };
            }
        }

        log.Save(result);
        return result;
    }

    private static string? AddressFor(Domain.Branch? branch, TicketKind kind)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (branch is null)
        {
            return null;
        }
        return kind == TicketKind.Kitchen
            ? Clean(branch.KitchenPrinterAddress)
            : Clean(branch.ReceiptPrinterAddress) ?? Clean(branch.KitchenPrinterAddress);
    }

    /// <summary>Kurulumda yazıcıyı denemek için: Türkçe karakterlerin doğru çıkıp çıkmadığı bir bakışta görülür.</summary>
    public static PrintJob TestTicket(Guid branchId, TicketKind kind, string branchName, DateTimeOffset now)
    {
        var text = new StringBuilder()
            .AppendLine(DoubleLine)
            .AppendLine(Center(kind == TicketKind.Kitchen ? "MUTFAK YAZICISI TESTİ" : "KASA YAZICISI TESTİ"))
            .AppendLine(DoubleLine)
            .AppendLine(LeftRight(branchName, Local(now).ToString("dd.MM.yyyy HH:mm")))
            .AppendLine(Line)
            .AppendLine("Türkçe karakterler:")
            .AppendLine("  ÇĞİÖŞÜ  çğıöşü")
            .AppendLine("  Şiş kebap, çiğ köfte, ığdır, İzmir")
            .AppendLine(Line)
            .AppendLine("Harfler bozuksa: Yönetim > Yazıcılar >")
            .AppendLine("karakter tablosu numarasını değiştirin.")
            .AppendLine(new string('.', PaperWidth))
            .AppendLine(LeftRight("48 karakter:", "sağ kenar|"))
            .AppendLine(DoubleLine)
            .ToString();
        return new PrintJob(kind, branchId, kind == TicketKind.Kitchen ? "Mutfak yazıcısı testi" : "Kasa yazıcısı testi", text);
    }
}
