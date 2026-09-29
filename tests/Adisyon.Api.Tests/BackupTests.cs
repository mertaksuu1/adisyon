using System.Net;
using System.Net.Http.Json;
using Adisyon.Api.Controllers;
using Adisyon.Api.Domain;
using Adisyon.Api.Hosting;
using Microsoft.Data.Sqlite;

namespace Adisyon.Api.Tests;

[Collection(nameof(ApiCollection))]
public class BackupTests(ApiFactory factory)
{
    [Fact]
    public async Task Backup_is_a_complete_openable_copy_of_the_database()
    {
        var (owner, tenantId) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);

        var response = await owner.PostAsync("/api/system/backups", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var backup = (await response.Content.ReadFromJsonAsync<BackupInfo>(ApiFactory.JsonOptions))!;
        var path = Path.Combine(factory.BackupDirectory, backup.FileName);
        Assert.True(File.Exists(path));

        // Yedek dosyası açılabilmeli ve az önce oluşturulan restoranı içermeli.
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tenants WHERE id = $id";
        command.Parameters.AddWithValue("$id", tenantId.ToString().ToUpperInvariant());
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);

        var list = await owner.GetFromJsonAsync<List<BackupInfo>>("/api/system/backups", ApiFactory.JsonOptions);
        Assert.Contains(list!, b => b.FileName == backup.FileName);
    }

    [Fact]
    public async Task Only_the_newest_thirty_backups_are_kept()
    {
        var (owner, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);
        Directory.CreateDirectory(factory.BackupDirectory);
        for (var day = 1; day <= 35; day++)
        {
            await File.WriteAllTextAsync(Path.Combine(factory.BackupDirectory, $"adisyon-2020-01-{day:00}_040000.db"), "eski");
        }

        var backup = await (await owner.PostAsync("/api/system/backups", null)).Content.ReadFromJsonAsync<BackupInfo>(ApiFactory.JsonOptions);

        var files = Directory.GetFiles(factory.BackupDirectory, "adisyon-*.db").Select(Path.GetFileName).ToList();
        Assert.Equal(BackupService.KeepCount, files.Count);
        Assert.Contains(backup!.FileName, files);
        Assert.DoesNotContain("adisyon-2020-01-01_040000.db", files); // en eskiler silindi
    }

    [Fact]
    public async Task Waiter_cannot_take_backups_or_see_system_info()
    {
        var (waiter, _) = await factory.CreateTenantAndLoginAsync(UserRole.Waiter);

        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsync("/api/system/backups", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync("/api/system/info")).StatusCode);
    }

    [Fact]
    public async Task System_info_shows_the_last_backup()
    {
        var (owner, _) = await factory.CreateTenantAndLoginAsync(UserRole.Owner);
        var backup = await (await owner.PostAsync("/api/system/backups", null)).Content.ReadFromJsonAsync<BackupInfo>(ApiFactory.JsonOptions);

        var info = await owner.GetFromJsonAsync<SystemInfo>("/api/system/info", ApiFactory.JsonOptions);

        Assert.NotNull(info!.Addresses);
        Assert.Equal(backup!.FileName, info.LastBackup!.FileName);
    }
}
