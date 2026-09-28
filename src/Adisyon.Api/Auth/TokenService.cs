using System.Security.Claims;
using System.Text;
using Adisyon.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Adisyon.Api.Auth;

/// <summary>Giriş yapan kullanıcı için imzalı JWT üretir.</summary>
public class TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    /// <summary>Token içindeki alan adları. Hem üretirken hem okurken aynı adlar kullanılmalı.</summary>
    public static class ClaimNames
    {
        public const string UserId = "sub";
        public const string TenantId = "tenant_id";
        public const string BranchId = "branch_id";
        public const string DeviceId = "device_id";
        public const string Role = "role";
        public const string Name = "name";
    }

    /// <summary>
    /// Kullanıcının o cihazdaki oturumu için token. Çalışılan şube, cihazın bağlı olduğu şubedir
    /// (sahip farklı şubelerdeki cihazlardan girebilir).
    /// </summary>
    public (string Token, DateTimeOffset ExpiresAt) CreateToken(User user, Device device)
    {
        var jwt = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddHours(jwt.LifetimeHours);

        var claims = new List<Claim>
        {
            new(ClaimNames.UserId, user.Id.ToString()),
            new(ClaimNames.TenantId, user.TenantId.ToString()),
            new(ClaimNames.BranchId, device.BranchId.ToString()),
            new(ClaimNames.DeviceId, device.Id.ToString()),
            new(ClaimNames.Role, user.Role.ToString()),
            new(ClaimNames.Name, user.DisplayName),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateSigningKey(jwt.SigningKey), SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }

    public static SymmetricSecurityKey CreateSigningKey(string signingKey)
    {
        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey en az 32 karakter olmalı.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }
}
