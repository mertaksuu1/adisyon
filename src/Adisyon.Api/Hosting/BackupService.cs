using Microsoft.Data.Sqlite;

namespace Adisyon.Api.Hosting;

/// <summary>
/// Veritabanı yedekleri. Her gün 04:00'te ve program açılışında (son yedek 20 saatten eskiyse) otomatik alınır;
/// restoran bilgisayarı gece kapatılsa bile her gün en az bir yedek oluşur. Son 30 yedek saklanır.
///
/// Yedek klasörü varsayılan olarak veri klasöründeki "yedekler"; "Backup:Directory" ayarıyla başka bir diske
/// (ör. harici disk) yönlendirilebilir. Geri yüklemek için yedek dosya adisyon.db adıyla veri klasörüne konur;
/// secrets.json da yedek klasörüne kopyalanır (onsuz PIN'ler çalışmaz).
/// </summary>
public class BackupService(IConfiguration configuration, TimeProvider timeProvider, ILogger<BackupService> logger)
{
    public const int KeepCount = 30;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(20);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string BackupDirectory =>
        configuration["Backup:Directory"] ?? Path.Combine(LocalInstall.DataDirectory(configuration), "yedekler");

    public List<BackupInfo> List()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return [];
        }

        return new DirectoryInfo(BackupDirectory).GetFiles("adisyon-*.db")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupInfo(f.Name, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero), f.Length))
            .ToList();
    }

    public async Task<BackupInfo> BackupNowAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken); // aynı anda iki yedek alınmasın
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var now = timeProvider.GetUtcNow();
            var local = TimeZoneInfo.ConvertTime(now, Printing.TicketFormat.RestaurantTimeZone);
            var path = Path.Combine(BackupDirectory, $"adisyon-{local:yyyy-MM-dd_HHmmss}.db");

            // SQLite'ın çevrimiçi yedeği: program çalışırken bile tutarlı bir kopya alır
            // (dosyayı düz kopyalamak yarım yazılmış bir kopya üretebilir).
            await using (var source = new SqliteConnection(configuration.GetConnectionString("Adisyon")))
            await using (var destination = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await source.OpenAsync(cancellationToken);
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
            }

            // Gizli anahtarlar olmadan yedek işe yaramaz (PIN'ler çalışmaz); yedeğin yanına koy.
            var secrets = Path.Combine(LocalInstall.DataDirectory(configuration), "secrets.json");
            if (File.Exists(secrets))
            {
                File.Copy(secrets, Path.Combine(BackupDirectory, "secrets.json"), overwrite: true);
            }

            Prune();
            File.SetLastWriteTimeUtc(path, now.UtcDateTime);
            var info = new FileInfo(path);
            logger.LogInformation("Yedek alındı: {Path} ({Size} bayt)", path, info.Length);
            return new BackupInfo(info.Name, now, info.Length);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Son yedek eskiyse (veya hiç yoksa) yedek alır. Açılışta çağrılır.</summary>
    public async Task BackupIfStaleAsync(CancellationToken cancellationToken)
    {
        var latest = List().FirstOrDefault();
        if (latest is null || timeProvider.GetUtcNow() - latest.CreatedAt > MaxAge)
        {
            await BackupNowAsync(cancellationToken);
        }
    }

    /// <summary>En yeni 30 yedeği tutar, eskileri siler.</summary>
    private void Prune()
    {
        foreach (var old in new DirectoryInfo(BackupDirectory).GetFiles("adisyon-*.db")
                     .OrderByDescending(f => f.Name) // ad tarih içerdiği için ada göre sıralama = tarihe göre
                     .Skip(KeepCount))               // yeni alınan yedek de listede; en yeni 30 kalır
        {
            old.Delete();
        }
    }
}

public record BackupInfo(string FileName, DateTimeOffset CreatedAt, long SizeBytes);

/// <summary>Arka planda: açılışta eksikse yedek alır, sonra her gün 04:00'te (restoran saatiyle) yedek alır.</summary>
public class NightlyBackupWorker(BackupService backups, TimeProvider timeProvider, ILogger<NightlyBackupWorker> logger) : BackgroundService
{
    private const int BackupHour = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunSafelyAsync(() => backups.BackupIfStaleAsync(stoppingToken));

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(UntilNextBackup(), timeProvider, stoppingToken);
            await RunSafelyAsync(() => backups.BackupNowAsync(stoppingToken));
        }
    }

    private TimeSpan UntilNextBackup()
    {
        var zone = Printing.TicketFormat.RestaurantTimeZone;
        var now = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var next = new DateTimeOffset(now.Date.AddHours(BackupHour), now.Offset);
        if (next <= now)
        {
            next = next.AddDays(1);
        }
        return next - now;
    }

    /// <summary>Yedek hatası programı durdurmamalı; günlüğe yazılır, sonraki denemede tekrar denenir.</summary>
    private async Task RunSafelyAsync(Func<Task> backup)
    {
        try
        {
            await backup();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Otomatik yedek alınamadı");
        }
    }
}
