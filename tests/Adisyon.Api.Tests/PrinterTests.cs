using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Adisyon.Api.Controllers;
using Adisyon.Api.Domain;
using Adisyon.Api.Printing;

namespace Adisyon.Api.Tests;

/// <summary>
/// ESC/POS ağ yazıcısı. Gerçek yazıcı yerine, 9100 portundaki yazıcı gibi davranan sahte bir TCP dinleyici
/// kullanılır: gönderilen baytlar birebir kontrol edilir (Türkçe karakterler, kağıt kesme…).
/// </summary>
[Collection(nameof(ApiCollection))]
public class PrinterTests(ApiFactory factory)
{
    [Fact]
    public void Encoding_uses_turkish_code_page_and_cuts_the_paper()
    {
        var bytes = EscPos.Encode(new PrintJob(TicketKind.Kitchen, Guid.Empty, "t", "Şiş ₺"), codePageNumber: 13);

        Assert.Equal(new byte[] { 0x1B, (byte)'@', 0x1B, (byte)'t', 13, 0x1D, (byte)'!', 0x01 }, bytes[..8]); // sıfırla, Türkçe tablo, iki kat yükseklik
        // PC857: Ş=0x9E, ş=0x9F; tabloda olmayan ₺ → '?'
        Assert.Equal(new byte[] { 0x9E, (byte)'i', 0x9F, (byte)' ', (byte)'?' }, bytes[8..13]);
        Assert.Equal(new byte[] { 0x1D, (byte)'V', (byte)'B', 0 }, bytes[^4..]); // kes
    }

    [Fact]
    public void Bills_are_printed_in_normal_size()
    {
        var bytes = EscPos.Encode(new PrintJob(TicketKind.Bill, Guid.Empty, "t", "x"), codePageNumber: 13);

        Assert.DoesNotContain(Sequence(bytes), s => s == "1D-21-01");
    }

    [Fact]
    public async Task Sending_an_order_prints_the_kitchen_ticket_on_the_kitchen_printer()
    {
        await using var kitchenPrinter = new FakePrinter();
        var (owner, waiter, tableId, productId) = await SetUpAsync();
        await SaveSettingsAsync(owner, kitchenPrinter.Address, null);

        var session = await SendOrderAsync(waiter, tableId, productId);

        Assert.Null(session.PrintWarning);
        var printed = await kitchenPrinter.NextTextAsync();
        Assert.Contains("MUTFAK FİŞİ", printed);
        Assert.Contains("2 x Şiş Köfte", printed);
        var ticket = (await owner.GetFromJsonAsync<List<PrintedTicket>>("/api/print/recent", ApiFactory.JsonOptions))!.First();
        Assert.Equal(PrintStatus.Printed, ticket.Status);
    }

    [Fact]
    public async Task Printer_off_does_not_lose_the_order_warns_and_can_reprint()
    {
        var (owner, waiter, tableId, productId) = await SetUpAsync();
        await SaveSettingsAsync(owner, $"127.0.0.1:{ClosedPort()}", null);

        var session = await SendOrderAsync(waiter, tableId, productId);

        // Sipariş kaydedildi, ama garson fişin basılmadığını görüyor.
        Assert.Equal(460m, session.Total);
        Assert.Contains("fişi yazdırılamadı", session.PrintWarning);
        Assert.NotNull(session.FailedTicketId);
        var failed = (await owner.GetFromJsonAsync<List<PrintedTicket>>("/api/print/recent", ApiFactory.JsonOptions))!.First();
        Assert.Equal(PrintStatus.Failed, failed.Status);

        // Yazıcı açıldı (adres düzeltildi): garson tekrar yazdırır.
        await using var kitchenPrinter = new FakePrinter();
        await SaveSettingsAsync(owner, kitchenPrinter.Address, null);
        var reprint = await waiter.PostAsync($"/api/print/{session.FailedTicketId}/reprint", null);
        var result = await reprint.Content.ReadFromJsonAsync<PrintResult>(ApiFactory.JsonOptions);

        Assert.Equal(PrintStatus.Printed, result!.Status);
        Assert.Contains("Şiş Köfte", await kitchenPrinter.NextTextAsync());
    }

    [Fact]
    public async Task Bill_goes_to_the_receipt_printer_when_there_is_one()
    {
        await using var kitchenPrinter = new FakePrinter();
        await using var receiptPrinter = new FakePrinter();
        var (owner, waiter, tableId, productId) = await SetUpAsync();
        await SaveSettingsAsync(owner, kitchenPrinter.Address, receiptPrinter.Address);
        var session = await SendOrderAsync(waiter, tableId, productId);
        await kitchenPrinter.NextTextAsync(); // mutfak fişi

        var bill = await (await waiter.PostAsync($"/api/sessions/{session.Id}/print-bill", null)).Content.ReadFromJsonAsync<PrintResult>(ApiFactory.JsonOptions);

        Assert.Equal(PrintStatus.Printed, bill!.Status);
        Assert.Contains("MALİ DEĞERİ YOKTUR", await receiptPrinter.NextTextAsync());
        Assert.False(kitchenPrinter.HasMore); // hesap fişi mutfağa gitmedi
    }

