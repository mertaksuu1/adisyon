using Adisyon.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Adisyon.Api.Controllers;

/// <summary>
/// Sistemin ayakta olup olmadığını söyler. Web arayüzü, izleme araçları ve testler kullanır.
/// </summary>
[ApiController]
[Route("api/health")]
public class HealthController(AdisyonDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<HealthResponse> Get(CancellationToken cancellationToken)
    {
        var databaseOk = await db.Database.CanConnectAsync(cancellationToken);
        return new HealthResponse(Api: "ok", Database: databaseOk ? "ok" : "unreachable");
    }
}

public record HealthResponse(string Api, string Database);
