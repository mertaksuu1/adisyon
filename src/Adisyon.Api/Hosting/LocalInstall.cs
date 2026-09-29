using System.Security.Cryptography;
using System.Text.Json;

namespace Adisyon.Api.Hosting;

/// <summary>
/// Restoran bilgisayarına kurulan sürümün ayarları. Geliştirme ve test ortamında devreye girmez.
///
/// Veri klasörü (varsayılan: programın yanındaki "data" klasörü):
///   secrets.json            → ilk açılışta otomatik üretilen gizli anahtarlar. KAYBOLURSA TÜM PIN'LER GEÇERSİZ OLUR;
///                             veritabanıyla birlikte yedeklenmeli.
///   appsettings.Local.json  → kurulumda yazılan ayarlar (ör. veritabanı bağlantısı, port). İsteğe bağlı.
/// </summary>
public static class LocalInstall
{
    public static string DataDirectory(IConfiguration configuration) =>
        configuration["Adisyon:DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data");

    public static void AddLocalConfiguration(WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            return;
        }

        var directory = DataDirectory(builder.Configuration);
        Directory.CreateDirectory(directory);

        var secretsPath = Path.Combine(directory, "secrets.json");
        if (!File.Exists(secretsPath))
        {
            // Her kurulumun kendine özel, tahmin edilemez anahtarları olur. Bir kez üretilir, sonra hiç değişmez.
            var secrets = new
            {
                Jwt = new { SigningKey = NewKey() },
                Pin = new { HashKey = NewKey() },
            };
            File.WriteAllText(secretsPath, JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true }));
        }

        builder.Configuration.AddJsonFile(secretsPath, optional: false, reloadOnChange: false);
        builder.Configuration.AddJsonFile(Path.Combine(directory, "appsettings.Local.json"), optional: true, reloadOnChange: false);
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}
