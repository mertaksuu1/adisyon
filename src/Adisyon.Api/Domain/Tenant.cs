namespace Adisyon.Api.Domain;

/// <summary>
/// Kiracı: sistemi kullanan bir işletme (restoran markası). SaaS'ta her müşteri bir Tenant'tır.
/// Diğer tüm veriler bir Tenant'a aittir ve başka kiracılar onları asla göremez.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>URL'lerde kullanılan kısa ad, ör. "kebapci-mehmet".</summary>
    public required string Slug { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
