using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Adisyon.Api.Reports;

namespace Adisyon.Api.Ai;

/// <summary>
/// Claude ile konuşan tek yer: gün sonu yorumu ve rapora soru sorma.
///
/// API anahtarı "Anthropic:ApiKey" ayarından (kurulumda data/appsettings.Local.json) veya ANTHROPIC_API_KEY
/// ortam değişkeninden okunur. Anahtar yoksa ya da internet yoksa özellik "kullanılamıyor" der; satış ve kasa
/// hiçbir şekilde etkilenmez.
/// </summary>
public class AiAssistant(IConfiguration configuration, ILogger<AiAssistant> logger)
{
    private const string Model = "claude-opus-5-5";

    /// <summary>Bir soru için en fazla bu kadar araç turu; sonsuz döngüye girmesin.</summary>
    private const int MaxToolRounds = 8;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string? ApiKey =>
        configuration["Anthropic:ApiKey"] is { Length: > 0 } key ? key : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    private AnthropicClient CreateClient() => new() { ApiKey = ApiKey };

    // ------------------------------------------------------------------ Gün sonu yorumu

    private const string DaySummarySystem = """
        Sen bir restoranın işletme sahibine gün sonu raporunu yorumlayan yardımcısın.
        Sana JSON olarak üç Z raporu verilecek: "bugun", "dun" ve "gecen_hafta_ayni_gun".
        Tutarlar Türk lirasıdır.

        Türkçe, sade ve kısa yaz: en fazla 6 madde, her madde tek cümle. Restoran sahibi yoğun biri;
        yalnızca işine yarayacak şeyleri söyle:
        - Ciro ve adisyon sayısı dün ve geçen haftanın aynı gününe göre nasıl (yüzdeyle).
        - Nakit/kart dağılımında olağandışı bir şey varsa.
        - İptal veya ikramlar dikkat çekiyorsa: özellikle aynı kişide yoğunlaşıyorsa adıyla belirt.
        - Açık kalan masa varsa uyar.
        - En çok satanlarda dikkat çeken bir değişiklik varsa.
        Veride olmayan bir şeyi tahmin etme veya uydurma. Karşılaştırma günü boşsa (hiç satış yoksa)
        o karşılaştırmayı atla. Başlık, giriş cümlesi veya kapanış cümlesi yazma; yalnızca maddeleri yaz,
        her maddeyi "- " ile başlat.
        """;

