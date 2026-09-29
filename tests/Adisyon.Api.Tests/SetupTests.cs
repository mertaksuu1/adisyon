using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;
using Adisyon.Api.Domain;

namespace Adisyon.Api.Tests;

/// <summary>
/// İlk kurulum sihirbazı. Boş bir veritabanı gerektirdiği için diğer testlerle paylaşılmayan,
/// kendi ayrı veritabanı dosyasını kullanır (IClassFixture: bu sınıfa özel ApiFactory).
/// </summary>
public class SetupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task First_run_creates_the_restaurant_logs_in_the_owner_and_then_closes()
    {
        var client = factory.CreateClient();
        Assert.True((await client.GetFromJsonAsync<SetupStatus>("/api/setup/status"))!.NeedsSetup);

        var response = await client.PostAsJsonAsync("/api/setup",
            new SetupRequest("Kebapçı Mehmet", "Merkez", "Mehmet Usta", "2580", "Kasa bilgisayarı"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setup = (await response.Content.ReadFromJsonAsync<SetupResponse>(ApiFactory.JsonOptions))!;
        Assert.Equal(("Kebapçı Mehmet", "Merkez"), (setup.Device.TenantName, setup.Device.BranchName));

        // Dönen token ile doğrudan işletme sahibi olarak çalışılabilir.
        var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Login.Token);
        var me = await owner.GetFromJsonAsync<UserDto>("/api/auth/me", ApiFactory.JsonOptions);
        Assert.Equal(("Mehmet Usta", UserRole.Owner), (me!.DisplayName, me.Role));

        // Bu bilgisayar eşleştirilmiş: sahip PIN'iyle yeniden girebilir.
        Assert.Equal(HttpStatusCode.OK, (await factory.PinLoginAsync(setup.Device.DeviceToken, "2580")).StatusCode);

        // Kurulum bir kez yapılır; sonra kapanır.
        Assert.False((await client.GetFromJsonAsync<SetupStatus>("/api/setup/status"))!.NeedsSetup);
        var again = await client.PostAsJsonAsync("/api/setup",
            new SetupRequest("Başka Restoran", "Şube", "Kötü Niyetli", "1111", "Laptop"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Owner_pin_must_be_four_digits()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/setup",
            new SetupRequest("Restoran", "Şube", "Sahip", "12", "Kasa"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
