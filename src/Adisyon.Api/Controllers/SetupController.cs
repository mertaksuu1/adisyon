using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>
/// İlk kurulum sihirbazı: program boş bir veritabanıyla ilk açıldığında restoranı, şubeyi ve işletme
/// sahibini oluşturur, bu bilgisayarı bağlar ve sahibi giriş yapmış olarak döndürür.
/// Giriş istemez (henüz kimse yok); bu yüzden YALNIZCA hiç restoran yokken çalışır.
/// </summary>
[ApiController]
[Route("api/setup")]
[AllowAnonymous]
public class SetupController(
    AdisyonDbContext db,
    SecretHasher secretHasher,
    TokenService tokenService,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("status")]
    public async Task<SetupStatus> Status(CancellationToken cancellationToken) =>
        new(NeedsSetup: !await db.Tenants.AnyAsync(cancellationToken));

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Pairing)]
    public async Task<ActionResult<SetupResponse>> Setup(SetupRequest request, CancellationToken cancellationToken)
    {
        // Aynı anda iki kurulum isteği gelirse ikisi de "restoran yok" görmesin diye kilitli işlem içinde.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        if (await db.Tenants.AnyAsync(cancellationToken))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Bu sistem zaten kurulmuş. Yeni cihaz eklemek için yönetim ekranından eşleştirme kodu oluşturun.");
        }

        var now = timeProvider.GetUtcNow();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = request.RestaurantName.Trim(), Slug = Slugify(request.RestaurantName), CreatedAt = now };
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = request.BranchName.Trim(),
            // Rastgele bir kodla başlar; sahip yeni cihaz eklerken yönetim ekranından yenisini üretir.
            PairingCodeHash = SecretHasher.HashPairingCode(SecretHasher.NewPairingCode()),
        };
        var owner = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            DisplayName = request.OwnerName.Trim(),
            PinHash = secretHasher.HashPin(tenant.Id, request.OwnerPin),
            Role = UserRole.Owner,
            CreatedAt = now,
        };
        var deviceToken = SecretHasher.NewDeviceToken();
        var device = new Device
        {
            TenantId = tenant.Id,
            BranchId = branch.Id,
            Name = request.DeviceName.Trim(),
            TokenHash = SecretHasher.HashDeviceToken(deviceToken),
            CreatedAt = now,
            LastSeenAt = now,
        };

        db.AddRange(tenant, branch, owner, device);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var (token, expiresAt) = tokenService.CreateToken(owner, device);
        return new SetupResponse(
            new PairDeviceResponse(deviceToken, device.Id, tenant.Name, branch.Name),
            new LoginResponse(token, expiresAt, UserDto.From(owner)));
    }

    /// <summary>"Kebapçı Mehmet & Oğulları" → "kebapci-mehmet-ogullari"</summary>
    private static string Slugify(string name)
    {
        var ascii = name.Trim().ToLower(new CultureInfo("tr-TR"))
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        var slug = new StringBuilder();
        foreach (var c in ascii)
        {
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        var result = slug.ToString().Trim('-');
        return result.Length == 0 ? "restoran" : result[..Math.Min(result.Length, 100)];
    }
}

public record SetupStatus(bool NeedsSetup);

public record SetupRequest(
    [Required, MaxLength(200)] string RestaurantName,
    [Required, MaxLength(200)] string BranchName,
    [Required, MaxLength(100)] string OwnerName,
    [Required, RegularExpression(@"^\d{4}$", ErrorMessage = "PIN 4 rakamdan oluşmalı.")] string OwnerPin,
    [Required, MaxLength(100)] string DeviceName);

/// <summary>Kurulumu yapan bilgisayar hem eşleştirilmiş hem de sahip olarak giriş yapmış olur.</summary>
public record SetupResponse(PairDeviceResponse Device, LoginResponse Login);
