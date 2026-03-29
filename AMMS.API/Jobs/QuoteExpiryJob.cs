using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Shared.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AMMS.API.Jobs
{
    public class QuoteExpiryJob
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;
        private readonly ILogger<QuoteExpiryJob> _logger;

        public QuoteExpiryJob(
            AppDbContext db,
            IConfiguration config,
            IEmailService emailService,
            ILogger<QuoteExpiryJob> logger)
        {
            _db = db;
            _config = config;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var now = AppTime.NowVnUnspecified();

            var consultantEmail = _config["Deal:ConsultantEmail"];
            if (string.IsNullOrWhiteSpace(consultantEmail))
                _logger.LogWarning("[QuoteExpiryJob] Missing Deal:ConsultantEmail in config");

            var reason = "Từ chối deal do báo giá quá hạn 7 ngày kể từ thời điểm manager duyệt";

            var expiredItems = await (
                from req in _db.order_requests
                where (req.process_status == "Verified" || req.process_status == "Waiting")
                   && req.verified_at != null
                   && req.quote_expires_at != null
                   && req.quote_expires_at < now
                   && !_db.payments.Any(p =>
                        p.order_request_id == req.order_request_id
                        && p.provider == "PAYOS"
                        && (p.status == "PAID" || p.status == "SUCCESS"))
                select new ExpiredItem
                {
                    RequestId = req.order_request_id,
                    VerifiedAt = req.verified_at!.Value,
                    ExpiredAt = req.quote_expires_at!.Value
                }
            ).ToListAsync(ct);

            if (expiredItems.Count == 0)
            {
                _logger.LogInformation("[QuoteExpiryJob] No expired verified requests");
                return;
            }

            _logger.LogInformation("[QuoteExpiryJob] Found {count} expired verified requests", expiredItems.Count);

            var requestIds = expiredItems
                .Select(x => x.RequestId)
                .Distinct()
                .ToList();

            foreach (var item in expiredItems)
            {
                var requestId = item.RequestId;

                var req = await _db.order_requests
                    .FirstOrDefaultAsync(x => x.order_request_id == requestId, ct);

                if (req == null)
                {
                    _logger.LogWarning("[QuoteExpiryJob] order_request not found id={id}", requestId);
                    continue;
                }

                EnsureTracked(req);

                // Chỉ reject nếu request vẫn còn Verified
                if (!string.Equals(req.process_status, "Verified", StringComparison.OrdinalIgnoreCase) && !string.Equals(req.process_status, "Waiting", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "[QuoteExpiryJob] Skip request_id={id} because process_status={status}",
                        requestId,
                        req.process_status);
                    continue;
                }

                req.process_status = "Rejected";
                req.reason = reason;
                req.accepted_estimate_id = null;
                MarkModified(req);

                // Reject các quote của request này nếu chưa Accepted
                var quotes = await _db.quotes
                    .Where(q => q.order_request_id == requestId
                             && q.status != "Accepted")
                    .ToListAsync(ct);

                foreach (var q in quotes)
                {
                    EnsureTracked(q);
                    q.status = "Rejected";
                    MarkModified(q);
                }

                // Cancel payment PENDING -> CANCELLED
                var pendingPayments = await _db.payments
                    .Where(p => p.order_request_id == requestId
                             && p.provider == "PAYOS"
                             && p.status == "PENDING")
                    .ToListAsync(ct);

                var updatedAt = AppTime.NowVnUnspecified();
                foreach (var p in pendingPayments)
                {
                    EnsureTracked(p);
                    p.status = "CANCELLED";
                    p.updated_at = updatedAt;
                    MarkModified(p);
                }

                if (!string.IsNullOrWhiteSpace(consultantEmail))
                {
                    try
                    {
                        var subject = "Từ chối deal do báo giá quá hạn 7 ngày";
                        var html = BuildConsultantExpiredEmailHtml(req, item, reason, now);
                        await _emailService.SendAsync(consultantEmail, subject, html);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "[QuoteExpiryJob] Failed to send consultant mail for request_id={id}",
                            requestId);
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("[QuoteExpiryJob] Done. Updated {count} requests", expiredItems.Count);
        }

        private void EnsureTracked<T>(T entity) where T : class
        {
            var entry = _db.Entry(entity);
            if (entry.State == EntityState.Detached)
                _db.Attach(entity);
        }

        private void MarkModified<T>(T entity) where T : class
        {
            _db.Entry(entity).State = EntityState.Modified;
        }

        private static string BuildConsultantExpiredEmailHtml(
            dynamic req,
            ExpiredItem expired,
            string reason,
            DateTime now)
        {
            return $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='font-family: Arial, sans-serif; background:#f1f5f9; padding: 24px;'>
  <div style='max-width:760px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 4px 16px rgba(0,0,0,0.08)'>
    <div style='background:linear-gradient(135deg,#ef4444,#b91c1c);padding:20px;color:#fff;text-align:center'>
      <div style='font-size:18px;font-weight:800'>TỪ CHỐI DEAL DO QUÁ HẠN</div>
      <div style='opacity:.9;font-size:13px;margin-top:6px'>Báo giá đã quá hạn 7 ngày</div>
    </div>

    <div style='padding:22px'>
      <div style='margin-bottom:14px'>
        <div><b>Request:</b> #AM{((int)req.order_request_id):D6}</div>
        <div><b>Thời điểm kiểm tra:</b> {now:dd/MM/yyyy HH:mm:ss}</div>
        <div><b>Lý do:</b> {System.Net.WebUtility.HtmlEncode(reason)}</div>
      </div>

      <hr style='border:none;border-top:1px solid #e2e8f0;margin:16px 0' />

      <div style='font-weight:800;margin-bottom:8px'>Thông tin Order Request</div>
      <div style='display:grid;grid-template-columns:1fr 1fr;gap:10px'>
        <div><b>Customer:</b> {System.Net.WebUtility.HtmlEncode((string)(req.customer_name ?? "N/A"))}</div>
        <div><b>Phone:</b> {System.Net.WebUtility.HtmlEncode((string)(req.customer_phone ?? "N/A"))}</div>
        <div><b>Email:</b> {System.Net.WebUtility.HtmlEncode((string)(req.customer_email ?? "N/A"))}</div>
        <div><b>Product type:</b> {System.Net.WebUtility.HtmlEncode((string)(req.product_type ?? "N/A"))}</div>
        <div><b>Process status:</b> {System.Net.WebUtility.HtmlEncode((string)(req.process_status ?? "N/A"))}</div>
        <div><b>Reason:</b> {System.Net.WebUtility.HtmlEncode((string)(req.reason ?? "N/A"))}</div>
        <div><b>Verified at:</b> {(((DateTime?)req.verified_at)?.ToString("dd/MM/yyyy HH:mm:ss") ?? "N/A")}</div>
        <div><b>Quote expired at:</b> {(((DateTime?)req.quote_expires_at)?.ToString("dd/MM/yyyy HH:mm:ss") ?? "N/A")}</div>
      </div>

      <div style='margin-top:18px;font-weight:800'>Thông tin hết hạn</div>
      <table style='width:100%;border-collapse:collapse;margin-top:10px;font-size:13px'>
        <thead>
          <tr style='background:#f8fafc'>
            <th style='padding:8px;border:1px solid #e2e8f0;text-align:left'>Request ID</th>
            <th style='padding:8px;border:1px solid #e2e8f0;text-align:left'>Verified At</th>
            <th style='padding:8px;border:1px solid #e2e8f0;text-align:left'>Expired At</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td style='padding:8px;border:1px solid #e2e8f0'>#{expired.RequestId}</td>
            <td style='padding:8px;border:1px solid #e2e8f0'>{expired.VerifiedAt:dd/MM/yyyy HH:mm:ss}</td>
            <td style='padding:8px;border:1px solid #e2e8f0'>{expired.ExpiredAt:dd/MM/yyyy HH:mm:ss}</td>
          </tr>
        </tbody>
      </table>

      <div style='margin-top:18px;padding:12px;background:#fff7ed;border:1px solid #fed7aa;border-radius:10px;color:#9a3412'>
        Ghi chú: Hệ thống đã ghi nhận trạng thái từ chối báo giá cho yêu cầu này.
      </div>
    </div>

    <div style='background:#f8fafc;padding:14px;text-align:center;color:#94a3b8;font-size:12px;border-top:1px solid #e2e8f0'>
      Email tự động từ hệ thống MES
    </div>
  </div>
</body>
</html>";
        }

        private class ExpiredItem
        {
            public int RequestId { get; set; }
            public DateTime VerifiedAt { get; set; }
            public DateTime ExpiredAt { get; set; }
        }
    }
}