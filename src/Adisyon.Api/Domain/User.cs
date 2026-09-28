namespace Adisyon.Api.Domain;

/// <summary>
/// Restoran personeli: sahip, yönetici, garson, mutfak veya kasa.
/// E-posta yok: personel eşleştirilmiş restoran bilgisayarında yalnızca 4 haneli PIN'le giriş yapar.
/// </summary>
public class User : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>
    /// PIN'in kendisi değil, gizli anahtarla alınmış HMAC özeti (bkz. SecretHasher.HashPin).
    /// Aynı restoranda iki kişinin PIN'i aynı olamaz; giriş anında kişiyi PIN'den buluyoruz.
    /// </summary>
    public string PinHash { get; set; } = "";

    public UserRole Role { get; set; }

    /// <summary>Hangi şubede çalıştığı. null ise tüm şubeler (ör. işletme sahibi).</summary>
    public Guid? BranchId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public enum UserRole
{
    /// <summary>İşletme sahibi: her şeye erişir, yönetici ekleyebilir.</summary>
    Owner,
    /// <summary>Şube yöneticisi: menü, masa ve personel yönetir.</summary>
    Manager,
    /// <summary>Garson: sipariş alır, masaları görür.</summary>
    Waiter,
    /// <summary>Mutfak: gelen siparişleri görür ve durumunu günceller.</summary>
    Kitchen,
    /// <summary>Kasa: adisyon kapatır, ödeme alır.</summary>
    Cashier,
}
