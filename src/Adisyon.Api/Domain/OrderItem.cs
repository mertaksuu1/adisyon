namespace Adisyon.Api.Domain;

/// <summary>Siparişteki tek satır, ör. "2 x Mercimek Çorbası".</summary>
public class OrderItem : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>
    /// Siparişteki sıra (0, 1, 2...). Garsonun girdiği sırayı korumak için gerekli; aynı anda kaydedilen
    /// satırların kimliklerinden (UUIDv7) sıra çıkarılamaz, çünkü milisaniye içinde rastgeledirler.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Ürün adı ve fiyatı sipariş anında kopyalanır ("snapshot"). Yarın menüde fiyat değişse bile
    /// bugünkü adisyon eski fiyatla kalmalı; yoksa geçmiş raporlar ve fişler bozulur.
    /// </summary>
    public required string ProductName { get; set; }
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    /// <summary>Mutfağa not, ör. "soğansız".</summary>
    public string? Note { get; set; }

    /// <summary>
    /// İptal: ürün hesaptan düşer ama silinmez. Kimin ne zaman iptal ettiği Z raporunda görünür
    /// (kasa açığı ve suistimal kontrolü için).
    /// </summary>
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedByUserId { get; set; }

    /// <summary>İkram: ürün fişte görünür ama ücreti alınmaz.</summary>
    public DateTimeOffset? CompedAt { get; set; }
    public Guid? CompedByUserId { get; set; }

    /// <summary>Hesaba yansıyor mu? İptal ve ikram edilen satırlar yansımaz.</summary>
    public bool IsCharged => VoidedAt is null && CompedAt is null;

    /// <summary>
    /// Satırdan belirli adedi ayırıp yeni bir satır yapar; ör. "2 x Kebap"tan 1'ini iptal etmek için.
    /// Adedin tamamı istenirse satırın kendisini döndürür.
    /// </summary>
    public OrderItem SplitOff(int quantity)
    {
        if (quantity == Quantity)
        {
            return this;
        }

        Quantity -= quantity;
        return new OrderItem
        {
            TenantId = TenantId,
            OrderId = OrderId,
            ProductId = ProductId,
            Position = Position,
            ProductName = ProductName,
            UnitPrice = UnitPrice,
            Quantity = quantity,
            Note = Note,
        };
    }
}
