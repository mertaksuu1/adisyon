using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Adisyon.Api.Ai;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Hosting;
using Adisyon.Api.Printing;
using Adisyon.Api.Realtime;
using Adisyon.Api.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Windows servisi olarak çalışırken çalışma klasörü C:\Windows\System32 olur; programın kendi
    // klasörünü (wwwroot, appsettings) bulabilmesi için kök klasörü açıkça veriyoruz.
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});

// Windows'ta servis olarak başlatıldıysa servis gibi davran (bilgisayar açılınca kendiliğinden başlar).
// Diğer durumlarda (Mac, geliştirme, testler) hiçbir etkisi yoktur.
builder.Host.UseWindowsService(options => options.ServiceName = "Adisyon");

// Restoran bilgisayarındaki kurulum: gizli anahtarlar ve yerel ayarlar "data" klasöründen (geliştirmede devre dışı).
LocalInstall.AddLocalConfiguration(builder);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TenantContext>();

// Bağlantı dizesi: geliştirmede appsettings.Development.json, kurulumda data/adisyon.db (LocalInstall),
// testlerde her test çalıştırması için geçici bir dosya.
builder.Services.AddDbContext<AdisyonDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Adisyon")
        ?? throw new InvalidOperationException("ConnectionStrings:Adisyon ayarı eksik.");
    // SQLite: veritabanı tek bir dosya (kurulumda data/adisyon.db). Ayrı veritabanı sunucusu gerekmez.
    options.UseSqlite(connectionString)
        // C#'taki "OrderItem" veritabanında "order_items" olur.
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
        // Tarayıcılar WebSocket bağlantısında Authorization başlığı gönderemez; SignalR token'ı
        // adrese "?access_token=..." olarak ekler. Bunu yalnızca hub adreslerinde kabul ediyoruz.
        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },
            // İmza doğru olsa bile kartı her istekte veritabanıyla doğrula: kişi pasife alındıysa, cihazın
            // bağlantısı kesildiyse veya kayıt artık yoksa (ör. veritabanı yeniden kuruldu) oturum hemen geçersiz olur.
            // Yoksa eski kart 12 saat boyunca çalışmaya (ve boş ekranlar göstermeye) devam ederdi.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;
                var db = context.HttpContext.RequestServices.GetRequiredService<AdisyonDbContext>();
                var userId = principal.GetUserId();
                var deviceId = Guid.TryParse(principal.FindFirst(TokenService.ClaimNames.DeviceId)?.Value, out var d) ? d : Guid.Empty;

                var userActive = await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId && u.IsActive);
                var deviceActive = await db.Devices.IgnoreQueryFilters().AnyAsync(x => x.Id == deviceId && x.IsActive);
                if (!userActive || !deviceActive)
                {
                    context.Fail("Oturum artık geçerli değil.");
                }
            },
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

// Gerçek zamanlı bildirimler: birden çok ekran varsa masa planları anında güncellenir.
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<BranchNotifier>();

// Fişler (mutfak ve hesap): yazıcı gelene kadar sanal yazıcı (önizleme). Faz 4'te gerçek ESC/POS yazıcı eklenecek.
builder.Services.AddSingleton<PreviewPrinter>();
builder.Services.AddSingleton<IPrinter>(services => services.GetRequiredService<PreviewPrinter>());
builder.Services.AddOpenApi();

// Yapay zeka: gün sonu yorumu ve rapora soru sorma (API anahtarı ve internet gerekir; yoksa "kullanılamıyor" der).
builder.Services.AddScoped<SalesQueries>();
builder.Services.AddSingleton<AiAssistant>();

// Yedekler: her zaman elle alınabilir; otomatik gece yedeği yalnızca restoran kurulumunda çalışır.
builder.Services.AddSingleton<BackupService>();
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<NightlyBackupWorker>();
}

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    // Açılışta bekleyen veritabanı güncellemelerini (migration) uygula. Her restoranın kendi veritabanı
    // olduğu için program güncellendiğinde şema da kendiliğinden güncellenir.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();
    await db.Database.MigrateAsync();
    // WAL modu: biri yazarken diğerleri okumaya devam edebilir (kasa ödeme alırken tablet masa listesini görür).
    // Veritabanı dosyasına kalıcı olarak yazılır; her açılışta tekrar çalıştırmak zararsızdır.
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

    if (app.Environment.IsDevelopment())
    {
        // Yalnızca geliştirmede: demo restoran. Gerçek kurulumda restoran ilk kurulum sihirbazıyla oluşturulur.
        await DevDataSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<SecretHasher>());
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    // API dokümantasyonu: http://localhost:5260/scalar
    app.MapScalarApiReference().AllowAnonymous();
}
else
{
    // Kurulumda web ekranları da bu programdan sunulur (wwwroot = web uygulamasının derlenmiş hâli):
    // tek program, tek adres. Restorandaki diğer cihazlar http://<kasa-bilgisayarı>:5000 adresini açar.
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// HTTPS yok: sistem restoranın kendi yerel ağında çalışır.

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
app.MapHub<BranchHub>(BranchHub.Path);

if (!app.Environment.IsDevelopment())
{
    // /masa/5 gibi ekran adresleri sayfa yenilenince de açılsın: bilinmeyen adreslerde web uygulamasını döndür.
    // Bilinmeyen /api adresleri ise web sayfası değil, 404 dönmeli.
    app.Map("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();
    app.MapFallbackToFile("index.html").AllowAnonymous();
}

app.Run();
