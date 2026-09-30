using System.Net;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;
using Adisyon.Api.Domain;
using Adisyon.Api.Printing;
using Adisyon.Api.Reports;

namespace Adisyon.Api.Tests;

[Collection(nameof(ApiCollection))]
public class ReportTests(ApiFactory factory)
{
    /// <summary>
    /// Masa 1: 2 kebap + 2 ayran (840). 1 ayran iptal, 1 ayran ikram → 760. 300 kart + 460 nakit, kapandı.
    /// Masa 2: 1 künefe (180), açık kaldı.
    /// </summary>
    private async Task<(ApiFactory.TestRestaurant Restaurant, HttpClient Manager, HttpClient Waiter)> RunADayAsync()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var manager = await factory.LoginAsync(restaurant, UserRole.Manager);
        var waiter = await factory.LoginAsync(restaurant, UserRole.Waiter);
        var cashier = await factory.LoginAsync(restaurant, UserRole.Cashier);

        var category = await MenuTests.CreateCategoryAsync(manager, "Menü");
        var kebap = await MenuTests.CreateProductAsync(manager, category.Id, "Adana Kebap", 380m);
        var ayran = await MenuTests.CreateProductAsync(manager, category.Id, "Ayran", 40m);
        var kunefe = await MenuTests.CreateProductAsync(manager, category.Id, "Künefe", 180m);
        var table1 = await CreateTableAsync(manager, "Masa 1");
        var table2 = await CreateTableAsync(manager, "Masa 2");

        var s = await PostAsync<SessionDto>(waiter, $"/api/tables/{table1.Id}/session", null);
        s = await PostAsync<SessionDto>(waiter, $"/api/sessions/{s.Id}/orders",
            new AddOrderRequest([new AddOrderItem(kebap.Id, 2), new AddOrderItem(ayran.Id, 2)]));
        var ayranLine = s.Orders.Single().Items.Single(i => i.ProductName == "Ayran");
        s = await PostAsync<SessionDto>(cashier, $"/api/sessions/{s.Id}/items/{ayranLine.Id}/void", new AdjustItemRequest(1, s.Version));
        ayranLine = s.Orders.Single().Items.Single(i => i.ProductName == "Ayran" && !i.IsVoided);
        s = await PostAsync<SessionDto>(cashier, $"/api/sessions/{s.Id}/items/{ayranLine.Id}/comp", new AdjustItemRequest(1, s.Version));
        s = await PostAsync<SessionDto>(cashier, $"/api/sessions/{s.Id}/payments", new PayRequest(PaymentMethod.Card, s.Version, 300m));
        s = await PostAsync<SessionDto>(cashier, $"/api/sessions/{s.Id}/payments", new PayRequest(PaymentMethod.Cash, s.Version));
        Assert.Equal(TableSessionStatus.Closed, s.Status);

        var open = await PostAsync<SessionDto>(waiter, $"/api/tables/{table2.Id}/session", null);
        await PostAsync<SessionDto>(waiter, $"/api/sessions/{open.Id}/orders", new AddOrderRequest([new AddOrderItem(kunefe.Id, 1)]));

