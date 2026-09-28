using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Adisyon.Api.Controllers;

/// <summary>
/// Sistemin ayakta olup olmadığını söyler. Web arayüzü, izleme araçları ve testler kullanır.
/// </summary>
[ApiController]
[Route("api/health")]
public class HealthController(NpgsqlDataSource dataSource, ILogger<HealthController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<HealthResponse> Get(CancellationToken cancellationToken)
    {
        return new HealthResponse(Api: "ok", Database: await CheckDatabaseAsync(cancellationToken));
    }

    private async Task<string> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return "ok";
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            logger.LogWarning(ex, "Veritabanına bağlanılamadı");
            return "unreachable";
        }
    }
}

public record HealthResponse(string Api, string Database);
