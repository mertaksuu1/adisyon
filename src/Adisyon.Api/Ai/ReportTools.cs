using System.Text.Json;
using Anthropic.Models.Beta.Messages;

namespace Adisyon.Api.Ai;

/// <summary>
/// Claude'a tanıtılan rapor araçları ve onları SalesQueries'e bağlayan yönlendirici.
/// Araç listesi sabit ve her istekte aynı sırada: önbelleğe alınabilsin.
/// </summary>
public static class ReportTools
{
    private static readonly JsonElement DateRange = Schema(new
    {
        from_date = new { type = "string", description = "Başlangıç iş günü, YYYY-MM-DD" },
        to_date = new { type = "string", description = "Bitiş iş günü (dahil), YYYY-MM-DD" },
    });

    public static readonly List<BetaToolUnion> Definitions =
    [
        Tool("sales_summary",
            "Tarih aralığında toplam satış, kapanan adisyon sayısı, ortalama hesap, nakit/kart tahsilat ve GÜN GÜN döküm. " +
            "Ciro, adisyon sayısı, gün veya hafta karşılaştırması soruları için.",
            DateRange, ["from_date", "to_date"]),
        Tool("top_products",
            "Tarih aralığında en çok satan ürünler (adet ve tutar). İptal ve ikramlar dahil değil.",
            Schema(new
            {
                from_date = new { type = "string", description = "Başlangıç iş günü, YYYY-MM-DD" },
                to_date = new { type = "string", description = "Bitiş iş günü (dahil), YYYY-MM-DD" },
                limit = new { type = "integer", description = "Kaç ürün (1-50)" },
                order_by = new { type = "string", @enum = new[] { "quantity", "revenue" }, description = "Adede mi tutara mı göre sıralansın" },
            }),
            ["from_date", "to_date", "limit", "order_by"]),
        Tool("sales_by_hour",
            "Tarih aralığında saatlere göre sipariş sayısı ve tutarı (restoran saatiyle). Yoğun saat soruları için.",
            DateRange, ["from_date", "to_date"]),
        Tool("sales_by_staff",
            "Tarih aralığında siparişi giren personele göre sipariş sayısı ve tutarı. Personel performansı soruları için.",
            DateRange, ["from_date", "to_date"]),
        Tool("adjustments",
            "Tarih aralığındaki iptal veya ikramlar tek tek: saat, masa, ürün, adet, tutar, yapan kişi.",
            Schema(new
            {
                from_date = new { type = "string", description = "Başlangıç iş günü, YYYY-MM-DD" },
                to_date = new { type = "string", description = "Bitiş iş günü (dahil), YYYY-MM-DD" },
                kind = new { type = "string", @enum = new[] { "void", "comp" }, description = "void = iptal, comp = ikram" },
            }),
            ["from_date", "to_date", "kind"]),
    ];

    /// <summary>Claude'un istediği aracı çalıştırır. Hatalı girdi ArgumentException fırlatır; Claude'a hata olarak döner.</summary>
    public static async Task<object> RunAsync(SalesQueries queries, Guid branchId, string name, JsonElement input, CancellationToken cancellationToken)
    {
        var from = Date(input, "from_date");
        var to = Date(input, "to_date");
        return name switch
        {
            "sales_summary" => await queries.SummaryAsync(branchId, from, to, cancellationToken),
            "top_products" => await queries.TopProductsAsync(branchId, from, to,
                input.TryGetProperty("limit", out var limit) ? limit.GetInt32() : 10,
                input.TryGetProperty("order_by", out var order) && order.GetString() == "revenue", cancellationToken),
            "sales_by_hour" => await queries.ByHourAsync(branchId, from, to, cancellationToken),
            "sales_by_staff" => await queries.ByStaffAsync(branchId, from, to, cancellationToken),
            "adjustments" => await queries.AdjustmentsAsync(branchId, from, to,
                input.GetProperty("kind").GetString() != "comp", cancellationToken),
            _ => throw new ArgumentException($"Bilinmeyen araç: {name}"),
        };
    }

    private static DateOnly Date(JsonElement input, string field) =>
        DateOnly.TryParseExact(input.GetProperty(field).GetString(), "yyyy-MM-dd", out var date)
            ? date
            : throw new ArgumentException($"{field} YYYY-MM-DD biçiminde olmalı.");

    private static BetaToolUnion Tool(string name, string description, JsonElement properties, string[] required) =>
        new BetaTool
        {
            Name = name,
            Description = description,
            // strict: Claude'un gönderdiği girdi şemaya birebir uyar (eksik/fazla alan olmaz).
            Strict = true,
            // Şemayı ham JSON olarak veriyoruz: "strict" için gereken additionalProperties=false alanı
            // SDK'nın InputSchema sınıfında ayrı bir özellik olarak yok.
            InputSchema = InputSchema.FromRawUnchecked(new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("object"),
                ["properties"] = properties,
                ["required"] = JsonSerializer.SerializeToElement(required),
                ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            }),
        };

    private static JsonElement Schema(object properties) => JsonSerializer.SerializeToElement(properties);
}
