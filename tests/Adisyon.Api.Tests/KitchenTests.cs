using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Adisyon.Api.Controllers;
using Adisyon.Api.Domain;
using Adisyon.Api.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Adisyon.Api.Tests;

/// <summary>Mutfak ekranı: gerçek zamanlı bildirimler ve sipariş durumları.</summary>
[Collection(nameof(ApiCollection))]
public class KitchenTests(ApiFactory factory)
{
    private record Setup(ApiFactory.TestRestaurant Restaurant, HttpClient Waiter, HttpClient Kitchen, Guid TableId, Guid ProductId);

    private async Task<Setup> SetUpAsync()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var category = await MenuTests.CreateCategoryAsync(owner, "Menü");
        var product = await MenuTests.CreateProductAsync(owner, category.Id, "Mercimek Çorbası", 90m);
        var table = await (await owner.PostAsJsonAsync("/api/tables", new SaveTableRequest("Masa 7"), ApiFactory.JsonOptions))
            .Content.ReadFromJsonAsync<TableDto>(ApiFactory.JsonOptions);

        return new Setup(restaurant,
            await factory.LoginAsync(restaurant, UserRole.Waiter),
            await factory.LoginAsync(restaurant, UserRole.Kitchen),
            table!.Id, product.Id);
    }

    /// <summary>Garson tarafı: masayı açıp bir sipariş gönderir, siparişin kimliğini döndürür.</summary>
    private static async Task<Guid> PlaceOrderAsync(Setup s)
    {
        var session = await (await s.Waiter.PostAsync($"/api/tables/{s.TableId}/session", null))
            .Content.ReadFromJsonAsync<SessionDto>(ApiFactory.JsonOptions);
        var afterOrder = await (await s.Waiter.PostAsJsonAsync($"/api/sessions/{session!.Id}/orders",
                new AddOrderRequest([new AddOrderItem(s.ProductId, 2, "Limonlu")]), ApiFactory.JsonOptions))
            .Content.ReadFromJsonAsync<SessionDto>(ApiFactory.JsonOptions);
        return afterOrder!.Orders.Single().Id;
    }

    [Fact]
    public async Task Kitchen_receives_new_order_instantly_and_other_restaurants_do_not()
    {
        var s = await SetUpAsync();
        var (stranger, _) = await factory.CreateTenantAndLoginAsync(UserRole.Kitchen);

        await using var kitchenConnection = await ConnectAsync(s.Kitchen);
        await using var strangerConnection = await ConnectAsync(stranger);
        var kitchenReceived = new TaskCompletionSource<KitchenOrderDto>();
        var strangerReceived = new TaskCompletionSource<KitchenOrderDto>();
        kitchenConnection.On<KitchenOrderDto>(RealtimeEvents.OrderCreated, o => kitchenReceived.TrySetResult(o));
        strangerConnection.On<KitchenOrderDto>(RealtimeEvents.OrderCreated, o => strangerReceived.TrySetResult(o));

        await PlaceOrderAsync(s);

        var card = await kitchenReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Masa 7", card.TableName);
        Assert.Equal("Test Waiter", card.CreatedByName);
        var item = Assert.Single(card.Items);
        Assert.Equal((2, "Limonlu"), (item.Quantity, item.Note));

        // Başka restoranın mutfağına hiçbir şey gitmemeli.
        await Task.Delay(500);
        Assert.False(strangerReceived.Task.IsCompleted);
    }

    [Fact]
    public async Task Hub_rejects_connections_without_login()
    {
        var anonymous = factory.CreateClient();

        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(anonymous));
    }

    [Fact]
    public async Task Order_moves_through_kitchen_and_is_served_by_waiter()
    {
        var s = await SetUpAsync();
        var orderId = await PlaceOrderAsync(s);

        Assert.Equal(OrderStatus.Preparing, await ChangeAsync(s.Kitchen, orderId, OrderStatus.Preparing));
        Assert.Equal(OrderStatus.Ready, await ChangeAsync(s.Kitchen, orderId, OrderStatus.Ready));
        Assert.Equal(OrderStatus.Served, await ChangeAsync(s.Waiter, orderId, OrderStatus.Served));

        // Servis edilen sipariş mutfak ekranından düşer.
        var active = await s.Kitchen.GetFromJsonAsync<List<KitchenOrderDto>>("/api/kitchen/orders", ApiFactory.JsonOptions);
        Assert.DoesNotContain(active!, o => o.Id == orderId);
    }

    [Fact]
    public async Task Waiter_cannot_mark_kitchen_steps()
    {
        var s = await SetUpAsync();
        var orderId = await PlaceOrderAsync(s);

        var response = await s.Waiter.PostAsJsonAsync($"/api/orders/{orderId}/status",
            new ChangeOrderStatusRequest(OrderStatus.Ready), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Status_cannot_go_backwards_or_skip_to_served()
    {
        var s = await SetUpAsync();
        var orderId = await PlaceOrderAsync(s);

        var skip = await s.Kitchen.PostAsJsonAsync($"/api/orders/{orderId}/status",
            new ChangeOrderStatusRequest(OrderStatus.Served), ApiFactory.JsonOptions);
        await ChangeAsync(s.Kitchen, orderId, OrderStatus.Ready);
        var back = await s.Kitchen.PostAsJsonAsync($"/api/orders/{orderId}/status",
            new ChangeOrderStatusRequest(OrderStatus.Preparing), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, back.StatusCode);
    }

    [Fact]
    public async Task Kitchen_sees_active_orders_oldest_first()
    {
        var s = await SetUpAsync();
        var first = await PlaceOrderAsync(s);
        var sessionId = (await s.Kitchen.GetFromJsonAsync<List<KitchenOrderDto>>("/api/kitchen/orders", ApiFactory.JsonOptions))!
            .Single().SessionId;
        await s.Waiter.PostAsJsonAsync($"/api/sessions/{sessionId}/orders",
            new AddOrderRequest([new AddOrderItem(s.ProductId, 1)]), ApiFactory.JsonOptions);

        var active = await s.Kitchen.GetFromJsonAsync<List<KitchenOrderDto>>("/api/kitchen/orders", ApiFactory.JsonOptions);

        Assert.Equal(2, active!.Count);
        Assert.Equal(first, active[0].Id);
    }

    [Fact]
    public async Task Orders_of_closed_tables_leave_the_kitchen_screen()
    {
        var s = await SetUpAsync();
        var orderId = await PlaceOrderAsync(s);
        var cashier = await factory.LoginAsync(s.Restaurant, UserRole.Cashier);
        var session = (await s.Kitchen.GetFromJsonAsync<List<KitchenOrderDto>>("/api/kitchen/orders", ApiFactory.JsonOptions))!
            .Single(o => o.Id == orderId);
        var detail = await cashier.GetFromJsonAsync<SessionDto>($"/api/sessions/{session.SessionId}", ApiFactory.JsonOptions);

        (await cashier.PostAsJsonAsync($"/api/sessions/{detail!.Id}/close", new CloseSessionRequest(detail.Version), ApiFactory.JsonOptions))
            .EnsureSuccessStatusCode();
        var active = await s.Kitchen.GetFromJsonAsync<List<KitchenOrderDto>>("/api/kitchen/orders", ApiFactory.JsonOptions);

        Assert.DoesNotContain(active!, o => o.Id == orderId);
    }

    private async Task<OrderStatus> ChangeAsync(HttpClient client, Guid orderId, OrderStatus status)
    {
        var response = await client.PostAsJsonAsync($"/api/orders/{orderId}/status", new ChangeOrderStatusRequest(status), ApiFactory.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KitchenOrderDto>(ApiFactory.JsonOptions))!.Status;
    }

    /// <summary>
    /// Test sunucusuna SignalR ile bağlanır. Test sunucusu gerçek ağ kullanmadığı için
    /// WebSocket yerine "long polling" taşıması kullanılır; olaylar aynı şekilde gelir.
    /// </summary>
    private async Task<HubConnection> ConnectAsync(HttpClient loggedInClient)
    {
        var token = loggedInClient.DefaultRequestHeaders.Authorization?.Parameter;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, BranchHub.Path.TrimStart('/')), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult(token);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        await connection.StartAsync();
        return connection;
    }
}