        return (restaurant, manager, waiter);
    }

    [Fact]
    public async Task Z_report_adds_up_the_day()
    {
        var (_, manager, _) = await RunADayAsync();

        var z = await manager.GetFromJsonAsync<ZReport>("/api/reports/z", ApiFactory.JsonOptions);

        Assert.Equal((460m, 300m, 760m), (z!.CashTotal, z.CardTotal, z.PaymentsTotal));
        Assert.Equal((1, 760m, 760m), (z.ClosedSessionCount, z.SalesTotal, z.AverageBill));
        var voided = Assert.Single(z.Voids);
        Assert.Equal(("Masa 1", "Ayran", 1, 40m, "Test Cashier"), (voided.TableName, voided.ProductName, voided.Quantity, voided.Amount, voided.ByName));
        Assert.Equal(40m, Assert.Single(z.Comps).Amount);
        Assert.Equal("Adana Kebap", Assert.Single(z.TopProducts).ProductName); // ayranların biri iptal, biri ikram: satış değil
        Assert.Equal((1, 180m), (z.OpenTableCount, z.OpenTablesTotal));
    }

    [Fact]
    public async Task Void_keeps_the_table_it_was_made_at_even_after_the_table_moves()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var manager = await factory.LoginAsync(restaurant, UserRole.Manager);
        var category = await MenuTests.CreateCategoryAsync(manager, "Menü");
        var ayran = await MenuTests.CreateProductAsync(manager, category.Id, "Ayran", 40m);
        var inside = await CreateTableAsync(manager, "Salon 1");
        var garden = await CreateTableAsync(manager, "Bahçe 3");

        var s = await PostAsync<SessionDto>(manager, $"/api/tables/{inside.Id}/session", null);
        s = await PostAsync<SessionDto>(manager, $"/api/sessions/{s.Id}/orders", new AddOrderRequest([new AddOrderItem(ayran.Id, 2)]));
        s = await PostAsync<SessionDto>(manager, $"/api/sessions/{s.Id}/items/{s.Orders.Single().Items.Single().Id}/void", new AdjustItemRequest(1, s.Version));
        // Müşteri iptalden sonra bahçeye geçti.
        await PostAsync<SessionDto>(manager, $"/api/sessions/{s.Id}/move", new MoveSessionRequest(garden.Id, s.Version));

        var z = await manager.GetFromJsonAsync<ZReport>("/api/reports/z", ApiFactory.JsonOptions);

        Assert.Equal("Salon 1", Assert.Single(z!.Voids).TableName);
    }

    [Fact]
    public async Task Report_of_another_day_or_restaurant_is_empty()
    {
        var (_, manager, _) = await RunADayAsync();
        var (stranger, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);

        var yesterday = BusinessDay.Of(DateTimeOffset.UtcNow).AddDays(-1);
        var old = await manager.GetFromJsonAsync<ZReport>($"/api/reports/z?date={yesterday:yyyy-MM-dd}", ApiFactory.JsonOptions);
        var other = await stranger.GetFromJsonAsync<ZReport>("/api/reports/z", ApiFactory.JsonOptions);

        Assert.Equal((0m, 0), (old!.PaymentsTotal, old.ClosedSessionCount));
        Assert.Equal((0m, 0, 0), (other!.PaymentsTotal, other.ClosedSessionCount, other.OpenTableCount));
    }

    [Fact]
    public async Task Only_owner_and_manager_see_the_report()
    {
        var (_, _, waiter) = await RunADayAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync("/api/reports/z")).StatusCode);
    }

    [Fact]
    public async Task Printed_report_fits_the_paper_and_warns_about_open_tables()
    {
        var (_, manager, _) = await RunADayAsync();

        (await manager.PostAsync("/api/reports/z/print", null)).EnsureSuccessStatusCode();

        var ticket = (await manager.GetFromJsonAsync<List<PrintedTicket>>("/api/print/recent", ApiFactory.JsonOptions))!
            .First(t => t.Kind == TicketKind.Report);
        Assert.Contains("GÜN SONU RAPORU", ticket.Text);
        Assert.Contains("İPTALLER (1)", ticket.Text);
        Assert.Contains("Açık masa: 1", ticket.Text);
        Assert.All(ticket.Text.Split('\n'), l => Assert.True(l.TrimEnd('\r').Length <= TicketFormat.PaperWidth, $"Satır kâğıda sığmıyor: '{l}'"));
    }

    [Theory]
    [InlineData("2026-09-29T01:30:00+03:00", "2026-09-28")] // gece yarısından sonra: önceki iş günü
    [InlineData("2026-09-29T04:59:00+03:00", "2026-09-28")]
    [InlineData("2026-09-29T05:00:00+03:00", "2026-09-29")] // 05:00'te yeni gün başlar
    [InlineData("2026-09-29T23:59:00+03:00", "2026-09-29")]
    public void Business_day_starts_at_five_in_the_morning(string instant, string expectedDay)
    {
        Assert.Equal(DateOnly.Parse(expectedDay), BusinessDay.Of(DateTimeOffset.Parse(instant)));
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body)
    {
        var response = body is null ? await client.PostAsync(url, null) : await client.PostAsJsonAsync(url, body, ApiFactory.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.JsonOptions))!;
    }

    private static Task<TableDto> CreateTableAsync(HttpClient client, string name) =>
        PostAsync<TableDto>(client, "/api/tables", new SaveTableRequest(name));
}
