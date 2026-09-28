namespace Adisyon.Api.Domain;

/// <summary>
/// Bir adisyona alınan ödeme. Hesap bölünürse bir adisyonda birden çok ödeme olur
/// (ör. 400 ₺ kart + 215 ₺ nakit). Kalan tutar sıfırlanınca adisyon kapanır.
/// </summary>
public class Payment : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TableSessionId { get; set; }

    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Ödemeyi alan personel (Z raporu ve kasa sayımı için).</summary>
    public Guid CreatedByUserId { get; set; }
}

public enum PaymentMethod
{
    Cash,
    Card,
}
