using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Adisyon.Api.Auth;
using Adisyon.Api.Controllers;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;

namespace Adisyon.Api.Tests;

/// <summary>
/// Testler için API'yi bellekte başlatır ve geçici bir SQLite veritabanı dosyası açar (Docker gerekmez).
/// Tüm test sınıfları aynı örneği paylaşır (ApiCollection), böylece konteyner bir kez açılır.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Bu test grubuna özel veritabanı dosyası; testler bitince silinir.</summary>
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"adisyon-test-{Guid.NewGuid():N}.db");

    /// <summary>Yedek testleri için geçici klasör; testler bitince silinir.</summary>
    public string BackupDirectory { get; } = Path.Combine(Path.GetTempPath(), $"adisyon-test-yedek-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" ortamında demo veri yükleyici çalışmaz; testler kendi verisini kurar.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Adisyon", $"Data Source={_databasePath}");
        builder.UseSetting("Backup:Directory", BackupDirectory);
        builder.UseSetting("Jwt:SigningKey", "test-ortami-icin-en-az-otuz-iki-karakterlik-anahtar");
        builder.UseSetting("Pin:HashKey", "test-ortami-icin-pin-ozet-anahtari-otuz-iki-karakter");
        // Tüm testler aynı "IP"den geldiği için istek sınırlarını testlerde yükseltiyoruz.
        builder.UseSetting("RateLimiting:PairingPermitsPerMinute", "1000");
        builder.UseSetting("RateLimiting:PinLoginPermitsPerMinute", "1000");
    }

    /// <summary>Test için kurulan restoran: kimliği, şubesi ve şubenin eşleştirme kodu.</summary>
    public record TestRestaurant(Guid TenantId, Guid BranchId, string PairingCode);

    /// <summary>Yeni bir restoran ve eşleştirme kodu olan bir şube oluşturur.</summary>
    public async Task<TestRestaurant> CreateRestaurantAsync()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var pairingCode = SecretHasher.NewPairingCode();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Test Restoran", Slug = $"t-{tenantId:N}", CreatedAt = DateTimeOffset.UtcNow });
        db.Branches.Add(new Branch
        {
            Id = branchId,
            TenantId = tenantId,
            Name = "Şube",
            PairingCodeHash = SecretHasher.HashPairingCode(pairingCode),
        });
        await db.SaveChangesAsync();
        return new TestRestaurant(tenantId, branchId, pairingCode);
    }

    /// <summary>Restorana doğrudan veritabanı üzerinden personel ekler.</summary>
    public async Task AddStaffAsync(Guid tenantId, UserRole role, string pin, Guid? branchId = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<SecretHasher>();
        db.Users.Add(new User
        {
            TenantId = tenantId,
            DisplayName = $"Test {role}",
            PinHash = hasher.HashPin(tenantId, pin),
            Role = role,
            BranchId = branchId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Gerçek akışla cihazı eşleştirir ve cihaz anahtarını döndürür.</summary>
    public async Task<string> PairDeviceAsync(string pairingCode)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/pair", new PairDeviceRequest(pairingCode, "Test cihazı"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PairDeviceResponse>(JsonOptions))!.DeviceToken;
    }

    /// <summary>Cihaz anahtarı başlığıyla PIN girişi dener.</summary>
    public Task<HttpResponseMessage> PinLoginAsync(string deviceToken, string pin)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(AuthController.DeviceTokenHeader, deviceToken);
        return client.PostAsJsonAsync("/api/auth/pin-login", new PinLoginRequest(pin));
    }

    /// <summary>
    /// Kısayol: yeni restoran + verilen rolde personel oluşturur, cihazı eşleştirip PIN'le girer ve
    /// Authorization başlığı ayarlanmış bir HttpClient döndürür.
    /// </summary>
    public async Task<(HttpClient Client, Guid TenantId)> CreateTenantAndLoginAsync(UserRole role)
    {
        var restaurant = await CreateRestaurantAsync();
        return (await LoginAsync(restaurant, role), restaurant.TenantId);
    }

    /// <summary>
    /// Var olan restorana verilen rolde yeni bir personel ekler, kendi cihazını eşleştirip PIN'le girer.
    /// Aynı restoranda birden çok personelle (ör. garson + kasa) test yazmak için.
    /// </summary>
    public async Task<HttpClient> LoginAsync(TestRestaurant restaurant, UserRole role)
    {
        var pin = Random.Shared.Next(0, 10_000).ToString("D4");
        while (!await TryAddStaffAsync(restaurant.TenantId, role, pin))
        {
            pin = Random.Shared.Next(0, 10_000).ToString("D4"); // Nadiren aynı PIN denk gelirse yenisini seç.
        }
        var deviceToken = await PairDeviceAsync(restaurant.PairingCode);

        var response = await PinLoginAsync(deviceToken, pin);
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    private async Task<bool> TryAddStaffAsync(Guid tenantId, UserRole role, string pin)
    {
        try
        {
            await AddStaffAsync(tenantId, role, pin);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    /// <summary>API enum'ları metin olarak gönderdiği için testler de aynı ayarla okumalı.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AdisyonDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools(); // dosya kilidini bırak, sonra sil
        if (Directory.Exists(BackupDirectory))
        {
            Directory.Delete(BackupDirectory, recursive: true);
        }
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_databasePath + suffix);
        }
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public class ApiCollection : ICollectionFixture<ApiFactory>;
