using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Adisyon.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Adisyon.Api.Tests;

/// <summary>
/// SaaS'ın en önemli güvencesi: bir kiracı diğerinin verisini asla göremez ve değiştiremez.
/// </summary>
[Collection(nameof(ApiCollection))]
public class TenantIsolationTests(ApiFactory factory)
{
    [Fact]
    public async Task Queries_only_return_current_tenants_data()
    {
        var (tenantA, tenantB) = await CreateTwoTenantsWithProductsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();

        var products = await db.Products.ToListAsync();

        Assert.NotEmpty(products);
        Assert.All(products, p => Assert.Equal(tenantA, p.TenantId));
        Assert.DoesNotContain(products, p => p.TenantId == tenantB);
    }

    [Fact]
    public async Task Without_a_tenant_nothing_is_visible()
    {
        await CreateTwoTenantsWithProductsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();

        Assert.Empty(await db.Products.ToListAsync());
    }

    [Fact]
    public async Task New_records_get_current_tenant_automatically()
    {
        var (tenantA, _) = await CreateTwoTenantsWithProductsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();

        var category = new Category { Name = "Kahvaltı" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        Assert.Equal(tenantA, category.TenantId);
    }

    [Fact]
    public async Task Cannot_create_records_for_another_tenant()
    {
        var (tenantA, tenantB) = await CreateTwoTenantsWithProductsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();

        db.Categories.Add(new Category { TenantId = tenantB, Name = "Sızma denemesi" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Only_one_open_session_per_table()
    {
        var (tenantA, _) = await CreateTwoTenantsWithProductsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();
        var table = await db.Tables.FirstAsync();

        db.TableSessions.Add(new TableSession { BranchId = table.BranchId, TableId = table.Id, OpenedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.TableSessions.Add(new TableSession { BranchId = table.BranchId, TableId = table.Id, OpenedAt = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>İki ayrı kiracı, her birinde bir şube, bir masa, bir kategori ve bir ürün oluşturur.</summary>
    private async Task<(Guid TenantA, Guid TenantB)> CreateTwoTenantsWithProductsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();

        var ids = new List<Guid>();
        foreach (var name in new[] { "A", "B" })
        {
            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = $"Restoran {name}",
                Slug = $"test-{Guid.NewGuid():N}",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Şube" };
            var category = new Category { TenantId = tenant.Id, Name = "Menü" };
            category.Products.Add(new Product { TenantId = tenant.Id, Name = $"Ürün {name}", Price = 100m });

            db.Tenants.Add(tenant);
            db.Branches.Add(branch);
            db.Tables.Add(new DiningTable { TenantId = tenant.Id, BranchId = branch.Id, Name = "Masa 1", QrToken = DevDataSeeder.NewQrToken() });
            db.Categories.Add(category);
            ids.Add(tenant.Id);
        }

        await db.SaveChangesAsync();
        return (ids[0], ids[1]);
    }
}
