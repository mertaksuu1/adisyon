using Adisyon.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Adisyon.Api.Realtime;

/// <summary>
/// Şube ekranlarının (garson, kasa, mutfak) bağlandığı gerçek zamanlı kanal.
/// Ekranlar buraya yalnızca dinlemek için bağlanır; tüm işlemler normal HTTP uç noktalarıyla yapılır.
/// Bağlanan her ekran, giriş yaptığı cihazın şubesine ait gruba eklenir.
/// </summary>
[Authorize]
public class BranchHub : Hub
{
    public const string Path = "/hubs/branch";

    public static string GroupName(Guid branchId) => $"branch:{branchId}";

    public override async Task OnConnectedAsync()
    {
        // Şube token'dan okunur; istemci başka bir şubenin grubuna katılmayı isteyemez.
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(Context.User!.GetBranchId()));
        await base.OnConnectedAsync();
    }
}

/// <summary>Ekranlara gönderilen olay adları. İstemci (web/src/realtime) aynı adları dinler.</summary>
public static class RealtimeEvents
{
    /// <summary>Mutfağa yeni sipariş düştü. Yük: KitchenOrderDto.</summary>
    public const string OrderCreated = "OrderCreated";

    /// <summary>Bir siparişin durumu değişti (hazırlanıyor, hazır, servis edildi). Yük: KitchenOrderDto.</summary>
    public const string OrderUpdated = "OrderUpdated";

    /// <summary>Masa planı değişti (masa açıldı/kapandı, tutar değişti). Yük yok; ekran listeyi yeniden çeker.</summary>
    public const string TablesChanged = "TablesChanged";
}
