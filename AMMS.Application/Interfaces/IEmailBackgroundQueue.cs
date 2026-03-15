using AMMS.Shared.DTOs.Background;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Interfaces
{
    public interface IEmailBackgroundQueue
    {
        ValueTask QueueAsync(EmailQueueItem item, CancellationToken ct = default);
        ValueTask<EmailQueueItem> DequeueAsync(CancellationToken ct = default);
    }
}
