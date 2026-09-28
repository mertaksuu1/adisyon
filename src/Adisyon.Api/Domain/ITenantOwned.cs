namespace Adisyon.Api.Domain;

/// <summary>
/// Bir kiracıya ait olan her varlık bu arayüzü uygular.
/// AdisyonDbContext bu arayüzü görünce iki şey yapar:
///   1) Sorgulara otomatik "WHERE tenant_id = aktif kiracı" filtresi ekler.
///   2) Kaydederken TenantId'yi aktif kiracıyla doldurur, başka kiracıya yazmayı engeller.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
