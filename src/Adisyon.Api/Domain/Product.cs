namespace Adisyon.Api.Domain;

/// <summary>Menüdeki bir ürün. Menü şimdilik kiracı genelinde ortak; şubeye özel fiyat ileride eklenebilir.</summary>
public class Product : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Para her zaman decimal ile tutulur; double/float yuvarlama hatası yapar.</summary>
    public decimal Price { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
