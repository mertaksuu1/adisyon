using System.Net;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Adisyon.Api.Tests;

[Collection(nameof(ApiCollection))]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Paired_device_and_correct_pin_logs_in()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        await factory.AddStaffAsync(restaurant.TenantId, UserRole.Waiter, "3333");
        var deviceToken = await factory.PairDeviceAsync(restaurant.PairingCode);

        var response = await factory.PinLoginAsync(deviceToken, "3333");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(ApiFactory.JsonOptions);
        Assert.Equal(UserRole.Waiter, login!.User.Role);
        Assert.Equal(restaurant.TenantId, login.User.TenantId);
    }

    [Fact]
    public async Task Pairing_code_is_accepted_in_lowercase_and_without_dashes()
    {
        var restaurant = await factory.CreateRestaurantAsync();

        var token = await factory.PairDeviceAsync(restaurant.PairingCode.Replace("-", "").ToLowerInvariant());

        Assert.False(string.IsNullOrEmpty(token));
    }

    [Fact]
    public async Task Wrong_pairing_code_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/pair",
            new PairDeviceRequest("AAAA-BBBB-CCCC", "Sahte cihaz"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pin_without_a_paired_device_is_rejected()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        await factory.AddStaffAsync(restaurant.TenantId, UserRole.Owner, "1111");

        var noHeader = await factory.CreateClient().PostAsJsonAsync("/api/auth/pin-login", new PinLoginRequest("1111"));
        var fakeDevice = await factory.PinLoginAsync("uydurma-cihaz-anahtari", "1111");

        Assert.Equal(HttpStatusCode.Unauthorized, noHeader.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, fakeDevice.StatusCode);
    }

    [Fact]
    public async Task Same_pin_in_another_restaurant_does_not_work_on_this_device()
    {
        var restaurantA = await factory.CreateRestaurantAsync();
        var restaurantB = await factory.CreateRestaurantAsync();
        await factory.AddStaffAsync(restaurantB.TenantId, UserRole.Owner, "7777");
        var deviceA = await factory.PairDeviceAsync(restaurantA.PairingCode);

        var response = await factory.PinLoginAsync(deviceA, "7777");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Staff_of_another_branch_cannot_log_in_on_this_device()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var otherBranchId = await AddBranchAsync(restaurant.TenantId);
        await factory.AddStaffAsync(restaurant.TenantId, UserRole.Waiter, "4321", otherBranchId);
        var device = await factory.PairDeviceAsync(restaurant.PairingCode);

        var response = await factory.PinLoginAsync(device, "4321");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Device_locks_after_five_wrong_pins_even_for_the_correct_pin()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        await factory.AddStaffAsync(restaurant.TenantId, UserRole.Waiter, "3333");
        var device = await factory.PairDeviceAsync(restaurant.PairingCode);

        for (var i = 0; i < AuthController.MaxFailedPinAttempts; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.PinLoginAsync(device, "0000")).StatusCode);
        }
        var correctPinWhileLocked = await factory.PinLoginAsync(device, "3333");

        Assert.Equal(HttpStatusCode.TooManyRequests, correctPinWhileLocked.StatusCode);
    }

    [Fact]
    public async Task Pin_must_be_four_digits()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var device = await factory.PairDeviceAsync(restaurant.PairingCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PinLoginAsync(device, "12")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PinLoginAsync(device, "12ab")).StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_logged_in_user()
    {
        var (client, tenantId) = await factory.CreateTenantAndLoginAsync(UserRole.Kitchen);

        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me", ApiFactory.JsonOptions);

        Assert.Equal(tenantId, me!.TenantId);
        Assert.Equal(UserRole.Kitchen, me.Role);
    }

    [Fact]
    public async Task Endpoints_require_login_by_default()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Forged_token_is_rejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.sahte-imza");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    private async Task<Guid> AddBranchAsync(Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "İkinci Şube" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        return branch.Id;
    }
}

[Collection(nameof(ApiCollection))]
public class UsersTests(ApiFactory factory)
{
    [Fact]
    public async Task Owner_can_create_staff_and_sees_only_own_restaurants_users()
    {
        var (ownerA, tenantA) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);
        await factory.CreateTenantAndLoginAsync(UserRole.Owner); // başka bir restoran

        var created = await ownerA.PostAsJsonAsync("/api/users", NewUser(UserRole.Waiter, "5678"), ApiFactory.JsonOptions);
        var users = await ownerA.GetFromJsonAsync<List<UserDto>>("/api/users", ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(2, users!.Count);
        Assert.All(users, u => Assert.Equal(tenantA, u.TenantId));
    }

    [Fact]
    public async Task Same_pin_twice_in_one_restaurant_is_rejected()
    {
        var (owner, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);

        await owner.PostAsJsonAsync("/api/users", NewUser(UserRole.Waiter, "5678"), ApiFactory.JsonOptions);
        var duplicate = await owner.PostAsJsonAsync("/api/users", NewUser(UserRole.Cashier, "5678"), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Same_pin_in_different_restaurants_is_allowed()
    {
        var (ownerA, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);
        var (ownerB, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);

        var a = await ownerA.PostAsJsonAsync("/api/users", NewUser(UserRole.Waiter, "5678"), ApiFactory.JsonOptions);
        var b = await ownerB.PostAsJsonAsync("/api/users", NewUser(UserRole.Waiter, "5678"), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);
    }

    [Fact]
    public async Task Waiter_cannot_manage_users()
    {
        var (waiter, _) = await factory.CreateTenantAndLoginAsync(UserRole.Waiter);

        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_create_an_owner()
    {
        var (manager, _) = await factory.CreateTenantAndLoginAsync(UserRole.Manager);

        var response = await manager.PostAsJsonAsync("/api/users", NewUser(UserRole.Owner, "9999"), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static CreateUserRequest NewUser(UserRole role, string pin) =>
        new("Yeni Personel", pin, role, BranchId: null);
}
