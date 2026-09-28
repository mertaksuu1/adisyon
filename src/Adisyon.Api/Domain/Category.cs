namespace Adisyon.Api.Domain;

/// <summary>Menü kategorisi, ör. "Çorbalar", "Ana Yemekler", "İçecekler".</summary>
public class Category : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public List<Product> Products { get; set; } = [];
}
