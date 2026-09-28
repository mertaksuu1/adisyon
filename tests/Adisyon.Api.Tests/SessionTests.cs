using System.Net;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;
using Adisyon.Api.Domain;

namespace Adisyon.Api.Tests;

/// <summary>Adisyon akışı: masa aç → sipariş ekle → hesabı kapat.</summary>
[Collection(nameof(ApiCollection))]
public class SessionTests(ApiFactory factory)
{
    /// <summary>Bir restoran, menüsünde iki ürün, bir masa ve giriş yapmış sahip + garson + kasiyer.</summary>
    private async Task<(HttpClient Owner, HttpClient Waiter, HttpClient Cashier, TableDto Table, ProductDto Kebap, ProductDto Ayran)> SetUpAsync()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var category = await MenuTests.CreateCategoryAsync(owner, "Menü");
        var kebap = await MenuTests.CreateProductAsync(owner, category.Id, "Adana Kebap", 380m);
        var ayran = await MenuTests.CreateProductAsync(owner, category.Id, "Ayran", 40m);
        var tableResponse = await owner.PostAsJsonAsync("/api/tables", new SaveTableRequest("Masa 1"), ApiFactory.JsonOptions);
        var table = (await tableResponse.Content.ReadFromJsonAsync<TableDto>(ApiFactory.JsonOptions))!;

        return (owner, await factory.LoginAsync(restaurant, UserRole.Waiter),
            await factory.LoginAsync(restaurant, UserRole.Cashier),
            table, kebap, ayran);
    }

    [Fact]
    public async Task Full_flow_open_order_close()
    {
        var (_, waiter, cashier, table, kebap, ayran) = await SetUpAsync();

        var session = await OpenAsync(waiter, table.Id);
        await AddOrderAsync(waiter, session.Id, new AddOrderItem(kebap.Id, 2, "Acılı"), new AddOrderItem(ayran.Id, 2));
        var afterSecond = await AddOrderAsync(waiter, session.Id, new AddOrderItem(ayran.Id, 1));

        Assert.Equal(2 * 380m + 3 * 40m, afterSecond.Total);
        Assert.Equal(2, afterSecond.Orders.Count);
        Assert.Equal("Acılı", afterSecond.Orders[0].Items.Single(i => i.ProductId == kebap.Id).Note);

        var tables = await waiter.GetFromJsonAsync<List<TableDto>>("/api/tables", ApiFactory.JsonOptions);
        Assert.Equal(880m, tables!.Single().OpenSession!.Total);

        var close = await cashier.PostAsJsonAsync($"/api/sessions/{session.Id}/close",
            new CloseSessionRequest(afterSecond.Version), ApiFactory.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);

        tables = await waiter.GetFromJsonAsync<List<TableDto>>("/api/tables", ApiFactory.JsonOptions);
        Assert.Null(tables!.Single().OpenSession); // masa yeniden boş
    }

    [Fact]
    public async Task Closing_with_a_stale_version_is_rejected()
    {
        var (_, waiter, cashier, table, kebap, ayran) = await SetUpAsync();
        var session = await OpenAsync(waiter, table.Id);
        var seenByCashier = await AddOrderAsync(waiter, session.Id, new AddOrderItem(kebap.Id, 1));

        // Kasiyer ekrana bakarken garson bir ayran daha ekliyor.
        await AddOrderAsync(waiter, session.Id, new AddOrderItem(ayran.Id, 1));
        var close = await cashier.PostAsJsonAsync($"/api/sessions/{session.Id}/close",
            new CloseSessionRequest(seenByCashier.Version), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);
    }

    [Fact]
    public async Task Second_open_on_same_table_returns_conflict_with_existing_session()
    {
        var (_, waiter, cashier, table, _, _) = await SetUpAsync();
        var first = await OpenAsync(waiter, table.Id);

        var second = await cashier.PostAsync($"/api/tables/{table.Id}/session", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(first.Id.ToString(), await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Price_change_does_not_affect_existing_orders()
    {
        var (owner, waiter, _, table, kebap, _) = await SetUpAsync();
        var session = await OpenAsync(waiter, table.Id);
        await AddOrderAsync(waiter, session.Id, new AddOrderItem(kebap.Id, 1));

        // Zam geldi: kebap 380'den 450'ye. Bu masa eski fiyattan ödemeli, yeni siparişler yeni fiyattan.
        var raise = await owner.PutAsJsonAsync($"/api/products/{kebap.Id}",
            new SaveProductRequest(kebap.CategoryId, kebap.Name, 450m), ApiFactory.JsonOptions);
        raise.EnsureSuccessStatusCode();
        var detail = await AddOrderAsync(waiter, session.Id, new AddOrderItem(kebap.Id, 1));

        Assert.Equal(380m + 450m, detail.Total);
    }

    [Fact]
    public async Task Kitchen_cannot_open_tables_or_close_bills()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var kitchen = await factory.LoginAsync(restaurant, UserRole.Kitchen);
        var tableResponse = await owner.PostAsJsonAsync("/api/tables", new SaveTableRequest("Masa 1"), ApiFactory.JsonOptions);
        var table = (await tableResponse.Content.ReadFromJsonAsync<TableDto>(ApiFactory.JsonOptions))!;

        var open = await kitchen.PostAsync($"/api/tables/{table.Id}/session", null);

        Assert.Equal(HttpStatusCode.Forbidden, open.StatusCode);
    }

    [Fact]
    public async Task Inactive_product_cannot_be_ordered()
    {
        var (owner, waiter, _, table, kebap, _) = await SetUpAsync();
        var session = await OpenAsync(waiter, table.Id);
        var deactivate = await owner.PutAsJsonAsync($"/api/products/{kebap.Id}",
            new SaveProductRequest(kebap.CategoryId, kebap.Name, kebap.Price, IsActive: false), ApiFactory.JsonOptions);
        deactivate.EnsureSuccessStatusCode();

        var response = await waiter.PostAsJsonAsync($"/api/sessions/{session.Id}/orders",
            new AddOrderRequest([new AddOrderItem(kebap.Id, 1)]), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Quantity_must_be_between_1_and_99()
    {
        var (_, waiter, _, table, kebap, _) = await SetUpAsync();
        var session = await OpenAsync(waiter, table.Id);

        var response = await waiter.PostAsJsonAsync($"/api/sessions/{session.Id}/orders",
            new AddOrderRequest([new AddOrderItem(kebap.Id, 0)]), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_restaurant_cannot_see_or_order_on_this_session()
    {
        var (_, waiter, _, table, kebap, _) = await SetUpAsync();
        var session = await OpenAsync(waiter, table.Id);
        var (stranger, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);

        var read = await stranger.GetAsync($"/api/sessions/{session.Id}");
        var order = await stranger.PostAsJsonAsync($"/api/sessions/{session.Id}/orders",
            new AddOrderRequest([new AddOrderItem(kebap.Id, 1)]), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, order.StatusCode);
    }

    private static async Task<SessionDto> OpenAsync(HttpClient client, Guid tableId)
    {
        var response = await client.PostAsync($"/api/tables/{tableId}/session", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionDto>(ApiFactory.JsonOptions))!;
    }

    private static async Task<SessionDto> AddOrderAsync(HttpClient client, Guid sessionId, params AddOrderItem[] items)
    {
        var response = await client.PostAsJsonAsync($"/api/sessions/{sessionId}/orders", new AddOrderRequest([.. items]), ApiFactory.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionDto>(ApiFactory.JsonOptions))!;
    }
}
