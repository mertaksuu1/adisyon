using System.ComponentModel.DataAnnotations;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>
/// Menü: kategoriler ve ürünler. Herkes okuyabilir. Ürünleri sahip, yönetici ve garson;
/// kategorileri yalnızca sahip ve yönetici değiştirebilir.
/// Silme yok: geçmiş adisyonlar ürünlere bağlı olduğu için ürün/kategori "pasif" yapılır.
/// </summary>
[ApiController]
[Route("api")]
public class MenuController(AdisyonDbContext db) : ControllerBase
{
    /// <summary>
    /// Tüm menü, kategorilere göre gruplanmış ve sıralanmış. Pasif kayıtlar da gelir (yönetim ekranı
    /// onları gösterir); sipariş ekranları IsActive'e göre süzer.
    /// </summary>
    [HttpGet("menu")]
    public async Task<List<CategoryDto>> GetMenu(CancellationToken cancellationToken)
    {
        var categories = await db.Categories
            .Include(c => c.Products)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return categories.Select(CategoryDto.From).ToList();
    }

    [HttpPost("categories")]
    [Authorize(Roles = RoleNames.Management)]
    public async Task<ActionResult<CategoryDto>> CreateCategory(SaveCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = new Category { Name = request.Name.Trim(), SortOrder = request.SortOrder, IsActive = request.IsActive };
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetMenu), CategoryDto.From(category));
    }

    [HttpPut("categories/{id:guid}")]
    [Authorize(Roles = RoleNames.Management)]
    public async Task<ActionResult<CategoryDto>> UpdateCategory(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken)
    {
        // Kiracı filtresi sayesinde başka restoranın kategorisi burada "bulunamadı" olur.
        var category = await db.Categories.Include(c => c.Products)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.Name = request.Name.Trim();
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return CategoryDto.From(category);
    }

    [HttpPost("products")]
    [Authorize(Roles = RoleNames.MenuEditors)]
    public async Task<ActionResult<ProductDto>> CreateProduct(SaveProductRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Kategori bulunamadı.");
        }

        var product = new Product { Name = "" };
        request.ApplyTo(product);
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetMenu), ProductDto.From(product));
    }

    [HttpPut("products/{id:guid}")]
    [Authorize(Roles = RoleNames.MenuEditors)]
    public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, SaveProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Kategori bulunamadı.");
        }

        // Fiyat değişikliği geçmiş adisyonları etkilemez: sipariş satırları eski fiyatın kopyasını tutar.
        request.ApplyTo(product);
        await db.SaveChangesAsync(cancellationToken);
        return ProductDto.From(product);
    }
}

public record SaveCategoryRequest(
    [Required, MaxLength(100)] string Name,
    int SortOrder = 0,
    bool IsActive = true);

public record SaveProductRequest(
    Guid CategoryId,
    [Required, MaxLength(200)] string Name,
    [Range(0, 99_999_999)] decimal Price,
    [MaxLength(1000)] string? Description = null,
    int SortOrder = 0,
    bool IsActive = true)
{
    public void ApplyTo(Product product)
    {
        product.CategoryId = CategoryId;
        product.Name = Name.Trim();
        product.Price = decimal.Round(Price, 2);
        product.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        product.SortOrder = SortOrder;
        product.IsActive = IsActive;
    }
}

public record CategoryDto(Guid Id, string Name, int SortOrder, bool IsActive, List<ProductDto> Products)
{
    public static CategoryDto From(Category c) => new(
        c.Id, c.Name, c.SortOrder, c.IsActive,
        c.Products.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).Select(ProductDto.From).ToList());
}

public record ProductDto(Guid Id, Guid CategoryId, string Name, string? Description, decimal Price, int SortOrder, bool IsActive)
{
    public static ProductDto From(Product p) => new(p.Id, p.CategoryId, p.Name, p.Description, p.Price, p.SortOrder, p.IsActive);
}
