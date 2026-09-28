using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

[ApiController]
[Route("api/branches")]
[Authorize(Roles = RoleNames.Management)]
public class BranchesController(AdisyonDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<List<BranchDto>> List(CancellationToken cancellationToken) =>
        db.Branches.OrderBy(b => b.Name).Select(b => new BranchDto(b.Id, b.Name, b.Address)).ToListAsync(cancellationToken);

    /// <summary>
    /// Şube için yeni bir eşleştirme kodu üretir ve bir kez gösterir. Eski kod geçersiz olur;
    /// daha önce eşleştirilmiş cihazlar çalışmaya devam eder.
    /// </summary>
    [HttpPost("{branchId:guid}/pairing-code")]
    public async Task<ActionResult<PairingCodeResponse>> RegeneratePairingCode(Guid branchId, CancellationToken cancellationToken)
    {
        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        if (branch is null)
        {
            return NotFound();
        }

        var code = SecretHasher.NewPairingCode();
        branch.PairingCodeHash = SecretHasher.HashPairingCode(code);
        await db.SaveChangesAsync(cancellationToken);

        return new PairingCodeResponse(code);
    }
}

public record PairingCodeResponse(string PairingCode);

public record BranchDto(Guid Id, string Name, string? Address);
