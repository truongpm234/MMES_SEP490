using AMMS.Shared.DTOs.Socket;
using Microsoft.AspNetCore.SignalR;

namespace AMMS.Application.Helpers
{
    //[Authorize]
    public class RealtimeHub : Hub
    {
        public Task JoinRequestsAll()
            => Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.RequestsAll);

        public Task LeaveRequestsAll()
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.RequestsAll);

        public Task JoinByRole(string role)
            => Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.ByRole(role));

        public Task LeaveRequestsByRole(string role)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.ByRole(role));

        public Task JoinRequest(int requestId)
            => Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Request(requestId));

        public Task LeaveRequest(int requestId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Request(requestId));

        public Task JoinProd(int prodId)
            => Groups.AddToGroupAsync(Context.ConnectionId, $"prod-{prodId}");

        public Task LeaveProd(int prodId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"prod-{prodId}");

        public Task JoinOrder(int orderId)
            => Groups.AddToGroupAsync(Context.ConnectionId, $"order-{orderId}");

        public Task LeaveOrder(int orderId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"order-{orderId}");
    }
}
