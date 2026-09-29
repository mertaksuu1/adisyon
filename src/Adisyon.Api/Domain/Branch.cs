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

    /// <summary>
    /// Mutfak yazıcısının ağ adresi, ör. "192.168.1.50" veya "192.168.1.50:9100". Boşsa fişler yalnızca
    /// ekranda önizlenir. Mutfak, iptal ve masa değişikliği fişleri buraya gider.
    /// </summary>
    public string? KitchenPrinterAddress { get; set; }

    /// <summary>Kasa yazıcısı: hesap fişi ve Z raporu. Boşsa bunlar da mutfak yazıcısından çıkar.</summary>
    public string? ReceiptPrinterAddress { get; set; }

    /// <summary>
    /// Yazıcının Türkçe karakter tablosu numarası (ESC/POS "ESC t n"). Epson ve çoğu uyumlu yazıcıda
    /// Türkçe (PC857) = 13. Test fişinde "ş, ğ, ı, İ" bozuk çıkarsa yazıcının kılavuzundaki numara girilir.
    /// </summary>
    public int PrinterCodePage { get; set; } = 13;

    public List<DiningTable> Tables { get; set; } = [];
}
