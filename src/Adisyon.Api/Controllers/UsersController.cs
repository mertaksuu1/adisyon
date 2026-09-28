using System.ComponentModel.DataAnnotations;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Adisyon.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>Personel yönetimi. Yalnızca işletme sahibi ve yöneticiler erişebilir.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = RoleNames.Management)]
public class UsersController(
    AdisyonDbContext db,
    TenantContext tenantContext,
    SecretHasher secretHasher,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>Restoranın tüm personeli. Kiracı filtresi sayesinde yalnızca kendi restoranınızınkiler gelir.</summary>
    [HttpGet]
    public async Task<List<UserDto>> List(CancellationToken cancellationToken)
    {
        var users = await db.Users.OrderBy(u => u.DisplayName).ToListAsync(cancellationToken);
        return users.Select(UserDto.From).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        // Yöneticiler garson/mutfak/kasa ekleyebilir; sahip veya yönetici eklemek sadece sahibin yetkisinde.
        if (request.Role is UserRole.Owner or UserRole.Manager && !User.IsInRole(RoleNames.Owner))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Sahip veya yönetici hesabını yalnızca işletme sahibi oluşturabilir.");
        }

        if (request.BranchId is { } branchId
            && !await db.Branches.AnyAsync(b => b.Id == branchId, cancellationToken))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Şube bulunamadı.");
        }

        var pinHash = secretHasher.HashPin(tenantContext.TenantId!.Value, request.Pin);
        if (await db.Users.AnyAsync(u => u.PinHash == pinHash, cancellationToken))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Bu PIN restoranınızda başka bir personel tarafından kullanılıyor.");
        }

        var user = new User
        {
            DisplayName = request.DisplayName.Trim(),
            PinHash = pinHash,
            Role = request.Role,
            BranchId = request.BranchId,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        db.Users.Add(user); // TenantId, giriş yapan kullanıcının restoranıyla otomatik dolar.
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), UserDto.From(user));
    }
}

public record CreateUserRequest(
    [Required, MaxLength(100)] string DisplayName,
    [Required, RegularExpression(@"^\d{4}$", ErrorMessage = "PIN 4 rakamdan oluşmalı.")] string Pin,
    UserRole Role,
    Guid? BranchId);
