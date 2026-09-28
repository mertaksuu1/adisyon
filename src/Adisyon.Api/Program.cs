using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TenantContext>();

// Bağlantı dizesi appsettings.Development.json içinde (testlerde test veritabanınınki kullanılır).
builder.Services.AddDbContext<AdisyonDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Adisyon")
        ?? throw new InvalidOperationException("ConnectionStrings:Adisyon ayarı eksik.");
    options.UseNpgsql(connectionString)
        // C#'taki "OrderItem" veritabanında "order_items" olur; PostgreSQL geleneği budur.
        .UseSnakeCaseNamingConvention();
});

// --- Kimlik doğrulama (JWT) ---
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<PinOptions>(builder.Configuration.GetSection(PinOptions.SectionName));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<SecretHasher>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
// JWT ayarlarını uygulama başladıktan sonra (ilk istekte) okuyoruz; böylece testler kendi ayarını verebiliyor.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        // Token'daki alan adlarını olduğu gibi kullan ("role" → "role"); .NET'in eski uzun adlarına çevirme.
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new()
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = TokenService.CreateSigningKey(jwt.SigningKey),
            NameClaimType = TokenService.ClaimNames.Name,
            RoleClaimType = TokenService.ClaimNames.Role,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Güvenli varsayılan: [AllowAnonymous] ile işaretlenmemiş her uç nokta giriş ister.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Pairing, httpContext => PerIpPerMinute(httpContext, "Pairing", 10));
    options.AddPolicy(RateLimitPolicies.PinLogin, httpContext => PerIpPerMinute(httpContext, "PinLogin", 60));

    // Sınır appsettings'ten değiştirilebilir (ör. RateLimiting:PinLoginPermitsPerMinute); testler bunu yükseltir.
    static RateLimitPartition<string> PerIpPerMinute(HttpContext httpContext, string name, int defaultLimit)
    {
        var limit = httpContext.RequestServices.GetRequiredService<IConfiguration>()
            .GetValue($"RateLimiting:{name}PermitsPerMinute", defaultLimit);
        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
    }
});

builder.Services.AddControllers()
    // Enum'lar JSON'da sayı yerine metin olarak gider: "role": "Waiter".
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    // API dokümantasyonu: http://localhost:5260/scalar
    app.MapScalarApiReference().AllowAnonymous();

    // Geliştirmede uygulama açılırken bekleyen migration'ları uygula ve demo veriyi yükle.
    // Üretimde bunu otomatik yapmayacağız; migration'lar kontrollü şekilde çalıştırılacak.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();
    await db.Database.MigrateAsync();
    await DevDataSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<SecretHasher>());
}

// HTTPS yönlendirmesi yok: üretimde TLS'i önündeki Caddy sunucusu yapacak.

app.UseRateLimiter();
app.UseAuthentication();

// Token'daki restoran bilgisini TenantContext'e yaz. Bundan sonraki tüm veritabanı sorguları
// otomatik olarak bu restoranla sınırlanır.
app.Use(async (context, next) =>
{
    var tenantClaim = context.User.FindFirst(TokenService.ClaimNames.TenantId)?.Value;
    if (Guid.TryParse(tenantClaim, out var tenantId))
    {
        context.RequestServices.GetRequiredService<TenantContext>().SetTenant(tenantId);
    }
    await next();
});

app.UseAuthorization();

app.MapControllers();

app.Run();
