namespace Adisyon.Api.Domain;

/// <summary>
/// Sipariş: bir adisyona tek seferde eklenen ürünler. Mutfağa tek bir fiş olarak düşer.
/// Bir adisyonda birden çok sipariş olur (önce içecekler, sonra yemekler, sonra tatlı...).
/// </summary>
public class Order : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TableSessionId { get; set; }
    public TableSession? TableSession { get; set; }

    public OrderSource Source { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Siparişi giren personel. QR siparişlerinde boştur. Garson bazlı raporlar için.</summary>
    public Guid? CreatedByUserId { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}

public enum OrderSource
{
    /// <summary>Müşteri masadaki QR menüden verdi.</summary>
    Qr,
    /// <summary>Garson veya kasa girdi.</summary>
    Staff,
}

public enum OrderStatus
{
    New,
    Preparing,
    Ready,
    Served,
    Cancelled,
}
