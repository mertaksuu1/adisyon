using System.Net;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;
using Adisyon.Api.Domain;

namespace Adisyon.Api.Tests;

[Collection(nameof(ApiCollection))]
public class MenuTests(ApiFactory factory)
{
    [Fact]
    public async Task Manager_builds_menu_and_waiter_reads_it_in_order()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var manager = await factory.LoginAsync(restaurant, UserRole.Manager);
        var waiter = await factory.LoginAsync(restaurant, UserRole.Waiter);

        var drinks = await CreateCategoryAsync(manager, "İçecekler", sortOrder: 2);
        var soups = await CreateCategoryAsync(manager, "Çorbalar", sortOrder: 1);
        await CreateProductAsync(manager, soups.Id, "Mercimek", 90m);
        await CreateProductAsync(manager, drinks.Id, "Ayran", 40m);

        var menu = (await waiter.GetFromJsonAsync<List<CategoryDto>>("/api/menu", ApiFactory.JsonOptions))!;

        Assert.Equal(["Çorbalar", "İçecekler"], menu.Select(c => c.Name));
        Assert.Equal("Mercimek", Assert.Single(menu[0].Products).Name);
    }

    [Fact]
    public async Task Waiter_can_add_products_but_not_categories()
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var manager = await factory.LoginAsync(restaurant, UserRole.Manager);
        var waiter = await factory.LoginAsync(restaurant, UserRole.Waiter);
        var category = await CreateCategoryAsync(manager, "Ana Yemekler");

        var product = await waiter.PostAsJsonAsync("/api/products",
            new SaveProductRequest(category.Id, "Günün Yemeği", 250m), ApiFactory.JsonOptions);
        var newCategory = await waiter.PostAsJsonAsync("/api/categories",
            new SaveCategoryRequest("Tatlılar"), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Created, product.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, newCategory.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Kitchen)]
    [InlineData(UserRole.Cashier)]
    public async Task Kitchen_and_cashier_cannot_add_products(UserRole role)
    {
        var restaurant = await factory.CreateRestaurantAsync();
        var manager = await factory.LoginAsync(restaurant, UserRole.Manager);
        var staff = await factory.LoginAsync(restaurant, role);
        var category = await CreateCategoryAsync(manager, "Menü");

        var response = await staff.PostAsJsonAsync("/api/products",
            new SaveProductRequest(category.Id, "Deneme", 10m), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_edit_another_restaurants_product()
    {
        var restaurantA = await factory.CreateRestaurantAsync();
        var ownerA = await factory.LoginAsync(restaurantA, UserRole.Owner);
        var category = await CreateCategoryAsync(ownerA, "Menü");
        var product = await CreateProductAsync(ownerA, category.Id, "Kebap", 300m);

        var (ownerB, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);
        var response = await ownerB.PutAsJsonAsync($"/api/products/{product.Id}",
            new SaveProductRequest(category.Id, "Bedava Kebap", 0m), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Negative_price_is_rejected()
    {
        var (owner, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);
        var category = await CreateCategoryAsync(owner, "Menü");

        var response = await owner.PostAsJsonAsync("/api/products",
            new SaveProductRequest(category.Id, "Hatalı", -5m), ApiFactory.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static async Task<CategoryDto> CreateCategoryAsync(HttpClient client, string name, int sortOrder = 0)
    {
        var response = await client.PostAsJsonAsync("/api/categories", new SaveCategoryRequest(name, sortOrder), ApiFactory.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(ApiFactory.JsonOptions))!;
    }

    public static async Task<ProductDto> CreateProductAsync(HttpClient client, Guid categoryId, string name, decimal price)
    {
        var response = await client.PostAsJsonAsync("/api/products", new SaveProductRequest(categoryId, name, price), ApiFactory.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductDto>(ApiFactory.JsonOptions))!;
    }
}
