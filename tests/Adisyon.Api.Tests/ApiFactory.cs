using Adisyon.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Adisyon.Api.Tests;

/// <summary>
/// Testler için API'yi bellekte başlatır ve Docker'da geçici bir PostgreSQL açar.
/// Tüm test sınıfları aynı örneği paylaşır (ApiCollection), böylece konteyner bir kez açılır.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" ortamında demo veri yükleyici çalışmaz; testler kendi verisini kurar.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Adisyon", _postgres.GetConnectionString());
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AdisyonDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public class ApiCollection : ICollectionFixture<ApiFactory>;
