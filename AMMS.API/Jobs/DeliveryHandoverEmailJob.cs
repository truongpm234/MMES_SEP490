using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AMMS.API.Jobs
{
    public sealed class DeliveryHandoverEmailJob
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;
        private readonly ILogger<DeliveryHandoverEmailJob> _logger;

        public DeliveryHandoverEmailJob(
            AppDbContext db,
            IConfiguration config,
            IEmailService emailService,
            ILogger<DeliveryHandoverEmailJob> logger)
        {
            _db = db;
            _config = config;
            _emailService = emailService;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 180, 600 })]
        [DisableConcurrentExecution(timeoutInSeconds: 5 * 60)]
        public async Task RunAsync(int orderId, CancellationToken ct = default)
        {
            var ord = await _db.orders
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.order_id == orderId, ct);

            if (ord == null)
            {
                _logger.LogWarning("[DeliveryHandoverEmailJob] Order not found. orderId={OrderId}", orderId);
                return;
            }

            var req = await _db.order_requests
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.order_id == orderId, ct);

            if (req == null)
            {
                _logger.LogWarning("[DeliveryHandoverEmailJob] Order request not found. orderId={OrderId}", orderId);
                return;
            }

            if (string.IsNullOrWhiteSpace(req.customer_email))
            {
                _logger.LogWarning("[DeliveryHandoverEmailJob] Customer email missing. requestId={RequestId}", req.order_request_id);
                return;
            }

            var prod = await _db.productions
                .AsNoTracking()
                .Where(x => x.order_id == orderId)
                .OrderByDescending(x => x.prod_id)
                .FirstOrDefaultAsync(ct);

            var est = await _db.cost_estimates
                .AsNoTracking()
                .Where(x => x.order_request_id == req.order_request_id && x.is_active)
                .OrderByDescending(x => x.estimate_id)
                .FirstOrDefaultAsync(ct);

            var isDelivery =
                string.Equals(ord.status, "Delivery", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(req.process_status, "Delivery", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prod?.status, "Delivery", StringComparison.OrdinalIgnoreCase);

            if (!isDelivery)
            {
                _logger.LogInformation(
                    "[DeliveryHandoverEmailJob] Skip because status is not Delivery. orderId={OrderId}, orderStatus={OrderStatus}, requestStatus={RequestStatus}, prodStatus={ProdStatus}",
                    orderId, ord.status, req.process_status, prod?.status);
                return;
            }

            var feBase = (_config["Deal:BaseUrlFe"] ?? "").TrimEnd('/');
            if (string.IsNullOrWhiteSpace(feBase))
                throw new InvalidOperationException("Missing Deal:BaseUrlFe");

            var confirmReceiveUrl = $"{feBase}/customer-receive/{req.order_request_id}";

            var html = DeliveryEmailTemplates.BuildDeliveryHandoverEmail(
                req,
                ord,
                prod,
                est,
                confirmReceiveUrl);

            var subject = $"[MES] Đơn hàng {ord.code} đã được bàn giao cho đơn vị vận chuyển";

            await _emailService.SendAsync(req.customer_email!, subject, html);

            _logger.LogInformation(
                "[DeliveryHandoverEmailJob] Sent delivery handover email successfully. orderId={OrderId}, requestId={RequestId}, to={To}",
                orderId, req.order_request_id, req.customer_email);
        }
    }
}