using Microsoft.AspNetCore.SignalR;

namespace Adisyon.Api.Realtime;

/// <summary>
/// Controller'ların şube ekranlarına "masalar değişti" haberi vermesi için yardımcı.
/// Bildirim "en iyi çaba" ile gider: gönderilemezse işlem yine başarılıdır, çünkü ekranlar
/// yeniden bağlandıklarında tüm durumu sunucudan tekrar çeker.
/// </summary>
public class BranchNotifier(IHubContext<BranchHub> hub, ILogger<BranchNotifier> logger)
{
    public async Task TablesChangedAsync(Guid branchId)
    {
        try
        {
            await hub.Clients.Group(BranchHub.GroupName(branchId)).SendAsync(RealtimeEvents.TablesChanged);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Masa değişikliği bildirimi gönderilemedi (şube {BranchId})", branchId);
        }
    }
}
