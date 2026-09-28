using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>Restorana bağlı bilgisayar ve tabletler. Kaybolan/çalınan cihazın bağlantısı buradan kesilir.</summary>
[ApiController]
[Route("api/devices")]
[Authorize(Roles = RoleNames.Management)]
public class DevicesController(AdisyonDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<DeviceDto>> List(CancellationToken cancellationToken)
    {
        var currentDeviceId = CurrentDeviceId();
        return await db.Devices
            .OrderByDescending(d => d.IsActive).ThenByDescending(d => d.LastSeenAt)
            .Select(d => new DeviceDto(d.Id, d.Name, d.IsActive, d.CreatedAt, d.LastSeenAt, d.Id == currentDeviceId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Cihazın bağlantısını keser: o cihazda artık PIN ile giriş yapılamaz, yeniden eşleştirmek gerekir.
    /// </summary>
    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        if (id == CurrentDeviceId())
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Şu an kullandığınız cihazın bağlantısını kesemezsiniz.");
        }

        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        device.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Guid? CurrentDeviceId() =>
        Guid.TryParse(User.FindFirst(TokenService.ClaimNames.DeviceId)?.Value, out var id) ? id : null;
}

/// <param name="IsCurrent">İsteği yapan cihaz mı? (Ekranda "bu cihaz" olarak gösterilir, bağlantısı kesilemez.)</param>
public record DeviceDto(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt, bool IsCurrent);
