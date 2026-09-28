namespace Adisyon.Api.Tenancy;

/// <summary>
/// Şu anki HTTP isteğinin hangi kiracı adına yapıldığını tutar. Her istek için ayrı bir örnek
/// oluşur (Scoped). Bir sonraki adımda giriş sistemi, kullanıcının token'ından TenantId'yi
/// okuyup buraya yazacak.
/// </summary>
public class TenantContext
{
    /// <summary>null ise istek henüz bir kiracıya bağlanmadı; bu durumda hiçbir kiracı verisi görünmez.</summary>
    public Guid? TenantId { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        if (TenantId is not null && TenantId != tenantId)
        {
            throw new InvalidOperationException("Bir istek içinde kiracı değiştirilemez.");
        }

        TenantId = tenantId;
    }
}
