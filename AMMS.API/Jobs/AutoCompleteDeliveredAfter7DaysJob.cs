using AMMS.Application.Helpers;
using AMMS.Infrastructure.DBContext;
using AMMS.Shared.Helpers;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AMMS.API.Jobs
{
    public sealed class AutoCompleteDeliveredAfter7DaysJob
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AutoCompleteDeliveredAfter7DaysJob> _logger;

        public AutoCompleteDeliveredAfter7DaysJob(
            AppDbContext db,
            ILogger<AutoCompleteDeliveredAfter7DaysJob> logger)
        {
            _db = db;
            _logger = logger;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
        public async Task RunAsync(CancellationToken ct)
        {
            var now = AppTime.NowVnUnspecified();
            var cutoff = now.AddDays(-7);

            var candidates = await _db.orders
                .AsNoTracking()
                .Where(o =>
                    o.confirmed_delivery_at != null &&
                    o.confirmed_delivery_at <= cutoff &&
                    o.status == "Delivery")
                .OrderBy(o => o.confirmed_delivery_at)
                .Select(o => new
                {
                    o.order_id,
                    o.code,
                    o.confirmed_delivery_at
                })
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                _logger.LogInformation(
                    "[AutoCompleteDeliveredAfter7DaysJob] No delivery orders overdue 7 days at {Now}",
                    now);
                return;
            }

            _logger.LogInformation(
                "[AutoCompleteDeliveredAfter7DaysJob] Found {Count} overdue delivery orders to auto complete",
                candidates.Count);

            var successCount = 0;
            var skipCount = 0;
            var failCount = 0;

            foreach (var item in candidates)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    var strategy = _db.Database.CreateExecutionStrategy();

                    var updated = await strategy.ExecuteAsync(async () =>
                    {
                        await using var tx = await _db.Database.BeginTransactionAsync(ct);

                        var order = await _db.orders
                            .FirstOrDefaultAsync(x => x.order_id == item.order_id, ct);

                        if (order == null)
                        {
                            await tx.RollbackAsync(ct);
                            return false;
                        }

                        // kiểm tra lại lần cuối để tránh race condition
                        if (!string.Equals(order.status, "Delivery", StringComparison.OrdinalIgnoreCase))
                        {
                            await tx.RollbackAsync(ct);
                            return false;
                        }

                        if (!order.confirmed_delivery_at.HasValue || order.confirmed_delivery_at.Value > cutoff)
                        {
                            await tx.RollbackAsync(ct);
                            return false;
                        }

                        var request = await _db.order_requests
                            .FirstOrDefaultAsync(x => x.order_id == item.order_id, ct);

                        var productions = await _db.productions
                            .Where(x => x.order_id == item.order_id)
                            .ToListAsync(ct);

                        order.status = "Completed";

                        if (request != null &&
                            string.Equals(request.process_status, "Delivery", StringComparison.OrdinalIgnoreCase))
                        {
                            request.process_status = "Completed";
                        }

                        foreach (var prod in productions)
                        {
                            if (string.Equals(prod.status, "Delivery", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(prod.status, "Importing", StringComparison.OrdinalIgnoreCase))
                            {
                                prod.status = "Completed";
                            }

                            if (prod.end_date == null)
                                prod.end_date = now;
                        }

                        await _db.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);
                        return true;
                    });

                    if (updated)
                    {
                        successCount++;
                        _logger.LogInformation(
                            "[AutoCompleteDeliveredAfter7DaysJob] Auto completed order successfully. OrderId={OrderId}, Code={Code}, ConfirmedDeliveryAt={ConfirmedDeliveryAt}",
                            item.order_id,
                            item.code,
                            item.confirmed_delivery_at);
                    }
                    else
                    {
                        skipCount++;
                        _logger.LogInformation(
                            "[AutoCompleteDeliveredAfter7DaysJob] Skip order because state changed. OrderId={OrderId}, Code={Code}",
                            item.order_id,
                            item.code);
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logger.LogError(
                        ex,
                        "[AutoCompleteDeliveredAfter7DaysJob] Auto complete failed. OrderId={OrderId}, Code={Code}",
                        item.order_id,
                        item.code);
                }
            }

            _logger.LogInformation(
                "[AutoCompleteDeliveredAfter7DaysJob] Done. Success={SuccessCount}, Skipped={SkipCount}, Failed={FailCount}",
                successCount,
                skipCount,
                failCount);
        }
    }
}