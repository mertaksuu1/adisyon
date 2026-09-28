using Adisyon.Api.Printing;

namespace Adisyon.Api.Reports;

/// <summary>
/// İş günü: restoranlar gece yarısından sonra da açık olduğu için gün sabah 05:00'te başlar.
/// Ör. 28 Eylül iş günü = 28 Eylül 05:00 → 29 Eylül 05:00 (İstanbul saati). 00:30'da kapanan hesap bir önceki güne yazılır.
/// </summary>
public static class BusinessDay
{
    public const int StartHour = 5;

    /// <summary>Verilen andaki iş günü (ör. 29 Eylül 01:00 → 28 Eylül).</summary>
    public static DateOnly Of(DateTimeOffset instant)
    {
        var local = TicketFormat.Local(instant);
        return DateOnly.FromDateTime(local.Hour < StartHour ? local.Date.AddDays(-1) : local.Date);
    }

    /// <summary>
    /// İş gününün başlangıç ve bitiş anları (bitiş hariç), UTC olarak. Veritabanı saatleri UTC tutar ve
    /// PostgreSQL sürücüsü sorgularda yalnızca UTC kabul eder; ekranda/fişte yerel saate çevrilir.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Range(DateOnly day)
    {
        var zone = TicketFormat.RestaurantTimeZone;
        var startLocal = day.ToDateTime(new TimeOnly(StartHour, 0));
        var start = new DateTimeOffset(startLocal, zone.GetUtcOffset(startLocal)).ToUniversalTime();
        var endLocal = startLocal.AddDays(1);
        var end = new DateTimeOffset(endLocal, zone.GetUtcOffset(endLocal)).ToUniversalTime();
        return (start, end);
    }
}
