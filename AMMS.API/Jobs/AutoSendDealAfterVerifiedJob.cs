using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AMMS.API.Jobs
{
    public sealed class AutoSendDealAfterVerifiedJob
    {
        private readonly AppDbContext _db;
        private readonly IDealService _dealService;
        private readonly ILogger<AutoSendDealAfterVerifiedJob> _logger;

        public AutoSendDealAfterVerifiedJob(
            AppDbContext db,
            IDealService dealService,
            ILogger<AutoSendDealAfterVerifiedJob> logger)
        {
            _db = db;
            _dealService = dealService;
            _logger = logger;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
        public async Task RunAsync(CancellationToken ct)
        {
            var now = AppTime.NowVnUnspecified();
            var verifiedBefore = now.AddHours(-24);

            var candidates = await _db.order_requests
                .AsNoTracking()
                .Where(req =>
                    req.process_status == "Verified" &&
                    req.verified_at != null &&
                    req.verified_at <= verifiedBefore &&
                    req.quote_expires_at != null &&
                    req.quote_expires_at > now &&
                    req.customer_email != null &&
                    req.customer_email != "" &&
                    _db.cost_estimates.Any(est =>
                        est.order_request_id == req.order_request_id &&
                        est.is_active))
                .OrderBy(req => req.verified_at)
                .Select(req => new AutoSendCandidate
                {
                    RequestId = req.order_request_id,
                    VerifiedAt = req.verified_at!.Value,
                    QuoteExpiresAt = req.quote_expires_at!.Value,
                    CustomerEmail = req.customer_email!
                })
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                _logger.LogInformation(
                    "[AutoSendDealAfterVerifiedJob] No eligible verified requests older than 24h");
                return;
            }

            _logger.LogInformation(
                "[AutoSendDealAfterVerifiedJob] Found {Count} eligible requests for auto send-deal",
                candidates.Count);

            foreach (var item in candidates)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    await _dealService.SendDealAndEmailAsync(item.RequestId);

                    _logger.LogInformation(
                        "[AutoSendDealAfterVerifiedJob] Auto send-deal success. RequestId={RequestId}; VerifiedAt={VerifiedAt}; QuoteExpiresAt={QuoteExpiresAt}; CustomerEmail={CustomerEmail}",
                        item.RequestId,
                        item.VerifiedAt,
                        item.QuoteExpiresAt,
                        item.CustomerEmail);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "[AutoSendDealAfterVerifiedJob] Skip RequestId={RequestId}. State changed or already sent.",
                        item.RequestId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "[AutoSendDealAfterVerifiedJob] Auto send-deal failed. RequestId={RequestId}",
                        item.RequestId);
                }
            }

            _logger.LogInformation(
                "[AutoSendDealAfterVerifiedJob] Finished processing {Count} request(s)",
                candidates.Count);
        }

        private sealed class AutoSendCandidate
        {
            public int RequestId { get; set; }
            public DateTime VerifiedAt { get; set; }
            public DateTime QuoteExpiresAt { get; set; }
            public string CustomerEmail { get; set; } = string.Empty;
        }
    }
}