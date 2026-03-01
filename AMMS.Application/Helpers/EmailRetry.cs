using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
namespace AMMS.Application.Helpers
{
    public static class EmailRetry
    {
        public static AsyncRetryPolicy CreatePolicy()
        {
            var rng = Random.Shared;

            return Policy
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>() // timeout
                .Or<Exception>(ex => IsTransientUnosend(ex))
                .WaitAndRetryAsync(
                    retryCount: 6,
                    sleepDurationProvider: attempt =>
                    {
                        var baseDelay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                        if (baseDelay > TimeSpan.FromSeconds(16)) baseDelay = TimeSpan.FromSeconds(16);

                        // jitter 0..250ms
                        var jitter = TimeSpan.FromMilliseconds(rng.Next(0, 250));
                        return baseDelay + jitter;
                    },
                    onRetryAsync: (ex, delay, attempt, ctx) =>
                    {
                        return Task.CompletedTask;
                    });
        }

        private static bool IsTransientUnosend(Exception ex)
        {
            var msg = (ex.Message ?? "").ToLowerInvariant();

            // retry transient
            if (msg.Contains("503") || msg.Contains("service temporarily unavailable")) return true;
            if (msg.Contains("502") || msg.Contains("bad gateway")) return true;
            if (msg.Contains("504") || msg.Contains("gateway timeout")) return true;
            if (msg.Contains("429") || msg.Contains("too many requests")) return true;

            return false;
        }
    }
}
