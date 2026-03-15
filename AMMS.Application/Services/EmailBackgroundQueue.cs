using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Background;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AMMS.Application.Services
{
    public sealed class EmailBackgroundQueue : IEmailBackgroundQueue
    {
        private readonly Channel<EmailQueueItem> _channel;

        public EmailBackgroundQueue()
        {
            var options = new BoundedChannelOptions(1000)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            };

            _channel = Channel.CreateBounded<EmailQueueItem>(options);
        }

        public ValueTask QueueAsync(EmailQueueItem item, CancellationToken ct = default)
            => _channel.Writer.WriteAsync(item, ct);

        public ValueTask<EmailQueueItem> DequeueAsync(CancellationToken ct = default)
            => _channel.Reader.ReadAsync(ct);
    }
}
