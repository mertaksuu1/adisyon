using System.Net;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;
using Adisyon.Api.Domain;

namespace Adisyon.Api.Tests;

/// <summary>Yönetim ekranının arka ucu: personel düzenleme, PIN değiştirme, cihazlar, sıralama.</summary>
[Collection(nameof(ApiCollection))]
public class AdminTests(ApiFactory factory)
{
    private static async Task<UserDto> CreateUserAsync(HttpClient owner, UserRole role, string pin, string name = "Yeni Personel")
    {
        var response = await owner.PostAsJsonAsync("/api/users", new CreateUserRequest(name, pin, role, null), ApiFactory.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(ApiFactory.JsonOptions))!;
    }

    [Fact]
    public async Task Changing_a_pin_takes_effect_immediately()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var waiter = await CreateUserAsync(owner, UserRole.Waiter, "4321");
        var device = await factory.PairDeviceAsync(restaurant.PairingCode);

        var change = await owner.PutAsJsonAsync($"/api/users/{waiter.Id}/pin", new ChangePinRequest("8765"), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.PinLoginAsync(device, "4321")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.PinLoginAsync(device, "8765")).StatusCode);
    }

    [Fact]
    public async Task Deactivated_staff_cannot_log_in()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var waiter = await CreateUserAsync(owner, UserRole.Waiter, "4321");
        var device = await factory.PairDeviceAsync(restaurant.PairingCode);

        var update = await owner.PutAsJsonAsync($"/api/users/{waiter.Id}",
            new UpdateUserRequest("Ayrılan Garson", UserRole.Waiter, null, IsActive: false), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.PinLoginAsync(device, "4321")).StatusCode);
    }

    [Fact]
    public async Task Pin_already_used_by_someone_else_is_rejected()
    {
        var (owner, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);
        await CreateUserAsync(owner, UserRole.Waiter, "1111");
        var cashier = await CreateUserAsync(owner, UserRole.Cashier, "2222");

        var response = await owner.PutAsJsonAsync($"/api/users/{cashier.Id}/pin", new ChangePinRequest("1111"), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_touch_owner_or_promote_but_can_change_own_pin()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var manager = await factory.LoginAsync(restaurant, UserRole.Manager);
        var users = (await owner.GetFromJsonAsync<List<UserDto>>("/api/users", ApiFactory.JsonOptions))!;
        var ownerUser = users.Single(u => u.Role == UserRole.Owner);
        var managerUser = users.Single(u => u.Role == UserRole.Manager);
        var waiter = await CreateUserAsync(owner, UserRole.Waiter, "5555");

        var editOwner = await manager.PutAsJsonAsync($"/api/users/{ownerUser.Id}",
            new UpdateUserRequest("Değişti", UserRole.Owner, null), ApiFactory.JsonOptions);
        var promote = await manager.PutAsJsonAsync($"/api/users/{waiter.Id}",
            new UpdateUserRequest(waiter.DisplayName, UserRole.Manager, null), ApiFactory.JsonOptions);
        var ownPin = await manager.PutAsJsonAsync($"/api/users/{managerUser.Id}/pin", new ChangePinRequest("9090"), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, editOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, promote.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ownPin.StatusCode);
    }

    [Fact]
    public async Task Nobody_can_deactivate_themselves_or_change_own_role()
    {
        var (owner, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);
        var me = await owner.GetFromJsonAsync<UserDto>("/api/auth/me", ApiFactory.JsonOptions);

        var deactivate = await owner.PutAsJsonAsync($"/api/users/{me!.Id}",
            new UpdateUserRequest(me.DisplayName, UserRole.Owner, null, IsActive: false), ApiFactory.JsonOptions);
        var demote = await owner.PutAsJsonAsync($"/api/users/{me.Id}",
            new UpdateUserRequest(me.DisplayName, UserRole.Waiter, null), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
    }

    [Fact]
    public async Task Revoked_device_can_no_longer_log_in_but_current_device_cannot_be_revoked()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        await CreateUserAsync(owner, UserRole.Waiter, "4321");
        var lostTablet = await factory.PairDeviceAsync(restaurant.PairingCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.PinLoginAsync(lostTablet, "4321")).StatusCode);

        var devices = (await owner.GetFromJsonAsync<List<DeviceDto>>("/api/devices", ApiFactory.JsonOptions))!;
        var current = devices.Single(d => d.IsCurrent);
        var tablet = devices.Where(d => !d.IsCurrent).OrderByDescending(d => d.CreatedAt).First();

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/devices/{tablet.Id}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.PinLoginAsync(lostTablet, "4321")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/devices/{current.Id}/revoke", null)).StatusCode);
    }

    [Fact]
    public async Task New_pairing_code_replaces_the_old_one()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var branch = Assert.Single((await owner.GetFromJsonAsync<List<BranchDto>>("/api/branches", ApiFactory.JsonOptions))!);

        var response = await owner.PostAsync($"/api/branches/{branch.Id}/pairing-code", null);
        var code = (await response.Content.ReadFromJsonAsync<PairingCodeResponse>(ApiFactory.JsonOptions))!.PairingCode;

        Assert.Matches(@"^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", code);
        Assert.False(string.IsNullOrEmpty(await factory.PairDeviceAsync(code)));
        var old = await factory.CreateClient().PostAsJsonAsync("/api/auth/pair", new PairDeviceRequest(restaurant.PairingCode, "Eski kod"));
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
    }

    [Fact]
    public async Task New_products_go_to_the_end_and_edits_keep_their_place()
    {
        var (owner, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);
        var category = await MenuTests.CreateCategoryAsync(owner, "Menü");
        var first = await MenuTests.CreateProductAsync(owner, category.Id, "Birinci", 10m);
        await MenuTests.CreateProductAsync(owner, category.Id, "İkinci", 10m);

        // Fiyatı değişen ürün yerinde kalmalı.
        (await owner.PutAsJsonAsync($"/api/products/{first.Id}", new SaveProductRequest(category.Id, "Birinci", 15m), ApiFactory.JsonOptions))
            .EnsureSuccessStatusCode();
        var menu = (await owner.GetFromJsonAsync<List<CategoryDto>>("/api/menu", ApiFactory.JsonOptions))!;

        Assert.Equal(["Birinci", "İkinci"], menu.Single().Products.Select(p => p.Name));
    }

    [Fact]
    public async Task Table_with_an_open_bill_cannot_be_deactivated()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var table = (await (await owner.PostAsJsonAsync("/api/tables", new SaveTableRequest("Masa 1"), ApiFactory.JsonOptions))
            .Content.ReadFromJsonAsync<TableDto>(ApiFactory.JsonOptions))!;
        (await owner.PostAsync($"/api/tables/{table.Id}/session", null)).EnsureSuccessStatusCode();

        var response = await owner.PutAsJsonAsync($"/api/tables/{table.Id}",
            new SaveTableRequest("Masa 1", IsActive: false), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deactivating_staff_ends_their_open_session_immediately()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var waiter = await factory.LoginAsync(restaurant, UserRole.Waiter);
        var me = await waiter.GetFromJsonAsync<UserDto>("/api/auth/me", ApiFactory.JsonOptions);

        (await owner.PutAsJsonAsync($"/api/users/{me!.Id}",
            new UpdateUserRequest(me.DisplayName, UserRole.Waiter, null, IsActive: false), ApiFactory.JsonOptions)).EnsureSuccessStatusCode();

        // Garsonun elindeki giriş kartı hâlâ "geçerli imzalı", ama artık kabul edilmemeli.
        Assert.Equal(HttpStatusCode.Unauthorized, (await waiter.GetAsync("/api/tables")).StatusCode);
    }

    [Fact]
    public async Task Revoking_a_device_ends_sessions_on_it_immediately()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var owner = await factory.LoginAsync(restaurant, UserRole.Owner);
        var waiter = await factory.LoginAsync(restaurant, UserRole.Waiter); // kendi cihazında
        var devices = (await owner.GetFromJsonAsync<List<DeviceDto>>("/api/devices", ApiFactory.JsonOptions))!;
        var waiterDevice = devices.Where(d => !d.IsCurrent).OrderByDescending(d => d.CreatedAt).First();

        (await owner.PostAsync($"/api/devices/{waiterDevice.Id}/revoke", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await waiter.GetAsync("/api/tables")).StatusCode);
    }

    [Fact]
    public async Task Unknown_device_gets_a_code_the_screen_can_act_on()
    {
        var response = await factory.PinLoginAsync("silinmis-veya-uydurma-cihaz", "1234");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(AuthController.DeviceNotRecognizedCode, await response.Content.ReadAsStringAsync());
    }
}
