using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Adisyon.Api.Printing;
using Adisyon.Api.Reports;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Ai;

/// <summary>
/// Yapay zekanın "rapor araçları": bir iş günü aralığı için hazır sorgular. Claude veritabanına doğrudan
/// erişmez; yalnızca bunları çağırabilir. Hepsi aktif kiracı ve şubeyle sınırlıdır (kiracı filtresi + branchId),
/// hepsi yalnızca okur. Tarihler iş günüdür (05:00 → 05:00), iki uç dahil.
/// </summary>
public class SalesQueries(AdisyonDbContext db)
{
    /// <summary>Tek sorguda en fazla bu kadar gün; çok uzun aralıklar hem yavaş hem gereksiz.</summary>
    public const int MaxDays = 400;

    public async Task<SalesSummary> SummaryAsync(Guid branchId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var sessions = await ClosedSessionsAsync(branchId, start, end, cancellationToken);
        var payments = await db.Payments
            .Where(p => p.CreatedAt >= start && p.CreatedAt < end
                && db.TableSessions.Any(s => s.Id == p.TableSessionId && s.BranchId == branchId))
            .Select(p => new { p.Method, p.Amount, p.CreatedAt })
            .ToListAsync(cancellationToken);

        var days = new List<DailySales>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var (dayStart, dayEnd) = BusinessDay.Range(day);
            var daySessions = sessions.Where(s => s.ClosedAt >= dayStart && s.ClosedAt < dayEnd).ToList();
            var dayPayments = payments.Where(p => p.CreatedAt >= dayStart && p.CreatedAt < dayEnd).ToList();
            days.Add(new DailySales(
                day, day.DayOfWeek.ToString(),
                daySessions.Count, daySessions.Sum(SessionMoney.Total),
                dayPayments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount),
                dayPayments.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount)));
        }

        var sales = days.Sum(d => d.Sales);
        var count = days.Sum(d => d.ClosedBills);
        return new SalesSummary(from, to, count, sales, count == 0 ? 0 : decimal.Round(sales / count, 2),
            days.Sum(d => d.Cash), days.Sum(d => d.Card), days);
    }

    public async Task<List<ProductSales>> TopProductsAsync(Guid branchId, DateOnly from, DateOnly to, int limit, bool byRevenue, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var items = (await ClosedSessionsAsync(branchId, start, end, cancellationToken))
            .SelectMany(s => s.Orders).Where(o => o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items).Where(i => i.IsCharged);
        var grouped = items.GroupBy(i => i.ProductName)
            .Select(g => new ProductSales(g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.UnitPrice * i.Quantity)));
        return (byRevenue ? grouped.OrderByDescending(p => p.Revenue) : grouped.OrderByDescending(p => p.Quantity).ThenByDescending(p => p.Revenue))
            .Take(Math.Clamp(limit, 1, 50))
            .ToList();
    }

    /// <summary>Saatlere göre satış (restoran saatiyle): hangi saatler yoğun?</summary>
    public async Task<List<HourlySales>> ByHourAsync(Guid branchId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var orders = await OrdersAsync(branchId, start, end, cancellationToken);
        return orders
            .GroupBy(o => TicketFormat.Local(o.CreatedAt).Hour)
            .OrderBy(g => g.Key)
            .Select(g => new HourlySales(g.Key, g.Count(), g.SelectMany(o => o.Items).Where(i => i.IsCharged).Sum(i => i.UnitPrice * i.Quantity)))
            .ToList();
    }

    /// <summary>Siparişi giren personele göre satış.</summary>
    public async Task<List<StaffSales>> ByStaffAsync(Guid branchId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var orders = await OrdersAsync(branchId, start, end, cancellationToken);
        var names = await db.Users.ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        return orders
            .GroupBy(o => o.CreatedByUserId)
            .Select(g => new StaffSales(
                g.Key is { } id && names.TryGetValue(id, out var name) ? name : "Bilinmiyor",
                g.Count(),
                g.SelectMany(o => o.Items).Where(i => i.IsCharged).Sum(i => i.UnitPrice * i.Quantity)))
            .OrderByDescending(s => s.Revenue)
            .ToList();
    }

    public async Task<List<AdjustmentLine>> AdjustmentsAsync(Guid branchId, DateOnly from, DateOnly to, bool voids, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var rows =
            from o in db.Orders
            where o.TableSession!.BranchId == branchId
            from i in o.Items
            let adjustedAt = voids ? i.VoidedAt : i.CompedAt
            let adjustedBy = voids ? i.VoidedByUserId : i.CompedByUserId
            where adjustedAt >= start && adjustedAt < end
            orderby adjustedAt
            select new AdjustmentLine(adjustedAt!.Value, o.TableSession!.Table!.Name, i.ProductName, i.Quantity,
                i.UnitPrice * i.Quantity, db.Users.Where(u => u.Id == adjustedBy).Select(u => u.DisplayName).FirstOrDefault());
        return await rows.Take(500).ToListAsync(cancellationToken);
    }

    private Task<List<TableSession>> ClosedSessionsAsync(Guid branchId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken) =>
        db.TableSessions
            .Where(s => s.BranchId == branchId && s.Status == TableSessionStatus.Closed && s.MergedIntoSessionId == null
                && s.ClosedAt >= start && s.ClosedAt < end)
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    private Task<List<Order>> OrdersAsync(Guid branchId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken) =>
        db.Orders
            .Where(o => o.TableSession!.BranchId == branchId && o.Status != OrderStatus.Cancelled
                && o.CreatedAt >= start && o.CreatedAt < end)
            .Include(o => o.Items)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    /// <summary>Başlangıç gününün 05:00'i ile bitiş gününün ertesi 05:00'i arası.</summary>
    private static (DateTimeOffset Start, DateTimeOffset End) Range(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.");
        }
        if (to.DayNumber - from.DayNumber >= MaxDays)
        {
            throw new ArgumentException($"En fazla {MaxDays} günlük aralık sorgulanabilir.");
        }
        return (BusinessDay.Range(from).Start, BusinessDay.Range(to).End);
    }
}

public record SalesSummary(DateOnly From, DateOnly To, int ClosedBills, decimal Sales, decimal AverageBill, decimal Cash, decimal Card, List<DailySales> Days);
public record DailySales(DateOnly Date, string DayOfWeek, int ClosedBills, decimal Sales, decimal Cash, decimal Card);
public record ProductSales(string ProductName, int Quantity, decimal Revenue);
public record HourlySales(int Hour, int Orders, decimal Revenue);
public record StaffSales(string StaffName, int Orders, decimal Revenue);
