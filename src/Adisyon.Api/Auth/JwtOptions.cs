namespace Adisyon.Api.Auth;

/// <summary>appsettings içindeki "Jwt" bölümü.</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "adisyon";
    public string Audience { get; set; } = "adisyon";

    /// <summary>
    /// Token'ları imzalayan gizli anahtar, en az 32 karakter. Bunu bilen herkes sahte token
    /// üretebilir; bu yüzden üretimde koda veya git'e değil, ortam değişkenine konur.
    /// </summary>
    public string SigningKey { get; set; } = "";

    /// <summary>Token ne kadar geçerli. Bir vardiyayı kapsayacak kadar uzun.</summary>
    public int LifetimeHours { get; set; } = 12;
}
