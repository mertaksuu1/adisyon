namespace Adisyon.Api.Domain;

/// <summary>
/// Restorana bağlanmış (eşleştirilmiş) bir bilgisayar veya tablet. PIN girişi yalnızca
/// eşleştirilmiş cihazlarda çalışır; böylece PIN'i bilen biri dışarıdan giriş yapamaz.
/// </summary>
public class Device : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }

    /// <summary>Kurulumda verilen ad, ör. "Kasa bilgisayarı" veya "Mutfak tableti".</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Cihazın tarayıcısında saklanan gizli anahtarın SHA-256 özeti. Anahtarın kendisi sunucuda tutulmaz.
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>Art arda yanlış PIN sayısı. 5'e ulaşınca cihaz kısa süre kilitlenir.</summary>
    public int FailedPinAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Çalınan/kaybolan cihaz yönetim ekranından devre dışı bırakılabilir.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}
