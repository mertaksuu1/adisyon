namespace Adisyon.Api.Domain;

/// <summary>
/// Adisyon: bir masanın açılışından hesabın kapanışına kadar geçen oturum.
/// Aynı masa gün içinde birçok kez açılıp kapanır; her seferinde yeni bir TableSession oluşur.
/// </summary>
public class TableSession : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid TableId { get; set; }
    public DiningTable? Table { get; set; }

    public TableSessionStatus Status { get; set; } = TableSessionStatus.Open;
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// Adisyonun son değiştiği an. Sipariş eklemek gibi işlemler bunu güncelleyerek adisyonu "değişti"
    /// sayar; böylece Version artar ve açık ödeme ekranları eskimiş olur.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Eşzamanlılık kontrolü (sürüm sayacı): adisyon her kaydedildiğinde bir artar (AdisyonDbContext).
    /// İstemci gördüğü sürümü gönderir; o arada başkası değiştirdiyse kayıt reddedilir ve
    /// kullanıcı güncel adisyonu görür ("adisyon siz bakarken değişti").
    /// </summary>
    public uint Version { get; set; }

    /// <summary>Bu adisyon başka bir masanınkiyle birleştirildiyse, birleştirildiği adisyon.</summary>
    public Guid? MergedIntoSessionId { get; set; }

    public List<Order> Orders { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public enum TableSessionStatus
{
    Open,
    Closed,
}
