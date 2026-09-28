using System.Net;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Adisyon.Api.Tests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_reports_api_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body!.Api);
        // Veritabanı durumu burada kontrol edilmiyor: test Docker kapalıyken de geçmeli.
        // Gerçek veritabanıyla testler Faz 1'de Testcontainers ile gelecek.
        Assert.Contains(body.Database, new[] { "ok", "unreachable" });
    }
}
