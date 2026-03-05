using AMMS.Application.Helpers;
using AMMS.Shared.DTOs.Socket;
using Microsoft.AspNetCore.SignalR;

namespace AMMS.Application.Services
{
    public interface IRealtimePublisher
    {
        Task PublishRequestChangedAsync(RequestChangedEvent evt, CancellationToken ct = default);
        Task PublishRequestNoteChangedAsync(RequestNoteChangedEvent evt, CancellationToken ct = default);
    }

    public class RealtimePublisher : IRealtimePublisher
    {
        private readonly IHubContext<RealtimeHub> _hub;

        public RealtimePublisher(IHubContext<RealtimeHub> hub)
        {
            _hub = hub;
        }

        public async Task PublishRequestChangedAsync(RequestChangedEvent evt, CancellationToken ct = default)
        {
            await _hub.Clients.Group(RealtimeGroups.RequestsAll)
                .SendAsync("request.changed", evt, ct);

            await _hub.Clients.Group(RealtimeGroups.RequestsByRole("manager"))
                .SendAsync("request.changed", evt, ct);

            await _hub.Clients.Group(RealtimeGroups.RequestsByRole("consultant"))
                .SendAsync("request.changed", evt, ct);

            await _hub.Clients.Group(RealtimeGroups.Request(evt.request_id))
                .SendAsync("request.changed", evt, ct);
        }

        public async Task PublishRequestNoteChangedAsync(RequestNoteChangedEvent evt, CancellationToken ct = default)
        {
            await _hub.Clients.Group(RealtimeGroups.RequestsAll)
                .SendAsync("request.noteChanged", evt, ct);

            await _hub.Clients.Group(RealtimeGroups.Request(evt.request_id))
                .SendAsync("request.noteChanged", evt, ct);
        }
    }
}