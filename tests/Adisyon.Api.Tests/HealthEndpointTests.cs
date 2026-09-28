using System.Net;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;

namespace Adisyon.Api.Tests;

[Collection(nameof(ApiCollection))]
public class HealthEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_endpoint_reports_api_and_database_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body!.Api);
        Assert.Equal("ok", body.Database);
    }
}
