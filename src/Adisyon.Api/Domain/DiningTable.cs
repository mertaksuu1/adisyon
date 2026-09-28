namespace Adisyon.Api.Domain;

/// <summary>
/// Masa. Adı "DiningTable" çünkü "Table" .NET'te başka anlamlarda da kullanılan bir isim.
/// </summary>
public class DiningTable : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }

    /// <summary>Ekranda görünen ad, ör. "Masa 5" veya "Bahçe 2".</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Masadaki QR kodun içindeki tahmin edilemez anahtar. Müşteri /m/{QrToken} adresine gider.
    /// Masa Id'si yerine bunu kullanıyoruz ki kimse adresi değiştirip başka masaya sipariş veremesin.
    /// </summary>
    public required string QrToken { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
