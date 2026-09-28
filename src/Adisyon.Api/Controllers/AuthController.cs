using System.ComponentModel.DataAnnotations;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Adisyon.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>
/// Giriş iki adımlıdır:
///   1) Cihaz eşleştirme (bir kez): şubenin eşleştirme kodu girilir, cihaz kalıcı bir anahtar alır.
///   2) PIN girişi (her vardiya): eşleştirilmiş cihazda 4 haneli PIN girilir, kullanıcı token'ı alınır.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    AdisyonDbContext db,
    TenantContext tenantContext,
    SecretHasher secretHasher,
    TokenService tokenService,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>Cihaz anahtarının gönderildiği HTTP başlığı.</summary>
    public const string DeviceTokenHeader = "X-Device-Token";

    public const int MaxFailedPinAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(1);

    [HttpPost("pair")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Pairing)]
    public async Task<ActionResult<PairDeviceResponse>> PairDevice(PairDeviceRequest request, CancellationToken cancellationToken)
    {
        // Eşleştirme anında kiracı bilinmiyor; şubeyi koddan buluyoruz. Filtreyi bu yüzden bilerek kapatıyoruz.
        var codeHash = SecretHasher.HashPairingCode(request.PairingCode);
        var branch = await db.Branches.IgnoreQueryFilters()
            .SingleOrDefaultAsync(b => b.PairingCodeHash == codeHash, cancellationToken);
        if (branch is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Eşleştirme kodu geçersiz.");
        }

        var tenant = await db.Tenants.SingleAsync(t => t.Id == branch.TenantId, cancellationToken);
        var deviceToken = SecretHasher.NewDeviceToken();
        var device = new Device
        {
            TenantId = branch.TenantId,
            BranchId = branch.Id,
            Name = request.DeviceName.Trim(),
            TokenHash = SecretHasher.HashDeviceToken(deviceToken),
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync(cancellationToken);

        // Anahtar yalnızca bu yanıtta bir kez gönderilir; cihaz onu saklar, sunucu yalnızca özetini tutar.
        return new PairDeviceResponse(deviceToken, device.Id, tenant.Name, branch.Name);
    }

    [HttpPost("pin-login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PinLogin)]
    public async Task<ActionResult<LoginResponse>> PinLogin(
        PinLoginRequest request,
        [FromHeader(Name = DeviceTokenHeader)] string? deviceToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            return DeviceNotRecognized();
        }

        var tokenHash = SecretHasher.HashDeviceToken(deviceToken);
        var device = await db.Devices.IgnoreQueryFilters()
            .SingleOrDefaultAsync(d => d.TokenHash == tokenHash && d.IsActive, cancellationToken);
        if (device is null)
        {
            return DeviceNotRecognized();
        }

        var now = timeProvider.GetUtcNow();
        if (device.LockedUntil > now)
        {
            var seconds = (int)Math.Ceiling((device.LockedUntil.Value - now).TotalSeconds);
            return Problem(statusCode: StatusCodes.Status429TooManyRequests,
                title: $"Çok fazla hatalı deneme. {seconds} saniye sonra tekrar deneyin.");
        }

        // Cihaz bulundu, artık hangi restoranda olduğumuzu biliyoruz. Bundan sonraki sorgular
        // otomatik olarak bu restoranla sınırlı.
        tenantContext.SetTenant(device.TenantId);

        var pinHash = secretHasher.HashPin(device.TenantId, request.Pin);
        var user = await db.Users.SingleOrDefaultAsync(u => u.PinHash == pinHash && u.IsActive, cancellationToken);
        // Şubeye bağlı personel yalnızca kendi şubesinin cihazından girebilir.
        var allowed = user is not null && (user.BranchId is null || user.BranchId == device.BranchId);

        if (!allowed)
        {
            device.FailedPinAttempts++;
            if (device.FailedPinAttempts >= MaxFailedPinAttempts)
            {
                device.LockedUntil = now + LockoutDuration;
                device.FailedPinAttempts = 0;
            }
            await db.SaveChangesAsync(cancellationToken);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "PIN hatalı.");
        }

        device.FailedPinAttempts = 0;
        device.LockedUntil = null;
        device.LastSeenAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var (token, expiresAt) = tokenService.CreateToken(user!, device);
        return new LoginResponse(token, expiresAt, UserDto.From(user!));
    }

    /// <summary>Token sahibinin bilgileri. Arayüz sayfa açılışında "kim giriş yapmış?" diye sorar.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirst(TokenService.ClaimNames.UserId)!.Value);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? NotFound() : UserDto.From(user);
    }

    private ObjectResult DeviceNotRecognized() =>
        Problem(statusCode: StatusCodes.Status401Unauthorized,
            title: "Bu cihaz tanınmadı. Şubenin eşleştirme koduyla yeniden eşleştirin.");
}

public record PairDeviceRequest(
    [Required, MaxLength(20)] string PairingCode,
    [Required, MaxLength(100)] string DeviceName);

public record PairDeviceResponse(string DeviceToken, Guid DeviceId, string TenantName, string BranchName);

public record PinLoginRequest(
    [Required, RegularExpression(@"^\d{4}$", ErrorMessage = "PIN 4 rakamdan oluşmalı.")] string Pin);

public record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);

public record UserDto(Guid Id, Guid TenantId, Guid? BranchId, string DisplayName, UserRole Role, bool IsActive)
{
    public static UserDto From(User u) => new(u.Id, u.TenantId, u.BranchId, u.DisplayName, u.Role, u.IsActive);
}
