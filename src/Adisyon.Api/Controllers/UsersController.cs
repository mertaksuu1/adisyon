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

    /// <summary>Personelin adını, rolünü, şubesini veya aktifliğini değiştirir (işten ayrılan: pasif).</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }
        if (CannotManage(user) || (request.Role is UserRole.Owner or UserRole.Manager && !User.IsInRole(RoleNames.Owner)))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Sahip ve yönetici hesaplarını yalnızca işletme sahibi düzenleyebilir.");
        }

        var isSelf = user.Id == User.GetUserId();
        if (isSelf && (!request.IsActive || request.Role != user.Role))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Kendi hesabınızı pasife alamaz veya rolünüzü değiştiremezsiniz.");
        }
        if (request.BranchId is { } branchId && !await db.Branches.AnyAsync(b => b.Id == branchId, cancellationToken))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Şube bulunamadı.");
        }

        // Restoranda en az bir aktif sahip kalmalı; yoksa kimse personel ve cihaz yönetemez.
        var losesOwner = user.Role == UserRole.Owner && user.IsActive && (!request.IsActive || request.Role != UserRole.Owner);
        if (losesOwner && !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Owner && u.IsActive, cancellationToken))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Restoranda en az bir aktif işletme sahibi kalmalı.");
        }

        user.DisplayName = request.DisplayName.Trim();
        user.Role = request.Role;
        user.BranchId = request.BranchId;
        user.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return UserDto.From(user);
    }

    /// <summary>PIN'i değiştirir (ör. unutulduğunda). Yönetici kendi PIN'ini de değiştirebilir.</summary>
    [HttpPut("{id:guid}/pin")]
    public async Task<IActionResult> ChangePin(Guid id, ChangePinRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }
        if (CannotManage(user) && user.Id != User.GetUserId())
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Sahip ve yönetici PIN'lerini yalnızca işletme sahibi değiştirebilir.");
        }

        var pinHash = secretHasher.HashPin(user.TenantId, request.Pin);
        if (await db.Users.AnyAsync(u => u.Id != user.Id && u.PinHash == pinHash, cancellationToken))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Bu PIN restoranınızda başka bir personel tarafından kullanılıyor.");
        }

        user.PinHash = pinHash;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Yönetici, sahip veya başka bir yöneticinin hesabına dokunamaz.</summary>
    private bool CannotManage(User target) =>
        !User.IsInRole(RoleNames.Owner) && target.Role is UserRole.Owner or UserRole.Manager;
}

public record UpdateUserRequest(
    [Required, MaxLength(100)] string DisplayName,
    UserRole Role,
    Guid? BranchId,
    bool IsActive = true);

public record ChangePinRequest(
    [Required, RegularExpression(@"^\d{4}$", ErrorMessage = "PIN 4 rakamdan oluşmalı.")] string Pin);

public record CreateUserRequest(
    [Required, MaxLength(100)] string DisplayName,
    [Required, RegularExpression(@"^\d{4}$", ErrorMessage = "PIN 4 rakamdan oluşmalı.")] string Pin,
    UserRole Role,
    Guid? BranchId);
