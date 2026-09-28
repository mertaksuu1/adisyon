using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Adisyon.Api.Printing;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Reports;

/// <summary>Gün sonu (Z) raporu: bir şubenin bir iş günündeki tahsilat, satış, iptal, ikram ve en çok satanlar.</summary>
public record ZReport(
    DateOnly Date,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal CashTotal,
    decimal CardTotal,
    decimal PaymentsTotal,
    int ClosedSessionCount,
    decimal SalesTotal,
    decimal AverageBill,
    List<AdjustmentLine> Voids,
    decimal VoidsTotal,
    List<AdjustmentLine> Comps,
    decimal CompsTotal,
    List<TopProduct> TopProducts,
    int OpenTableCount,
    decimal OpenTablesTotal)
{
    public static async Task<ZReport> BuildAsync(AdisyonDbContext db, Guid branchId, DateOnly date, CancellationToken cancellationToken)
    {
        var (from, to) = BusinessDay.Range(date);

        // Tahsilat: bu iş gününde alınan ödemeler (kasa sayımıyla karşılaştırılacak rakam).
        var payments = await db.Payments
            .Where(p => p.CreatedAt >= from && p.CreatedAt < to
                && db.TableSessions.Any(s => s.Id == p.TableSessionId && s.BranchId == branchId))
            .Select(p => new { p.Method, p.Amount })
            .ToListAsync(cancellationToken);
        var cash = payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
        var card = payments.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount);

        // Satış: bu iş gününde kapanan adisyonlar. Başka masayla birleştirilip kapanan adisyonlar sayılmaz
        // (siparişleri birleştirildikleri adisyonda sayılır).
        var closed = await db.TableSessions
            .Where(s => s.BranchId == branchId && s.Status == TableSessionStatus.Closed
                && s.MergedIntoSessionId == null && s.ClosedAt >= from && s.ClosedAt < to)
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var sales = closed.Sum(SessionMoney.Total);
        var soldItems = closed.SelectMany(s => s.Orders).Where(o => o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items).Where(i => i.IsCharged).ToList();
        var topProducts = soldItems
            .GroupBy(i => i.ProductName)
            .Select(g => new TopProduct(g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.UnitPrice * i.Quantity)))
            .OrderByDescending(p => p.Quantity).ThenByDescending(p => p.Amount)
            .Take(10)
            .ToList();

        // İptal ve ikramlar, işlem saatine göre (kim, ne zaman, hangi masa).
        var voids = await AdjustmentsAsync(db, branchId, isVoid: true, from, to, cancellationToken);
        var comps = await AdjustmentsAsync(db, branchId, isVoid: false, from, to, cancellationToken);

        // Rapor alınırken hâlâ açık olan masalar: gün kapatılmadan önce ödenmeli.
        var open = await db.TableSessions
            .Where(s => s.BranchId == branchId && s.Status == TableSessionStatus.Open)
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .Include(s => s.Payments)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new ZReport(
            date, from, to,
            cash, card, cash + card,
            closed.Count, sales, closed.Count == 0 ? 0 : decimal.Round(sales / closed.Count, 2),
            voids, voids.Sum(v => v.Amount),
            comps, comps.Sum(c => c.Amount),
            topProducts,
            open.Count, open.Sum(SessionMoney.Remaining));
    }

    private static Task<List<AdjustmentLine>> AdjustmentsAsync(
        AdisyonDbContext db, Guid branchId, bool isVoid, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        // Not: "from", "by" gibi kelimeler sorgu sözdiziminde ayrılmış; değişken adı olarak kullanılamaz.
        var rows =
            from o in db.Orders
            where o.TableSession!.BranchId == branchId
            from i in o.Items
            let adjustedAt = isVoid ? i.VoidedAt : i.CompedAt
            let adjustedBy = isVoid ? i.VoidedByUserId : i.CompedByUserId
            where adjustedAt >= start && adjustedAt < end
            orderby adjustedAt
            select new AdjustmentLine(
                adjustedAt!.Value,
                o.TableSession!.Table!.Name,
                i.ProductName,
                i.Quantity,
                i.UnitPrice * i.Quantity,
                db.Users.Where(u => u.Id == adjustedBy).Select(u => u.DisplayName).FirstOrDefault());
        return rows.ToListAsync(cancellationToken);
    }
}

/// <summary>Bir iptal veya ikram satırı.</summary>
public record AdjustmentLine(DateTimeOffset At, string TableName, string ProductName, int Quantity, decimal Amount, string? ByName);

public record TopProduct(string ProductName, int Quantity, decimal Amount);
