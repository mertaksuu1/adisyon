using System.ComponentModel.DataAnnotations;
using Adisyon.Api.Ai;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>Yapay zeka özellikleri (internet ve API anahtarı gerektirir). Yalnızca işletme sahibi ve yönetici.</summary>
[ApiController]
[Route("api/ai")]
[Authorize(Roles = RoleNames.Management)]
public class AiController(AdisyonDbContext db, AiAssistant assistant, SalesQueries queries, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("status")]
    public AiStatus Status() => new(assistant.IsConfigured);

    /// <summary>Z raporunun kısa Türkçe yorumu: dün ve geçen haftanın aynı günüyle karşılaştırmalı.</summary>
    [HttpPost("z-summary")]
    public async Task<AiResult> DaySummary([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        var day = date ?? BusinessDay.Of(timeProvider.GetUtcNow());
        var today = await ZReport.BuildAsync(db, branchId, day, cancellationToken);
        var yesterday = await ZReport.BuildAsync(db, branchId, day.AddDays(-1), cancellationToken);
        var lastWeek = await ZReport.BuildAsync(db, branchId, day.AddDays(-7), cancellationToken);
        return await assistant.SummarizeDayAsync(today, yesterday, lastWeek, cancellationToken);
    }

    /// <summary>Satış verilerine Türkçe soru: "Geçen ay en çok ne sattı?"</summary>
    [HttpPost("ask")]
    public async Task<AiResult> Ask(AskRequest request, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        var branch = await db.Branches.SingleAsync(b => b.Id == branchId, cancellationToken);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == branch.TenantId, cancellationToken);

        return await assistant.AskAsync(
            request.Question.Trim(),
            BusinessDay.Of(timeProvider.GetUtcNow()),
            tenant.Name,
            // Claude'un çağırdığı her araç bu şubeyle sınırlı çalışır.
            (name, input) => ReportTools.RunAsync(queries, branchId, name, input, cancellationToken),
            cancellationToken);
    }
}

public record AiStatus(bool Configured);

public record AskRequest([Required, MaxLength(500)] string Question);
