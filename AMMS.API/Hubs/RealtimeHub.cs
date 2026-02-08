using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AMMS.API.Hubs
{
    [Authorize]
    public class RealtimeHub : Hub
    {
        public Task JoinProd(int prodId)
            => Groups.AddToGroupAsync(Context.ConnectionId, $"prod:{prodId}");

        public Task LeaveProd(int prodId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"prod:{prodId}");

        public Task JoinOrder(int orderId)
            => Groups.AddToGroupAsync(Context.ConnectionId, $"order:{orderId}");

        public Task LeaveOrder(int orderId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"order:{orderId}");
    }
}
