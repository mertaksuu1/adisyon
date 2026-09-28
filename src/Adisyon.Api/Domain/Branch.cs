namespace Adisyon.Api.Domain;

/// <summary>Şube: bir kiracının fiziksel restoranı. Bir marka birden çok şubeye sahip olabilir.</summary>
public class Branch : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }

    /// <summary>
    /// Yeni bir cihazı bu şubeye bağlamak için girilen eşleştirme kodunun SHA-256 özeti.
    /// Kodun kendisi yalnızca oluşturulduğu anda gösterilir; unutulursa yenisi üretilir.
    /// </summary>
    public string? PairingCodeHash { get; set; }

    public List<DiningTable> Tables { get; set; } = [];
}
