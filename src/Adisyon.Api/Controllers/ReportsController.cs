using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Printing;
using Adisyon.Api.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>Gün sonu (Z) raporu. Yalnızca işletme sahibi ve yönetici görebilir.</summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = RoleNames.Management)]
public class ReportsController(AdisyonDbContext db, IPrinter printer, TimeProvider timeProvider) : ControllerBase
{
    /// <summary>Bir iş gününün raporu. Tarih verilmezse bugünkü iş günü (05:00'ten önceyse dünkü).</summary>
    [HttpGet("z")]
    public Task<ZReport> Get([FromQuery] DateOnly? date, CancellationToken cancellationToken) =>
        ZReport.BuildAsync(db, User.GetBranchId(), date ?? BusinessDay.Of(timeProvider.GetUtcNow()), cancellationToken);

    [HttpPost("z/print")]
    public async Task<ActionResult<PrintResult>> Print([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        var report = await ZReport.BuildAsync(db, branchId, date ?? BusinessDay.Of(timeProvider.GetUtcNow()), cancellationToken);
        var branch = await db.Branches.SingleAsync(b => b.Id == branchId, cancellationToken);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == branch.TenantId, cancellationToken);

        var ticket = await printer.PrintAsync(ZReportTicket.Create(tenant.Name, branch.Name, report, timeProvider.GetUtcNow(), branchId), cancellationToken);
        return PrintResult.From(ticket);
    }
}
