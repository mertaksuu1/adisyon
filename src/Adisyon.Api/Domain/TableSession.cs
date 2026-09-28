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
    /// Adisyona son sipariş eklendiği an. Her siparişte güncellenir; bu sayede satır değişir ve
    /// Version (xmin) da değişir. Kapanıştaki "adisyon siz bakarken değişti" kontrolü buna dayanır.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Eşzamanlılık kontrolü: iki garson aynı adisyonu aynı anda değiştirirse ikincisi hata alır
    /// ve güncel veriyi yeniden yükler. PostgreSQL'in gizli "xmin" kolonuna eşlenir.
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
