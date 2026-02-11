using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
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
            {
                _logger.LogWarning("[QuoteExpiryJob] Missing Deal:ConsultantEmail in config");
            }

            var reason = "Từ chối deal do quá hạn 24h";

            var expiredRows = await (
    from q in _db.quotes.AsTracking()
    join req in _db.order_requests.AsTracking()
        on q.order_request_id equals req.order_request_id
    join est in _db.cost_estimates.AsNoTracking()
        on q.estimate_id equals est.estimate_id
    where q.status == "Sent"
       && req.process_status == "Waiting"
       && est.created_at.AddHours(24) < now
       && !_db.payments.AsNoTracking().Any(p =>
              p.order_request_id == req.order_request_id
              && p.provider == "PAYOS"
              && (p.status == "PAID" || p.status == "SUCCESS"))
    select new { q, req, est }
).ToListAsync(ct);

            if (expiredRows.Count == 0)
            {
                _logger.LogInformation("[QuoteExpiryJob] No expired quotes");
                return;
            }

            _logger.LogInformation("[QuoteExpiryJob] Found {count} expired quotes", expiredRows.Count);

            foreach (var row in expiredRows)
            {
                row.q.status = "Rejected";

                // 2) update request
                row.req.process_status = "Rejected";
                row.req.reason = reason;

                var pendingPayments = await _db.payments
                    .AsTracking()
                    .Where(p =>
                        p.order_request_id == row.req.order_request_id
                        && p.status == "PENDING"
                        && p.provider == "PAYOS"
                    )
                    .ToListAsync(ct);

                foreach (var p in pendingPayments)
                {
                    p.status = "CANCELLED";
                    p.updated_at = AppTime.NowVnUnspecified();
                }

                if (!string.IsNullOrWhiteSpace(consultantEmail))
                {
                    try
                    {
                        var subject = "Từ chối deal do quá hạn";
                        var html = BuildConsultantExpiredEmailHtml(
                            requestId: row.req.order_request_id,
                            customerName: row.req.customer_name ?? "N/A",
                            customerPhone: row.req.customer_phone ?? "N/A",
                            customerEmail: row.req.customer_email ?? "N/A",
                            reason: reason,
                            expiredAt: row.est.created_at.AddHours(24)
                        );

                        await _emailService.SendAsync(consultantEmail, subject, html);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[QuoteExpiryJob] Failed to send consultant mail for request_id={id}", row.req.order_request_id);
                    }
                }
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("[QuoteExpiryJob] Done. Updated {count} requests/quotes", expiredRows.Count);
        }

        private static string BuildConsultantExpiredEmailHtml(
            int requestId,
            string customerName,
            string customerPhone,
            string customerEmail,
            string reason,
            DateTime expiredAt)
        {
            return $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='font-family: Arial, sans-serif; background:#f1f5f9; padding: 24px;'>
  <div style='max-width:640px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 4px 16px rgba(0,0,0,0.08)'>
    <div style='background:linear-gradient(135deg,#ef4444,#b91c1c);padding:20px;color:#fff;text-align:center'>
      <div style='font-size:18px;font-weight:800'>TỪ CHỐI DEAL DO QUÁ HẠN</div>
      <div style='opacity:.9;font-size:13px;margin-top:6px'>Báo giá đã quá hạn 24 giờ</div>
    </div>

    <div style='padding:22px'>
      <p style='margin:0 0 12px 0'>Request: <b>#AM{requestId:D6}</b></p>
      <p style='margin:0 0 12px 0'>Lý do: <b>{System.Net.WebUtility.HtmlEncode(reason)}</b></p>
      <p style='margin:0 0 12px 0'>Hết hạn lúc: <b>{expiredAt:dd/MM/yyyy HH:mm:ss}</b></p>

      <hr style='border:none;border-top:1px solid #e2e8f0;margin:16px 0' />

      <div style='font-weight:700;margin-bottom:8px'>Thông tin khách</div>
      <div>Tên: <b>{System.Net.WebUtility.HtmlEncode(customerName)}</b></div>
      <div>SĐT: <b>{System.Net.WebUtility.HtmlEncode(customerPhone)}</b></div>
      <div>Email: <b>{System.Net.WebUtility.HtmlEncode(customerEmail)}</b></div>

      <div style='margin-top:18px;padding:12px;background:#fff7ed;border:1px solid #fed7aa;border-radius:10px;color:#9a3412'>
        Ghi chú: Hệ thống tự động chuyển trạng thái do quá hạn 24h.
      </div>
    </div>

    <div style='background:#f8fafc;padding:14px;text-align:center;color:#94a3b8;font-size:12px;border-top:1px solid #e2e8f0'>
      Email tự động từ hệ thống MES
    </div>
  </div>
</body>
</html>";
        }
    }
}
