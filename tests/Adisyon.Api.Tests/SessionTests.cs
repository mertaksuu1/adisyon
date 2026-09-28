using System.Net;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;
using Adisyon.Api.Domain;
using Adisyon.Api.Printing;

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
    public async Task Full_flow_open_order_pay()
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

        // Kasiyer "Nakit"e basar: kalanın tamamı ödenir ve masa kendiliğinden kapanır.
        var pay = await cashier.PostAsJsonAsync($"/api/sessions/{session.Id}/payments",
            new PayRequest(PaymentMethod.Cash, afterSecond.Version), ApiFactory.JsonOptions);
        var paid = await pay.Content.ReadFromJsonAsync<SessionDto>(ApiFactory.JsonOptions);
        Assert.Equal((TableSessionStatus.Closed, 880m, 0m), (paid!.Status, paid.Paid, paid.Remaining));

        tables = await waiter.GetFromJsonAsync<List<TableDto>>("/api/tables", ApiFactory.JsonOptions);
        Assert.Null(tables!.Single().OpenSession); // masa yeniden boş
    }

    [Fact]
    public async Task Items_keep_the_order_they_were_entered_in()
    {
        var (owner, waiter, _, table, kebap, ayran) = await SetUpAsync();
        var category = (await owner.GetFromJsonAsync<List<CategoryDto>>("/api/menu", ApiFactory.JsonOptions))!.Single();
        var extra = new List<ProductDto>();
        foreach (var name in new[] { "Çorba", "Salata", "Pilav", "Künefe", "Çay", "Su" })
        {
            extra.Add(await MenuTests.CreateProductAsync(owner, category.Id, name, 10m));
        }
        // Aynı istekte 8 satır: hepsi aynı milisaniyede kaydedilir, sıra kimlikten çıkarılamaz.
        var entered = new[] { ayran, kebap }.Concat(extra).ToList();
        var session = await OpenAsync(waiter, table.Id);

        var result = await AddOrderAsync(waiter, session.Id, [.. entered.Select(p => new AddOrderItem(p.Id, 1))]);

        Assert.Equal(entered.Select(p => p.Name), result.Orders.Single().Items.Select(i => i.ProductName));
    }

    [Fact]
    public async Task Paying_with_a_stale_version_is_rejected()
    {
        var (_, waiter, cashier, table, kebap, ayran) = await SetUpAsync();
        var session = await OpenAsync(waiter, table.Id);
        var seenByCashier = await AddOrderAsync(waiter, session.Id, new AddOrderItem(kebap.Id, 1));

        // Kasiyer ekrana bakarken garson bir ayran daha ekliyor.
        await AddOrderAsync(waiter, session.Id, new AddOrderItem(ayran.Id, 1));
        var pay = await cashier.PostAsJsonAsync($"/api/sessions/{session.Id}/payments",
            new PayRequest(PaymentMethod.Card, seenByCashier.Version), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, pay.StatusCode);
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

    // ---------------- Ödeme ve hesap fişi ----------------

    /// <summary>Masaya 1 kebap + 2 ayran (460 ₺) girilmiş hâlini döndürür.</summary>
    private async Task<(HttpClient Owner, HttpClient Waiter, HttpClient Cashier, SessionDto Session)> OpenWithOrderAsync()
    {
        var (owner, waiter, cashier, table, kebap, ayran) = await SetUpAsync();
        var session = await OpenAsync(waiter, table.Id);
        session = await AddOrderAsync(waiter, session.Id, new AddOrderItem(kebap.Id, 1), new AddOrderItem(ayran.Id, 1));
        session = await AddOrderAsync(waiter, session.Id, new AddOrderItem(ayran.Id, 1));
        return (owner, waiter, cashier, session);
    }

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, SessionDto session, PaymentMethod method, decimal? amount = null) =>
        client.PostAsJsonAsync($"/api/sessions/{session.Id}/payments", new PayRequest(method, session.Version, amount), ApiFactory.JsonOptions);

    [Fact]
    public async Task Split_payment_keeps_table_open_until_fully_paid()
    {
        var (_, _, cashier, session) = await OpenWithOrderAsync();

        var afterCard = await (await PayAsync(cashier, session, PaymentMethod.Card, 300m)).Content.ReadFromJsonAsync<SessionDto>(ApiFactory.JsonOptions);
        Assert.Equal((TableSessionStatus.Open, 300m, 160m), (afterCard!.Status, afterCard.Paid, afterCard.Remaining));

        var afterCash = await (await PayAsync(cashier, afterCard, PaymentMethod.Cash)).Content.ReadFromJsonAsync<SessionDto>(ApiFactory.JsonOptions);
        Assert.Equal((TableSessionStatus.Closed, 460m, 0m), (afterCash!.Status, afterCash.Paid, afterCash.Remaining));
        Assert.Equal([PaymentMethod.Card, PaymentMethod.Cash], afterCash.Payments.Select(p => p.Method));
    }

    [Fact]
    public async Task Paying_more_than_remaining_is_rejected()
    {
        var (_, _, cashier, session) = await OpenWithOrderAsync();

        var response = await PayAsync(cashier, session, PaymentMethod.Cash, 500m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Waiter_cannot_take_payment()
    {
        var (_, waiter, _, session) = await OpenWithOrderAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await PayAsync(waiter, session, PaymentMethod.Cash)).StatusCode);
    }

    [Fact]
    public async Task Unpaid_table_cannot_be_closed_but_empty_one_can()
    {
        var (_, waiter, cashier, table, _, _) = await SetUpAsync();
        var empty = await OpenAsync(waiter, table.Id);

        var closeEmpty = await cashier.PostAsJsonAsync($"/api/sessions/{empty.Id}/close", new CloseSessionRequest(empty.Version), ApiFactory.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, closeEmpty.StatusCode);

        var (_, _, cashier2, unpaid) = await OpenWithOrderAsync();
        var closeUnpaid = await cashier2.PostAsJsonAsync($"/api/sessions/{unpaid.Id}/close", new CloseSessionRequest(unpaid.Version), ApiFactory.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, closeUnpaid.StatusCode);
    }

    [Fact]
    public async Task Bill_ticket_groups_items_and_shows_payments_and_legal_notice()
    {
        var (owner, waiter, cashier, session) = await OpenWithOrderAsync();
        (await PayAsync(cashier, session, PaymentMethod.Card, 100m)).EnsureSuccessStatusCode();

        var print = await waiter.PostAsync($"/api/sessions/{session.Id}/print-bill", null);

        Assert.Equal(HttpStatusCode.NoContent, print.StatusCode);
        var bill = (await owner.GetFromJsonAsync<List<PrintedTicket>>("/api/print/recent", ApiFactory.JsonOptions))!
            .First(t => t.Kind == TicketKind.Bill);
        var lines = bill.Text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.Contains(lines, l => l.StartsWith("2 x Ayran") && l.EndsWith("80,00")); // iki siparişteki ayranlar tek satırda
        Assert.Contains(lines, l => l.StartsWith("TOPLAM") && l.EndsWith("460,00"));
        Assert.Contains(lines, l => l.StartsWith("Ödenen (Kart)") && l.EndsWith("100,00"));
        Assert.Contains(lines, l => l.StartsWith("KALAN") && l.EndsWith("360,00"));
        Assert.Contains("MALİ DEĞERİ YOKTUR", bill.Text);
        Assert.All(lines, l => Assert.True(l.Length <= TicketFormat.PaperWidth, $"Satır kâğıda sığmıyor: '{l}'"));
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
