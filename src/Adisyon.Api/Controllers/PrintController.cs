using System.ComponentModel.DataAnnotations;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>Fişler: son fişler, tekrar yazdırma, yazıcı ayarları ve test fişi.</summary>
[ApiController]
[Route("api/print")]
public class PrintController(TicketLog log, PrintService printer, AdisyonDbContext db, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("recent")]
    [Authorize(Roles = RoleNames.Management + "," + RoleNames.Kitchen)]
    public List<PrintedTicket> Recent() => log.Recent(User.GetBranchId());

    /// <summary>
    /// Bir fişi tekrar yazdırır (ör. kağıt bitmişti). Sipariş giren herkes kullanabilir: garson kendi
    /// siparişinin fişi basılamadıysa tekrar gönderebilmeli.
    /// </summary>
    [HttpPost("{id:guid}/reprint")]
    [Authorize(Roles = RoleNames.FrontOfHouse + "," + RoleNames.Kitchen)]
    public async Task<ActionResult<PrintResult>> Reprint(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await printer.ReprintAsync(id, User.GetBranchId(), cancellationToken);
        return ticket is null ? NotFound() : PrintResult.From(ticket);
    }

    [HttpGet("settings")]
    [Authorize(Roles = RoleNames.Management)]
    public async Task<PrinterSettings> Settings(CancellationToken cancellationToken)
    {
        var branch = await CurrentBranchAsync(cancellationToken);
        return new PrinterSettings(branch.KitchenPrinterAddress, branch.ReceiptPrinterAddress, branch.PrinterCodePage);
    }

    [HttpPut("settings")]
    [Authorize(Roles = RoleNames.Management)]
    public async Task<PrinterSettings> SaveSettings(PrinterSettings request, CancellationToken cancellationToken)
    {
        var branch = await CurrentBranchAsync(cancellationToken);
        branch.KitchenPrinterAddress = Clean(request.KitchenPrinterAddress);
        branch.ReceiptPrinterAddress = Clean(request.ReceiptPrinterAddress);
        branch.PrinterCodePage = request.PrinterCodePage;
        await db.SaveChangesAsync(cancellationToken);
        return new PrinterSettings(branch.KitchenPrinterAddress, branch.ReceiptPrinterAddress, branch.PrinterCodePage);
    }

    /// <summary>Test fişi: kurulumda yazıcının bağlandığını ve Türkçe karakterlerin doğru çıktığını görmek için.</summary>
    [HttpPost("test")]
    [Authorize(Roles = RoleNames.Management)]
    public async Task<PrintResult> Test([FromQuery] string target, CancellationToken cancellationToken)
    {
        var branch = await CurrentBranchAsync(cancellationToken);
        var kind = target == "receipt" ? TicketKind.Bill : TicketKind.Kitchen;
        var ticket = await printer.PrintAsync(PrintService.TestTicket(branch.Id, kind, branch.Name, timeProvider.GetUtcNow()), cancellationToken);
        return PrintResult.From(ticket);
    }

    private Task<Domain.Branch> CurrentBranchAsync(CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        return db.Branches.SingleAsync(b => b.Id == branchId, cancellationToken);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <param name="KitchenPrinterAddress">Ör. "192.168.1.50" veya "192.168.1.50:9100"; boşsa yalnızca önizleme.</param>
/// <param name="ReceiptPrinterAddress">Boşsa hesap fişi ve Z raporu da mutfak yazıcısından çıkar.</param>
/// <param name="PrinterCodePage">ESC/POS karakter tablosu numarası; Türkçe (PC857) çoğu yazıcıda 13.</param>
public record PrinterSettings(
    [MaxLength(100), RegularExpression(@"^[A-Za-z0-9.\-]+(:\d{1,5})?$", ErrorMessage = "Adres 192.168.1.50 veya 192.168.1.50:9100 biçiminde olmalı.")] string? KitchenPrinterAddress,
    [MaxLength(100), RegularExpression(@"^[A-Za-z0-9.\-]+(:\d{1,5})?$", ErrorMessage = "Adres 192.168.1.50 veya 192.168.1.50:9100 biçiminde olmalı.")] string? ReceiptPrinterAddress,
    [Range(0, 255)] int PrinterCodePage = 13);
