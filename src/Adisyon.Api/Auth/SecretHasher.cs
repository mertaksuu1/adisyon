using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Adisyon.Api.Auth;

/// <summary>appsettings içindeki "Pin" bölümü.</summary>
public class PinOptions
{
    public const string SectionName = "Pin";

    /// <summary>PIN özetlerinde kullanılan gizli anahtar, en az 32 karakter. Üretimde ortam değişkeninden gelir.</summary>
    public string HashKey { get; set; } = "";
}

/// <summary>PIN, cihaz anahtarı ve eşleştirme kodu gibi gizli değerleri üretir ve özetler.</summary>
public class SecretHasher(IOptions<PinOptions> options)
{
    /// <summary>
    /// PIN için HMAC-SHA256. Neden şifre hash'i (PBKDF2) değil?
    /// - Girişte PIN'den kişiyi bulmamız gerekiyor; HMAC aynı PIN için hep aynı sonucu verir,
    ///   böylece veritabanında indeksle arayabiliyoruz.
    /// - 4 haneli PIN'de yalnızca 10.000 olasılık var; yavaş hash bile bunu korumaz. Asıl koruma
    ///   gizli anahtar (veritabanı çalınsa bile anahtar olmadan çözülemez), eşleştirilmiş cihaz
    ///   zorunluluğu ve deneme kilidi.
    /// TenantId de karıştırılıyor ki iki restorandaki aynı PIN farklı özet versin.
    /// </summary>
    public string HashPin(Guid tenantId, string pin)
    {
        var key = options.Value.HashKey;
        if (Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Pin:HashKey en az 32 karakter olmalı.");
        }

        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes($"{tenantId}:{pin}"));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Cihaz anahtarı ve eşleştirme kodu için düz SHA-256. Bunlar zaten uzun ve rastgele
    /// olduğu için tahmin edilemezler; ek gizli anahtara gerek yok.
    /// </summary>
    public static string HashDeviceToken(string token) => Sha256(token);

    /// <summary>Kod küçük harfle veya tiresiz girilse de aynı özeti versin diye önce normalleştirilir.</summary>
    public static string HashPairingCode(string code) =>
        Sha256(code.Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant());

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Cihazın saklayacağı 256 bitlik rastgele anahtar.</summary>
    public static string NewDeviceToken() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// İnsanın klavyeden girebileceği eşleştirme kodu, ör. "K7M3-9QPX-TR4H".
    /// Karışan karakterler (0/O, 1/I/L) alfabede yok. 12 karakter ≈ 60 bit: tahmin edilemez.
    /// </summary>
    public static string NewPairingCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        var code = new string(chars);
        return $"{code[..4]}-{code[4..8]}-{code[8..]}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
