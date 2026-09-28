namespace Adisyon.Api.Auth;

/// <summary>
/// IP başına dakikalık istek sınırları. Restorandaki tüm cihazlar genelde aynı IP'yi paylaşır;
/// sınırlar vardiya başında birçok personelin aynı anda girişine yetecek şekilde seçildi.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Cihaz eşleştirme: kod tahminine karşı sıkı sınır.</summary>
    public const string Pairing = "pairing";

    /// <summary>PIN girişi: daha gevşek; PIN tahminine karşı asıl koruma cihaz başına deneme kilidi.</summary>
    public const string PinLogin = "pin-login";
}
