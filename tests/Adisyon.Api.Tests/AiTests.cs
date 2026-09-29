using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Adisyon.Api.Ai;
using Adisyon.Api.Controllers;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Adisyon.Api.Reports;
using Adisyon.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Adisyon.Api.Tests;

/// <summary>
/// Yapay zeka özellikleri. Testler gerçek Claude API'sini çağırmaz (anahtar yok, ücretli); Claude'un kullandığı
/// rapor araçlarının doğru rakamları verdiğini ve araç tanımlarının API'nin beklediği biçimde olduğunu kanıtlar.
/// </summary>
[Collection(nameof(ApiCollection))]
public class AiTests(ApiFactory factory)
{
    [Fact]
    public async Task Without_an_api_key_ai_says_unavailable_and_nothing_breaks()
    {
        var (owner, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);

        var status = await owner.GetFromJsonAsync<AiStatus>("/api/ai/status", ApiFactory.JsonOptions);
        var summary = await (await owner.PostAsync("/api/ai/z-summary", null)).Content.ReadFromJsonAsync<AiResult>(ApiFactory.JsonOptions);
        var answer = await (await owner.PostAsJsonAsync("/api/ai/ask", new AskRequest("Bugün ne sattık?"), ApiFactory.JsonOptions))
            .Content.ReadFromJsonAsync<AiResult>(ApiFactory.JsonOptions);

        Assert.False(status!.Configured);
        Assert.False(summary!.Available);
        Assert.Equal(AiPackage.NotEnabledMessage, summary.Text);
        Assert.False(answer!.Available);
    }

    [Fact]
    public async Task Only_owner_and_manager_can_use_ai()
    {
        var (waiter, _) = await factory.CreateTenantAndLoginAsync(UserRole.Waiter);

        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsJsonAsync("/api/ai/ask", new AskRequest("Ciro?"), ApiFactory.JsonOptions)).StatusCode);
    }

    [Fact]
    public void Tool_definitions_match_the_api_format()
    {
        var json = JsonSerializer.SerializeToElement(ReportTools.Definitions);

        Assert.Equal(["sales_summary", "top_products", "sales_by_hour", "sales_by_staff", "adjustments"],
            json.EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        foreach (var tool in json.EnumerateArray())
        {
            Assert.True(tool.GetProperty("strict").GetBoolean());
            var schema = tool.GetProperty("input_schema");
            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            // strict modda her alan "required" listesinde olmalı
            var properties = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order();
            var required = schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()!).Order();
            Assert.Equal(properties, required);
        }
    }

    [Fact]
    public async Task Report_tools_return_the_right_numbers()
    {
        // Gün: 2 kebap (760) + 2 ayran (80) → 1 ayran iptal, masa 800'e kapanır: 500 kart + 300 nakit.
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var waiter = await factory.LoginAsync(restaurant, UserRole.Waiter);
        var category = await MenuTests.CreateCategoryAsync(owner, "Menü");
        var kebap = await MenuTests.CreateProductAsync(owner, category.Id, "Adana Kebap", 380m);
        var ayran = await MenuTests.CreateProductAsync(owner, category.Id, "Ayran", 40m);
        var table = await Post<TableDto>(owner, "/api/tables", new SaveTableRequest("Masa 1"));
        var s = await Post<SessionDto>(waiter, $"/api/tables/{table.Id}/session", null);
        s = await Post<SessionDto>(waiter, $"/api/sessions/{s.Id}/orders", new AddOrderRequest([new AddOrderItem(kebap.Id, 2), new AddOrderItem(ayran.Id, 2)]));
        var ayranLine = s.Orders.Single().Items.Single(i => i.ProductName == "Ayran");
        s = await Post<SessionDto>(owner, $"/api/sessions/{s.Id}/items/{ayranLine.Id}/void", new AdjustItemRequest(1, s.Version));
        s = await Post<SessionDto>(owner, $"/api/sessions/{s.Id}/payments", new PayRequest(PaymentMethod.Card, s.Version, 500m));
        await Post<SessionDto>(owner, $"/api/sessions/{s.Id}/payments", new PayRequest(PaymentMethod.Cash, s.Version));

        var today = BusinessDay.Of(DateTimeOffset.UtcNow);
        var range = JsonSerializer.SerializeToElement(new { from_date = today.AddDays(-2).ToString("yyyy-MM-dd"), to_date = today.ToString("yyyy-MM-dd") });

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(restaurant.TenantId);
        var queries = scope.ServiceProvider.GetRequiredService<SalesQueries>();
        Task<object> Run(string tool, JsonElement input) => ReportTools.RunAsync(queries, restaurant.BranchId, tool, input, CancellationToken.None);

        var summary = (SalesSummary)await Run("sales_summary", range);
        Assert.Equal((1, 800m, 300m, 500m), (summary.ClosedBills, summary.Sales, summary.Cash, summary.Card));
        Assert.Equal(3, summary.Days.Count); // üç günün dökümü; ikisi boş
        Assert.Equal(800m, summary.Days.Single(d => d.Date == today).Sales);

        var top = (List<ProductSales>)await Run("top_products", JsonSerializer.SerializeToElement(new
        {
            from_date = today.ToString("yyyy-MM-dd"), to_date = today.ToString("yyyy-MM-dd"), limit = 5, order_by = "quantity",
        }));
        Assert.Equal([("Adana Kebap", 2, 760m), ("Ayran", 1, 40m)], top.Select(p => (p.ProductName, p.Quantity, p.Revenue)));

        var staff = (List<StaffSales>)await Run("sales_by_staff", range);
        Assert.Equal(("Test Waiter", 1, 800m), (staff.Single().StaffName, staff.Single().Orders, staff.Single().Revenue));

        var voids = (List<AdjustmentLine>)await Run("adjustments", JsonSerializer.SerializeToElement(new
        {
            from_date = today.ToString("yyyy-MM-dd"), to_date = today.ToString("yyyy-MM-dd"), kind = "void",
        }));
        Assert.Equal(("Ayran", 40m, "Test Owner"), (voids.Single().ProductName, voids.Single().Amount, voids.Single().ByName));

        var hours = (List<HourlySales>)await Run("sales_by_hour", range);
        Assert.Equal(800m, hours.Sum(h => h.Revenue));
    }

    [Fact]
    public async Task Bad_tool_input_is_rejected_so_claude_can_retry()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<SalesQueries>();

        await Assert.ThrowsAsync<ArgumentException>(() => ReportTools.RunAsync(queries, Guid.NewGuid(), "sales_summary",
            JsonSerializer.SerializeToElement(new { from_date = "28.09.2026", to_date = "2026-09-28" }), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => ReportTools.RunAsync(queries, Guid.NewGuid(), "sales_summary",
            JsonSerializer.SerializeToElement(new { from_date = "2026-09-28", to_date = "2026-09-01" }), CancellationToken.None));
    }

    private static async Task<T> Post<T>(HttpClient client, string url, object? body)
    {
        var response = body is null ? await client.PostAsync(url, null) : await client.PostAsJsonAsync(url, body, ApiFactory.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.JsonOptions))!;
    }
}
