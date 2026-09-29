using System.Net.Sockets;
using System.Text;

namespace Adisyon.Api.Printing;

/// <summary>
/// ESC/POS: termal fiş yazıcılarının ortak dili. Düz metnin arasına kısa komut baytları konur:
///   ESC @      yazıcıyı sıfırla
///   ESC t n    karakter tablosunu seç (Türkçe PC857 çoğu yazıcıda n = 13)
///   GS ! n     yazı boyutu (0x01 = iki kat yükseklik; genişlik aynı kalır, 48 karakter sığmaya devam eder)
///   ESC d n    n satır boş kağıt ilerlet (kesmeden önce metin kesiciden çıksın)
///   GS V B 0   kağıdı kes (kısmi kesim)
/// </summary>
public static class EscPos
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;

    static EscPos()
    {
        // .NET varsayılan olarak eski DOS kod sayfalarını (857 gibi) içermez; açıkça etkinleştiriyoruz.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Türkçe DOS kod sayfası (PC857): ş=0x9F, ğ=0xA7, ı=0x8D, İ=0x98... Tabloda olmayan karakterler
    /// (ör. ₺ işareti) "?" olur; fişlerimizde bu yüzden ₺ kullanılmıyor.
    /// </summary>
    public static Encoding Turkish => Encoding.GetEncoding(857, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);

    public static byte[] Encode(PrintJob job, int codePageNumber)
    {
        var bytes = new List<byte>();
        bytes.AddRange([Esc, (byte)'@']);                       // sıfırla
        bytes.AddRange([Esc, (byte)'t', (byte)codePageNumber]); // Türkçe karakter tablosu

        // Mutfak fişleri iki kat yüksek: aşçı uzaktan okuyabilsin.
        var tall = job.Kind == TicketKind.Kitchen;
        if (tall)
        {
            bytes.AddRange([Gs, (byte)'!', 0x01]);
        }

        var text = job.Text.Replace("\r\n", "\n");
        bytes.AddRange(Turkish.GetBytes(text));

        if (tall)
        {
            bytes.AddRange([Gs, (byte)'!', 0x00]);
        }
        bytes.AddRange([Esc, (byte)'d', 4]);         // 4 satır ilerlet
        bytes.AddRange([Gs, (byte)'V', (byte)'B', 0]); // kes
        return bytes.ToArray();
    }
}

/// <summary>Yazıcıya ulaşılamadı: kapalı, kağıt/ağ sorunu veya yanlış adres. Mesaj kullanıcıya gösterilir.</summary>
public class PrinterUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Ağ yazıcısına ham bayt gönderir (TCP, varsayılan port 9100 — neredeyse tüm ağ fiş yazıcılarının kullandığı
/// "RAW" portu). Aynı yazıcıya aynı anda tek bağlantı açılır; yazıcılar paralel bağlantıda fişleri karıştırabilir.
/// </summary>
public class NetworkPrinterClient
{
    public const int DefaultPort = 9100;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);
    private readonly Dictionary<string, SemaphoreSlim> _locks = [];

    public async Task SendAsync(string address, byte[] data, CancellationToken cancellationToken)
    {
        var (host, port) = Parse(address);
        SemaphoreSlim gate;
        lock (_locks)
        {
            gate = _locks.TryGetValue(address, out var existing) ? existing : _locks[address] = new SemaphoreSlim(1, 1);
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(data, timeout.Token);
            await stream.FlushAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new PrinterUnavailableException($"Yazıcıya ulaşılamadı ({address}). Açık ve ağa bağlı mı?", ex);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>"192.168.1.50" veya "192.168.1.50:9100".</summary>
    public static (string Host, int Port) Parse(string address)
    {
        var trimmed = address.Trim();
        var colon = trimmed.LastIndexOf(':');
        if (colon > 0 && int.TryParse(trimmed[(colon + 1)..], out var port) && port is > 0 and < 65536)
        {
            return (trimmed[..colon], port);
        }
        return (trimmed, DefaultPort);
    }
}