    public async Task<AiResult> SummarizeDayAsync(ZReport today, ZReport yesterday, ZReport lastWeek, CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(new { bugun = today, dun = yesterday, gecen_hafta_ayni_gun = lastWeek }, Json);
        return await RunAsync(async client =>
        {
            var response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = 4000,
                // Basit bir özet: düşük çaba yeterli, daha ucuz ve hızlı.
                OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
                System = SystemPrompt(DaySummarySystem),
                Messages = [new() { Role = Role.User, Content = data }],
                Betas = [FallbackBeta],
                Fallbacks = Fallbacks,
            }, cancellationToken);
            return TextOf(response);
        }, cancellationToken);
    }

    // ------------------------------------------------------------------ Rapora soru sorma

    private const string AskSystem = """
        Sen bir restoranın satış verilerine erişen yardımcısın. İşletme sahibinin veya yöneticinin
        Türkçe sorularını, sana verilen rapor araçlarını kullanarak cevaplarsın.

        Kurallar:
        - Rakamları YALNIZCA araç sonuçlarından al; asla tahmin etme veya uydurma. Araçlarla cevaplanamayan
          bir soruysa (ör. stok, maliyet, kâr, müşteri bilgisi) bunu açıkça söyle.
        - Tarihler iş günüdür: gün sabah 05:00'te başlar. Araçlara tarihleri YYYY-MM-DD biçiminde ver.
          "Bu hafta" Pazartesi'den bugüne, "geçen ay" bir önceki takvim ayıdır.
        - Cevabı Türkçe, kısa ve doğrudan ver: önce cevap, sonra gerekirse 2-4 destekleyici rakam.
          Tutarları "1.250,50 ₺" biçiminde yaz. Gereksiz giriş cümlesi yazma.
        """;

    public async Task<AiResult> AskAsync(string question, DateOnly today, string restaurantName,
        Func<string, JsonElement, Task<object>> runTool, CancellationToken cancellationToken)
    {
        return await RunAsync(async client =>
        {
            List<BetaMessageParam> messages =
            [
                new()
                {
                    Role = Role.User,
                    // Değişen bilgiler (bugünün tarihi) sistem mesajında değil burada: sistem mesajı ve araç
                    // listesi hep aynı kalsın ki önbelleğe alınabilsin.
                    Content = $"Restoran: {restaurantName}\nBugünün iş günü: {today:yyyy-MM-dd} ({today.DayOfWeek})\n\nSoru: {question}",
                },
            ];

            for (var round = 0; round < MaxToolRounds; round++)
            {
                var response = await client.Beta.Messages.Create(new MessageCreateParams
                {
                    Model = Model,
                    MaxTokens = 8000,
                    OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                    System = SystemPrompt(AskSystem),
                    Tools = ReportTools.Definitions,
                    Messages = messages,
                    Betas = [FallbackBeta],
                    Fallbacks = Fallbacks,
                }, cancellationToken);

                if (response.StopReason != "tool_use")
                {
                    return TextOf(response);
                }

                // Claude'un cevabını olduğu gibi geri ekle (düşünme blokları imzasıyla birlikte), sonra
                // istediği her aracı çalıştırıp sonuçları TEK bir kullanıcı mesajında gönder.
                List<BetaContentBlockParam> assistant = [];
                List<BetaContentBlockParam> results = [];
                foreach (var block in response.Content)
                {
                    if (block.TryPickText(out var text))
                    {
                        assistant.Add(new BetaTextBlockParam { Text = text.Text });
                    }
                    else if (block.TryPickThinking(out var thinking))
                    {
                        assistant.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                    }
                    else if (block.TryPickRedactedThinking(out var redacted))
                    {
                        assistant.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                    }
                    else if (block.TryPickToolUse(out var toolUse))
                    {
                        assistant.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                        results.Add(await RunToolSafelyAsync(toolUse, runTool));
                    }
                }

                messages.Add(new() { Role = Role.Assistant, Content = assistant });
                messages.Add(new() { Role = Role.User, Content = results });
            }

            return "Bu soruyu cevaplamak için çok fazla adım gerekti. Soruyu daha dar sorabilir misiniz? (ör. belirli bir tarih aralığı)";
        }, cancellationToken);
    }

    private async Task<BetaContentBlockParam> RunToolSafelyAsync(BetaToolUseBlock toolUse, Func<string, JsonElement, Task<object>> runTool)
    {
        try
        {
            var input = JsonSerializer.SerializeToElement(toolUse.Input, Json);
            var result = await runTool(toolUse.Name, input);
            return new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = JsonSerializer.Serialize(result, Json) };
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            // Hatalı tarih vb.: Claude'a hatayı söyle, düzeltip tekrar denesin.
            return new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = $"Hata: {ex.Message}", IsError = true };
        }
    }

    // ------------------------------------------------------------------ Ortak

    /// <summary>İlk model isteği reddederse (nadir) aynı isteği sunucu tarafında yedek modelle cevaplat.</summary>
    private const string FallbackBeta = "server-side-fallback-2026-06-01";
    private const string FallbackModel = "claude-opus-4-8";
    private static BetaFallbacksParam Fallbacks => new List<BetaFallbackParam> { new() { Model = FallbackModel } };

    /// <summary>Sistem mesajı önbelleğe alınır: her soruda aynı olduğu için tekrar ücretlendirilmez.</summary>
    private static List<BetaTextBlockParam> SystemPrompt(string text) =>
        [new() { Text = text, CacheControl = new BetaCacheControlEphemeral() }];

    private static string TextOf(BetaMessage response)
    {
        if (response.StopReason == "refusal")
        {
            return "Yapay zeka bu isteği cevaplamadı. Soruyu farklı şekilde sormayı deneyin.";
        }
        return string.Join("\n", response.Content.Select(b => b.TryPickText(out var t) ? t.Text : null).Where(t => t is not null)).Trim();
    }

    /// <summary>Anahtar yoksa, internet yoksa veya Claude hata verirse kullanıcıya anlaşılır bir mesaj döner.</summary>
    private async Task<AiResult> RunAsync(Func<AnthropicClient, Task<string>> work, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return AiResult.Unavailable("Yapay zeka ayarlanmamış (API anahtarı yok).");
        }
        try
        {
            return AiResult.Ok(await work(CreateClient()));
        }
        catch (AnthropicRateLimitException)
        {
            return AiResult.Unavailable("Yapay zeka şu an çok yoğun. Birkaç dakika sonra tekrar deneyin.");
        }
        catch (AnthropicApiException ex)
        {
            logger.LogWarning(ex, "Claude API hatası");
            return AiResult.Unavailable("Yapay zekaya ulaşılamadı. İnternet bağlantısını ve API anahtarını kontrol edin.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Claude API'ye bağlanılamadı");
            return AiResult.Unavailable("İnternet bağlantısı yok; yapay zeka şu an kullanılamıyor.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiResult.Unavailable("Yapay zeka zamanında cevap vermedi. Tekrar deneyin.");
        }
    }
}

/// <summary>Available false ise Text kullanıcıya gösterilecek açıklamadır.</summary>
public record AiResult(bool Available, string Text)
{
    public static AiResult Ok(string text) => new(true, text);
    public static AiResult Unavailable(string reason) => new(false, reason);
}
