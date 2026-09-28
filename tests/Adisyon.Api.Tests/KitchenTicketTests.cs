using System.Net;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;
using Adisyon.Api.Domain;
using Adisyon.Api.Printing;
using Adisyon.Api.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Adisyon.Api.Tests;

/// <summary>Sipariş gönderilince mutfak fişi basılır; birden çok ekran varsa masa planı anında güncellenir.</summary>
[Collection(nameof(ApiCollection))]
public class KitchenTicketTests(ApiFactory factory)
{
    private record Setup(HttpClient Owner, HttpClient Waiter, Guid TableId, ProductDto Soup, ProductDto Grill);

    private async Task<Setup> SetUpAsync()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var category = await MenuTests.CreateCategoryAsync(owner, "Menü");
        var soup = await MenuTests.CreateProductAsync(owner, category.Id, "Ezogelin Çorbası", 90m);
        var grill = await MenuTests.CreateProductAsync(owner, category.Id, "Karışık Izgara", 520m);
        var table = await (await owner.PostAsJsonAsync("/api/tables", new SaveTableRequest("Masa 7"), ApiFactory.JsonOptions))
            .Content.ReadFromJsonAsync<TableDto>(ApiFactory.JsonOptions);
        return new Setup(owner, await factory.LoginAsync(restaurant, UserRole.Waiter), table!.Id, soup, grill);
    }

    private static async Task PlaceOrderAsync(Setup s)
    {
        var session = await (await s.Waiter.PostAsync($"/api/tables/{s.TableId}/session", null))
            .Content.ReadFromJsonAsync<SessionDto>(ApiFactory.JsonOptions);
        (await s.Waiter.PostAsJsonAsync($"/api/sessions/{session!.Id}/orders",
            new AddOrderRequest([new AddOrderItem(s.Grill.Id, 1, "Soğansız"), new AddOrderItem(s.Soup.Id, 2)]),
            ApiFactory.JsonOptions)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Sending_an_order_prints_a_kitchen_ticket()
    {
        var s = await SetUpAsync();

        await PlaceOrderAsync(s);

        var ticket = Assert.Single((await s.Owner.GetFromJsonAsync<List<PrintedTicket>>("/api/print/recent", ApiFactory.JsonOptions))!);
        var lines = ticket.Text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.Contains("MUTFAK FİŞİ", ticket.Text);
        Assert.StartsWith("MASA 7", lines[3]);
        Assert.Contains("Garson: Test Waiter", lines);
        // Girildiği sırayla, not ayrı satırda; fiyat yok.
        var itemsStart = lines.IndexOf("1 x Karışık Izgara");
        Assert.Equal(["1 x Karışık Izgara", "    >> Soğansız", "2 x Ezogelin Çorbası"], lines.Skip(itemsStart).Take(3));
        Assert.DoesNotContain("520", ticket.Text);
        Assert.All(lines, l => Assert.True(l.Length <= KitchenTicket.PaperWidth, $"Satır kâğıda sığmıyor: '{l}'"));
    }

    [Fact]
    public async Task Tickets_of_other_restaurants_are_not_visible()
    {
        var s = await SetUpAsync();
        await PlaceOrderAsync(s);
        var (stranger, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);

        var tickets = await stranger.GetFromJsonAsync<List<PrintedTicket>>("/api/print/recent", ApiFactory.JsonOptions);

        Assert.Empty(tickets!);
    }

    [Fact]
    public async Task Waiter_cannot_browse_ticket_history()
    {
        var s = await SetUpAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Waiter.GetAsync("/api/print/recent")).StatusCode);
    }

    [Fact]
    public async Task Other_screens_of_the_same_restaurant_hear_table_changes_and_strangers_do_not()
    {
        var s = await SetUpAsync();
        var (stranger, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);
        await using var ownerScreen = await ConnectAsync(s.Owner);
        await using var strangerScreen = await ConnectAsync(stranger);
        var ownerHeard = new TaskCompletionSource();
        var strangerHeard = new TaskCompletionSource();
        ownerScreen.On(RealtimeEvents.TablesChanged, () => ownerHeard.TrySetResult());
        strangerScreen.On(RealtimeEvents.TablesChanged, () => strangerHeard.TrySetResult());

        await PlaceOrderAsync(s);

        await ownerHeard.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(500);
        Assert.False(strangerHeard.Task.IsCompleted);
    }

    [Fact]
    public async Task Realtime_channel_requires_login()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(factory.CreateClient()));
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
            .Build();
        await connection.StartAsync();
        return connection;
    }
}
