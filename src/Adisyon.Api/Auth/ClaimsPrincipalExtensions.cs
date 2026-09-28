using System.Security.Claims;

namespace Adisyon.Api.Auth;

/// <summary>
/// Giriş yapmış kullanıcının token'ından sık kullanılan bilgileri okur.
/// Controller içinde <c>User.GetBranchId()</c> şeklinde kullanılır.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirst(TokenService.ClaimNames.UserId)!.Value);

    /// <summary>Kullanıcının şu an çalıştığı şube: giriş yaptığı cihazın bağlı olduğu şube.</summary>
    public static Guid GetBranchId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirst(TokenService.ClaimNames.BranchId)!.Value);
}
