using Adisyon.Api.Controllers;
using Microsoft.AspNetCore.SignalR;

namespace Adisyon.Api.Realtime;

/// <summary>
/// Controller'ların şube ekranlarına haber vermek için kullandığı yardımcı.
/// Bildirimler "en iyi çaba" ile gider: gönderilemezse işlem yine başarılıdır, çünkü ekranlar
/// yeniden bağlandıklarında tüm durumu sunucudan tekrar çeker.
/// </summary>
public class BranchNotifier(IHubContext<BranchHub> hub, ILogger<BranchNotifier> logger)
{
    public Task OrderCreatedAsync(Guid branchId, KitchenOrderDto order) =>
        SendAsync(branchId, RealtimeEvents.OrderCreated, order);

    public Task OrderUpdatedAsync(Guid branchId, KitchenOrderDto order) =>
        SendAsync(branchId, RealtimeEvents.OrderUpdated, order);

    public Task TablesChangedAsync(Guid branchId) =>
        SendAsync(branchId, RealtimeEvents.TablesChanged, null);

    private async Task SendAsync(Guid branchId, string eventName, object? payload)
    {
        try
        {
            var group = hub.Clients.Group(BranchHub.GroupName(branchId));
            await (payload is null ? group.SendAsync(eventName) : group.SendAsync(eventName, payload));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Event} bildirimi gönderilemedi (şube {BranchId})", eventName, branchId);
        }
    }
}
