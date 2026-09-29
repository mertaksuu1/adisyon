using System.Net.NetworkInformation;
using System.Net.Sockets;
using Adisyon.Api.Auth;
using Adisyon.Api.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;

namespace Adisyon.Api.Controllers;

/// <summary>Kurulum ve bakım bilgileri: diğer cihazların açacağı adres, yedekler.</summary>
[ApiController]
[Route("api/system")]
[Authorize(Roles = RoleNames.Management)]
public class SystemController(BackupService backups, IServer server) : ControllerBase
{
    /// <summary>
    /// Bu bilgisayarın yerel ağdaki adresleri, ör. http://192.168.1.20:5000. Restorandaki tablet ve diğer
    /// bilgisayarlar tarayıcılarında bu adresi açar.
    /// </summary>
    [HttpGet("info")]
    public SystemInfo Info()
    {
        var port = server.Features.Get<IServerAddressesFeature>()?.Addresses
            .Select(a => Uri.TryCreate(a.Replace("0.0.0.0", "localhost").Replace("[::]", "localhost").Replace("+", "localhost").Replace("*", "localhost"), UriKind.Absolute, out var uri) ? uri.Port : (int?)null)
            .FirstOrDefault(p => p is not null) ?? 5000;

        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => $"http://{a.Address}:{port}")
            .Distinct()
            .ToList();

        return new SystemInfo(addresses, backups.List().FirstOrDefault(), backups.BackupDirectory);
    }

    [HttpGet("backups")]
    public List<BackupInfo> Backups() => backups.List();

    [HttpPost("backups")]
    public Task<BackupInfo> BackupNow(CancellationToken cancellationToken) => backups.BackupNowAsync(cancellationToken);
}

public record SystemInfo(List<string> Addresses, BackupInfo? LastBackup, string BackupDirectory);
