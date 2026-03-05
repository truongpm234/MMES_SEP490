using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Interfaces
{
    public interface IRequestRealtimeNotifier
    {
        Task RequestStatusChangedAsync(int requestId, string? oldStatus, string? newStatus, DateTime changedAt);
        Task RequestUpdatedAsync(int requestId, DateTime changedAt);
    }
}