    [Fact]
    public async Task Test_ticket_shows_turkish_characters()
    {
        await using var printer = new FakePrinter();
        var (owner, _, _, _) = await SetUpAsync();
        await SaveSettingsAsync(owner, printer.Address, null);

        var result = await (await owner.PostAsync("/api/print/test?target=kitchen", null)).Content.ReadFromJsonAsync<PrintResult>(ApiFactory.JsonOptions);

        Assert.Equal(PrintStatus.Printed, result!.Status);
        Assert.Contains("ÇĞİÖŞÜ  çğıöşü", await printer.NextTextAsync());
    }

    [Fact]
    public async Task Without_a_printer_tickets_are_only_previewed()
    {
        var (owner, waiter, tableId, productId) = await SetUpAsync();

        var session = await SendOrderAsync(waiter, tableId, productId);

        Assert.Null(session.PrintWarning);
        var ticket = (await owner.GetFromJsonAsync<List<PrintedTicket>>("/api/print/recent", ApiFactory.JsonOptions))!.First();
        Assert.Equal(PrintStatus.Preview, ticket.Status);
    }

    [Fact]
    public async Task Invalid_printer_address_is_rejected()
    {
        var (owner, _, _, _) = await SetUpAsync();

        var response = await owner.PutAsJsonAsync("/api/print/settings", new PrinterSettings("http://yazici", null), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------- yardımcılar

    private async Task<(HttpClient Owner, HttpClient Waiter, Guid TableId, Guid ProductId)> SetUpAsync()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var category = await MenuTests.CreateCategoryAsync(owner, "Menü");
        var product = await MenuTests.CreateProductAsync(owner, category.Id, "Şiş Köfte", 230m);
        var table = await (await owner.PostAsJsonAsync("/api/tables", new SaveTableRequest("Masa 1"), ApiFactory.JsonOptions))
            .Content.ReadFromJsonAsync<TableDto>(ApiFactory.JsonOptions);
        return (owner, await factory.LoginAsync(restaurant, UserRole.Waiter), table!.Id, product.Id);
    }

    private static async Task SaveSettingsAsync(HttpClient owner, string? kitchen, string? receipt) =>
        (await owner.PutAsJsonAsync("/api/print/settings", new PrinterSettings(kitchen, receipt), ApiFactory.JsonOptions)).EnsureSuccessStatusCode();

    private static async Task<SessionDto> SendOrderAsync(HttpClient waiter, Guid tableId, Guid productId)
    {
        var session = await (await waiter.PostAsync($"/api/tables/{tableId}/session", null)).Content.ReadFromJsonAsync<SessionDto>(ApiFactory.JsonOptions);
        var response = await waiter.PostAsJsonAsync($"/api/sessions/{session!.Id}/orders",
            new AddOrderRequest([new AddOrderItem(productId, 2)]), ApiFactory.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionDto>(ApiFactory.JsonOptions))!;
    }

    /// <summary>Hiçbir şeyin dinlemediği bir port: "yazıcı kapalı" durumu.</summary>
    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static IEnumerable<string> Sequence(byte[] bytes) =>
        Enumerable.Range(0, bytes.Length - 2).Select(i => BitConverter.ToString(bytes, i, 3));

    /// <summary>9100 portundaki fiş yazıcısı gibi davranır: her bağlantıda gelen baytları bir "fiş" olarak saklar.</summary>
    private sealed class FakePrinter : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly BlockingCollection<byte[]> _jobs = [];
        private readonly CancellationTokenSource _stop = new();

        public FakePrinter()
        {
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        public string Address => $"127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        public bool HasMore => _jobs.Count > 0;

        /// <summary>Sıradaki fişin metni (PC857'den çözülmüş, komut baytları dahil).</summary>
        public Task<string> NextTextAsync() => Task.Run(() =>
            _jobs.TryTake(out var job, TimeSpan.FromSeconds(5))
                ? EscPos.Turkish.GetString(job)
                : throw new TimeoutException("Yazıcıya fiş gelmedi."));

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    using var buffer = new MemoryStream();
                    await client.GetStream().CopyToAsync(buffer, _stop.Token);
                    _jobs.Add(buffer.ToArray());
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
