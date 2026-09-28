namespace Adisyon.Api.Domain;

/// <summary>Siparişteki tek satır, ör. "2 x Mercimek Çorbası".</summary>
public class OrderItem : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>
    /// Ürün adı ve fiyatı sipariş anında kopyalanır ("snapshot"). Yarın menüde fiyat değişse bile
    /// bugünkü adisyon eski fiyatla kalmalı; yoksa geçmiş raporlar ve fişler bozulur.
    /// </summary>
    public required string ProductName { get; set; }
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    /// <summary>Mutfağa not, ör. "soğansız".</summary>
    public string? Note { get; set; }
}
