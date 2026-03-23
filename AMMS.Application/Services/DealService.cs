using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Background;
using AMMS.Shared.DTOs.Exceptions.AMMS.Application.Exceptions;
using AMMS.Shared.DTOs.PayOS;
using AMMS.Shared.DTOs.Socket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AMMS.Application.Services
{
    public class DealService : IDealService
    {
        private readonly IRequestRepository _requestRepo;
        private readonly ICostEstimateRepository _estimateRepo;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;
        private readonly IQuoteRepository _quoteRepo;
        private readonly IPayOsService _payOs;
        private readonly IPaymentsService _payment;
        private readonly IRealtimePublisher _rt;
        private readonly ILogger<DealService> _logger;
        private readonly IEmailBackgroundQueue _emailQueue;
        private readonly IUserRepository _userRepo;

        public DealService(
    IRequestRepository requestRepo,
    ICostEstimateRepository estimateRepo,
    IConfiguration config,
    IEmailService emailService,
    IQuoteRepository quoteRepo,
    IPayOsService payOs,
    IPaymentsService payment,
    IRealtimePublisher rt,
    ILogger<DealService> logger,
    IEmailBackgroundQueue emailQueue,
    IUserRepository userRepo)
        {
            _requestRepo = requestRepo;
            _estimateRepo = estimateRepo;
            _config = config;
            _emailService = emailService;
            _quoteRepo = quoteRepo;
            _payOs = payOs;
            _payment = payment;
            _rt = rt;
            _logger = logger;
            _emailQueue = emailQueue;
            _userRepo = userRepo;
        }

        public async Task SendDealAndEmailAsync(int orderRequestId, int? estimateId = null)
        {
            var locked = await _requestRepo.TryMarkDealWaitingFromVerifiedAsync(orderRequestId);
            if (!locked)
                throw new InvalidOperationException("Request must be Verified and not already sent");

            var req = await _requestRepo.GetByIdAsync(orderRequestId)
                ?? throw new Exception("Order request not found");

            if (string.IsNullOrWhiteSpace(req.customer_email))
            {
                req.process_status = "Verified";
                await _requestRepo.SaveChangesAsync();
                throw new Exception("Customer email missing");
            }

            List<cost_estimate> estimates;

            if (estimateId.HasValue && estimateId.Value > 0)
            {
                var one = await _estimateRepo.GetByIdAsync(estimateId.Value)
                    ?? throw new Exception("Estimate not found");

                if (one.order_request_id != orderRequestId)
                {
                    req.process_status = "Verified";
                    await _requestRepo.SaveChangesAsync();
                    throw new InvalidOperationException("Estimate does not belong to this order_request_id");
                }

                if (!one.is_active)
                {
                    req.process_status = "Verified";
                    await _requestRepo.SaveChangesAsync();
                    throw new InvalidOperationException("Estimate is inactive. Please create a new estimate.");
                }

                estimates = new List<cost_estimate> { one };
            }
            else
            {
                var all = await _estimateRepo.GetAllByOrderRequestIdAsync(orderRequestId);
                estimates = all.Where(x => x.is_active)
                               .OrderByDescending(x => x.estimate_id)
                               .Take(2)
                               .ToList();

                if (estimates.Count == 0)
                {
                    req.process_status = "Verified";
                    await _requestRepo.SaveChangesAsync();
                    throw new Exception("No active estimates found for this request");
                }
            }

            var baseUrlFe = _config["Deal:BaseUrlFe"]!;
            var consultantEmail = await ResolveConsultantEmailAsync(req);
            var quotePairs = new List<(cost_estimate est, quote q, string checkoutUrl)>();

            try
            {
                foreach (var est in estimates)
                {
                    var q = new quote
                    {
                        order_request_id = orderRequestId,
                        estimate_id = est.estimate_id,
                        total_amount = est.final_total_cost,
                        status = "Sent",
                        created_at = AppTime.NowVnUnspecified()
                    };

                    await _quoteRepo.AddAsync(q);

                    var checkoutUrl = $"{baseUrlFe}/checkout/{orderRequestId}";
                    quotePairs.Add((est, q, checkoutUrl));
                }

                await _quoteRepo.SaveChangesAsync();

                var htmlCustomer = DealEmailTemplates.QuoteEmailCompare(req, quotePairs);
                var sentSuffix = DateTime.Now.ToString("ddMMyyyy-HHmmss");

                var subjectCustomer =
                    quotePairs.Count == 1
                        ? $"Báo giá đơn hàng in ấn #{req.order_request_id:D6} (E{quotePairs[0].est.estimate_id}) - {sentSuffix}"
                        : $"Báo giá đơn hàng in ấn #{req.order_request_id:D6} (E{quotePairs[0].est.estimate_id} vs {quotePairs[1].est.estimate_id}) - {sentSuffix}";

                await _emailQueue.QueueAsync(new EmailQueueItem(
                    req.customer_email!,
                    subjectCustomer,
                    htmlCustomer));

                // enqueue consultant mail
                if (!string.IsNullOrWhiteSpace(consultantEmail))
                {
                    try
                    {
                        var consultantPairs = quotePairs
                            .Select(x => (x.est, x.q, checkoutUrl: (string?)null))
                            .ToList();

                        var htmlConsultant = DealEmailTemplates.QuoteEmailCompare(req, consultantPairs);

                        var subjectConsultant =
    quotePairs.Count == 1
        ? $"COPY báo giá #{req.order_request_id:D6} (E{quotePairs[0].est.estimate_id}) - {sentSuffix}"
        : $"COPY báo giá #{req.order_request_id:D6} (Compare {quotePairs[0].est.estimate_id} vs {quotePairs[1].est.estimate_id}) - {sentSuffix}";

                        await _emailQueue.QueueAsync(new EmailQueueItem(
                            consultantEmail!,
                            subjectConsultant,
                            htmlConsultant));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Queue consultant copy failed. RequestId={RequestId}; ConsultantEmail={ConsultantEmail}",
                            orderRequestId, consultantEmail);
                    }
                }

                await _rt.PublishRequestChangedAsync(new(
                    request_id: req.order_request_id,
                    old_status: "Verified",
                    new_status: "Waiting",
                    action: "sent_deal_email",
                    changed_at: AppTime.NowVnUnspecified(),
                    changed_by: null
                ));
            }
            catch
            {
                req.process_status = "Verified";
                await _requestRepo.SaveChangesAsync();
                throw;
            }
        }

        public async Task<string> AcceptAndCreatePayOsLinkAsync(int orderRequestId)
        {
            var req = await _requestRepo.GetByIdAsync(orderRequestId) ?? throw new Exception("Order request not found");
            var est = await _estimateRepo.GetByOrderRequestIdAsync(orderRequestId) ?? throw new Exception("Estimate not found");

            await SendConsultantStatusEmailAsync(req, est, statusText: "KHÁCH ĐỒNG Ý BÁO GIÁ");

            var deposit = est.deposit_amount;
            var amount = (int)Math.Round(deposit, 0) / 100;
            var description = $"AM{orderRequestId:D6}";

            var orderCode = await GetOrCreatePayOsOrderCodeAsync(orderRequestId);
            var baseUrl = _config["Deal:BaseUrl"]!;

            var returnUrl = $"{baseUrl}/api/requests/payos/return?request_id={orderRequestId}&order_code={orderCode}";
            var cancelUrl = $"{baseUrl}/api/requests/payos/cancel?orderRequestId={orderRequestId}&orderCode={orderCode}";

            var existing = await _payOs.GetPaymentLinkInformationAsync(orderCode);
            if (existing != null)
            {
                var st = (existing.status ?? "").ToUpperInvariant();
                if (st == "PENDING" || st == "PAID" || st == "SUCCESS" || st == "CANCELLED")
                    return existing.check_out_url ?? "";
            }

            var result = await _payOs.CreatePaymentLinkAsync(
                orderCode: orderCode,
                amount: amount,
                description: description,
                buyerName: req.customer_name ?? "N/A",
                buyerEmail: req.customer_email ?? "",
                buyerPhone: req.customer_phone ?? "",
                returnUrl: returnUrl,
                cancelUrl: cancelUrl
            );

            return result.check_out_url ?? "";
        }

        public async Task RejectDealAsync(int orderRequestId, string reason)
        {
            var req = await _requestRepo.GetByIdAsync(orderRequestId)
                ?? throw new Exception("Order request not found");

            req.process_status = "Rejected";

            if (req.quote_id != null)
            {
                var q = await _quoteRepo.GetByIdAsync(req.quote_id.Value);
                if (q != null) q.status = "Rejected";
                await _quoteRepo.SaveChangesAsync();
            }

            await _requestRepo.SaveChangesAsync();

            cost_estimate? est = null;
            try 
            { 
                est = await _estimateRepo.GetByOrderRequestIdAsync(orderRequestId); 
            } 
            catch { }

            var safeReason = System.Net.WebUtility.HtmlEncode(reason ?? "");

            RequestChangedEvent evt = new RequestChangedEvent(orderRequestId, "", req.process_status, "customer_rejected", DateTime.Now, "Customer");
            await _rt.PublishRequestChangedAsync(evt);

            await SendConsultantStatusEmailAsync(req, est, $"KHACH TU CHOI (LY DO: {safeReason})");
        }


        public async Task SendConsultantStatusEmailAsync(
    order_request req,
    cost_estimate? est,
    string statusText,
    decimal? paidAmount = null,
    DateTime? paidAt = null,
    CancellationToken ct = default)
        {
            var consultantEmail = await ResolveConsultantEmailAsync(req, ct);
            if (string.IsNullOrWhiteSpace(consultantEmail))
                return;

            var address = $"{req.detail_address}";
            var delivery = req.delivery_date?.ToString("dd/MM/yyyy") ?? "N/A";

            var finalTotal = est?.final_total_cost ?? 0m;
            var deposit = est?.deposit_amount ?? 0m;

            var paidLine = paidAmount.HasValue
                ? $"<p><b>Số tiền đã thanh toán:</b> {paidAmount.Value:n0} VND</p>"
                : "";

            var paidAtLine = paidAt.HasValue
                ? $"<p><b>Thời gian thanh toán:</b> {paidAt.Value:dd/MM/yyyy HH:mm:ss}</p>"
                : "";
            string FormatVND(decimal amount) => string.Format("{0:N0} đ", amount);

            string paymentInfoHtml = "";
            if (paidAmount.HasValue)
            {
                paymentInfoHtml = $@"
            <div style='background-color: #f8fafc; border: 1px dashed #cbd5e1; border-radius: 8px; padding: 15px; margin-top: 20px;'>
                <table width='100%'>
                    <tr>
                        <td style='color: #64748b; font-size: 13px;'>Số tiền đã nhận:</td>
                        <td style='text-align: right; color: #059669; font-weight: 700; font-size: 16px;'>{FormatVND(paidAmount.Value)}</td>
                    </tr>
                    {(paidAt.HasValue ? $"<tr><td style='color: #64748b; font-size: 12px;'>Thời gian:</td><td style='text-align: right; color: #94a3b8; font-size: 12px;'>{paidAt.Value:dd/MM/yyyy HH:mm:ss}</td></tr>" : "")}
                </table>
            </div>";
            }

            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
</head>
<body style='margin: 0; padding: 30px 0; background-color: #f1f5f9; font-family: sans-serif;'>
    <div style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1);'>
        
        <div style='background: linear-gradient(135deg, #4f46e5 0%, #3730a3 100%); padding: 25px; text-align: center;'>
            <div style='color: #ffffff; font-size: 18px; font-weight: 700; letter-spacing: 1px;'>CẬP NHẬT HỆ THỐNG</div>
            <div style='color: #e0e7ff; font-size: 13px; margin-top: 5px;'>Thông báo trạng thái đơn hàng mới</div>
        </div>

        <div style='padding: 30px;'>
            <div style='text-align: center; margin-bottom: 25px;'>
                <span style='background-color: #f1f5f9; color: #1e293b; padding: 6px 16px; border-radius: 50px; font-size: 13px; font-weight: 700; border: 1px solid #e2e8f0;'>
                    TRẠNG THÁI: {statusText.ToUpper()}
                </span>
            </div>

            <table width='100%' border='0' cellpadding='0' cellspacing='0'>
                <tr>
                    <td width='48%' style='vertical-align: top;'>
                        <div style='font-size: 13px; font-weight: 700; color: #4f46e5; border-bottom: 2px solid #e0e7ff; padding-bottom: 5px; margin-bottom: 12px; text-transform: uppercase;'>Khách hàng</div>
                        <div style='font-size: 14px; color: #1e293b; font-weight: 600; margin-bottom: 4px;'>{req.customer_name}</div>
                        <div style='font-size: 12px; color: #64748b; margin-bottom: 2px;'>📞 {req.customer_phone}</div>
                        <div style='font-size: 12px; color: #64748b; margin-bottom: 2px;'>✉️ {req.customer_email}</div>
                        <div style='font-size: 12px; color: #64748b; line-height: 1.4;'>📍 {address}</div>
                    </td>

                    <td width='4%'></td>

                    <td width='48%' style='vertical-align: top;'>
                        <div style='font-size: 13px; font-weight: 700; color: #f59e0b; border-bottom: 2px solid #fef3c7; padding-bottom: 5px; margin-bottom: 12px; text-transform: uppercase;'>Đơn hàng</div>
                        <table width='100%'>
                            <tr><td style='font-size: 12px; color: #64748b; padding: 2px 0;'>Mã Request:</td><td style='font-size: 12px; color: #1e293b; font-weight: 600; text-align: right;'>#AM{req.order_request_id:D6}</td></tr>
                            <tr><td style='font-size: 12px; color: #64748b; padding: 2px 0;'>Sản phẩm:</td><td style='font-size: 12px; color: #1e293b; font-weight: 600; text-align: right;'>{req.product_name}</td></tr>
                            <tr><td style='font-size: 12px; color: #64748b; padding: 2px 0;'>Số lượng:</td><td style='font-size: 12px; color: #1e293b; font-weight: 600; text-align: right;'>{req.quantity:N0}</td></tr>
                            <tr><td style='font-size: 12px; color: #64748b; padding: 2px 0;'>Ngày giao:</td><td style='font-size: 12px; color: #1e293b; font-weight: 600; text-align: right;'>{delivery}</td></tr>
                        </table>
                    </td>
                </tr>
            </table>

            <div style='margin-top: 25px; padding-top: 15px; border-top: 1px solid #f1f5f9;'>
                <table width='100%'>
                    <tr>
                        <td style='color: #64748b; font-size: 13px;'>Tổng giá trị đơn hàng:</td>
                        <td style='text-align: right; color: #1e293b; font-weight: 600; font-size: 13px;'>{FormatVND(finalTotal)}</td>
                    </tr>
                    <tr>
                        <td style='color: #64748b; font-size: 13px; padding-top: 5px;'>Yêu cầu đặt cọc:</td>
                        <td style='text-align: right; color: #1e293b; font-weight: 600; font-size: 13px; padding-top: 5px;'>{FormatVND(deposit)}</td>
                    </tr>
                </table>
            </div>

            {paymentInfoHtml}

            <div style='margin-top: 30px; text-align: center;'>
                <a href='#' style='background-color: #4f46e5; color: white; padding: 10px 20px; border-radius: 6px; text-decoration: none; font-size: 13px; font-weight: 600;'>Truy cập hệ thống quản trị</a>
            </div>
        </div>

        <div style='background-color: #f8fafc; padding: 15px; text-align: center; color: #94a3b8; font-size: 11px; border-top: 1px solid #f1f5f9;'>
            Email này được gửi tự động từ hệ thống quản lý MES nội bộ.
        </div>
    </div>
</body>
</html>";

            await _emailService.SendAsync(
                consultantEmail,
                $"[MES] Trạng thái đơn #{req.order_request_id:D6}: {statusText}",
                html
            );
        }

        public async Task NotifyConsultantPaidAsync(int orderRequestId, decimal paidAmount, DateTime paidAt)
        {
            var req = await _requestRepo.GetByIdAsync(orderRequestId)
                ?? throw new Exception("Order request not found");

            cost_estimate? est = null;
            try
            {
                if (req.accepted_estimate_id.HasValue)
                    est = await _estimateRepo.GetByIdAsync(req.accepted_estimate_id.Value);
                else
                    est = await _estimateRepo.GetByOrderRequestIdAsync(orderRequestId);
            }
            catch { }

            await SendConsultantStatusEmailAsync(
                req,
                est,
                statusText: "KHÁCH ĐỒNG Ý & ĐÃ THANH TOÁN CỌC",
                paidAmount: paidAmount,
                paidAt: paidAt
            );
        }
        public async Task NotifyCustomerPaidAsync(int orderRequestId, decimal paidAmount, DateTime paidAt)
        {
            var req = await _requestRepo.GetByIdAsync(orderRequestId)
                ?? throw new Exception("Order request not found");
            var fe = _config["Deal:BaseUrlFe"]!;

            if (string.IsNullOrWhiteSpace(req.customer_email))
                return;

            cost_estimate? est = null;
            try
            {
                if (req.accepted_estimate_id.HasValue)
                    est = await _estimateRepo.GetByIdAsync(req.accepted_estimate_id.Value);
                else
                    est = await _estimateRepo.GetByOrderRequestIdAsync(orderRequestId);
            }
            catch { }

            var finalTotal = est?.final_total_cost ?? 0m;
            var deposit = est?.deposit_amount ?? 0m;
            string FormatVND(decimal amount) => string.Format("{0:N0} đ", amount);

            var html = $@"
<!DOCTYPE html>
<html>
<head>
<style>
    body {{ margin: 0; padding: 0; font-family: 'Helvetica Neue', Helvetica, Arial, sans-serif; background-color: #f1f5f9; }}
    .container {{ max-width: 600px; margin: 0 auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.05); }}
    table {{ width: 100%; border-collapse: collapse; }}
    td {{ vertical-align: top; }}
    
    /* Typography */
    .header-text {{ color: #ffffff; font-size: 20px; font-weight: 700; }}
    .label {{ color: #64748b; font-size: 13px; padding: 8px 0; }}
    .value {{ color: #1e293b; font-weight: 600; font-size: 13px; text-align: right; padding: 8px 0; }}
    .section-title {{ font-size: 14px; font-weight: 700; text-transform: uppercase; color: #334155; margin-bottom: 10px; border-bottom: 2px solid #e2e8f0; padding-bottom: 5px; display: inline-block; }}
    
    /* Success Box */
    .success-box {{ background-color: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 8px; padding: 20px; text-align: center; margin-bottom: 25px; }}
    .paid-amount {{ color: #15803d; font-size: 24px; font-weight: 800; margin: 5px 0; }}
    .success-badge {{ display: inline-block; background: #16a34a; color: white; padding: 4px 12px; border-radius: 50px; font-size: 12px; font-weight: bold; margin-bottom: 8px; }}
</style>
</head>
<body style='padding: 30px 0;'>

  <div class='container'>
    
    <div style='background: linear-gradient(135deg, #2563eb 0%, #1d4ed8 100%); padding: 25px 30px; text-align: center;'>
       <div class='header-text'>XÁC NHẬN THANH TOÁN</div>
       <div style='color: #bfdbfe; font-size: 13px; margin-top: 5px;'>Cảm ơn bạn đã thanh toán tiền cọc</div>
    </div>

    <div style='padding: 30px;'>
      
      <p style='margin: 0 0 20px 0; color: #334155; font-size: 15px;'>
        Chào <b>{req.customer_name}</b>,<br>
        Hệ thống MES đã nhận được khoản thanh toán của bạn cho đơn hàng <b>AM{req.order_request_id:D6}</b>.
      </p>

      <div class='success-box'>
         <div class='success-badge'>THANH TOÁN THÀNH CÔNG</div>
         <div style='color: #86efac; font-size: 40px; line-height: 1;'>&#10003;</div>
         <div style='color: #64748b; font-size: 13px; margin-top: 10px;'>Số tiền đã nhận</div>
         <div class='paid-amount'>{FormatVND(paidAmount)}</div>
         <div style='color: #94a3b8; font-size: 12px;'>Thời gian: {paidAt:dd/MM/yyyy HH:mm:ss}</div>
      </div>

      <table border='0' cellpadding='0' cellspacing='0'>
        <tr>
           <td width='48%' style='padding-right: 15px;'>
              <div class='section-title' style='border-color: #3b82f6; color: #2563eb;'>Thông tin đơn hàng</div>
              <table width='100%'>
                 <tr><td class='label'>Mã đơn</td><td class='value'>AM{req.order_request_id:D6}</td></tr>
                 <tr><td class='label'>Sản phẩm</td><td class='value'>{req.product_name}</td></tr>
                 <tr><td class='label'>Số lượng</td><td class='value'>{req.quantity:N0}</td></tr>
              </table>
           </td>
           
           <td width='4%'></td>

           <td width='48%' style='padding-left: 15px;'>
              <div class='section-title' style='border-color: #f59e0b; color: #d97706;'>Chi tiết tài chính</div>
              <table width='100%'>
                 <tr><td class='label'>Tổng giá trị</td><td class='value'>{FormatVND(finalTotal)}</td></tr>
                 <tr><td class='label'>Yêu cầu cọc</td><td class='value'>{FormatVND(deposit)}</td></tr>
                 <tr>
                    <td class='label' style='border-top: 1px dashed #cbd5e1; color: #059669; font-weight: 700;'>Đã thanh toán</td>
                    <td class='value' style='border-top: 1px dashed #cbd5e1; color: #059669; font-weight: 800;'>{FormatVND(paidAmount)}</td>
                 </tr>
              </table>
           </td>
        </tr>
      </table>

      <div style='margin-top: 30px; border-top: 1px solid #f1f5f9; padding-top: 20px; text-align: center;'>
   <p style='color: #64748b; font-size: 13px; line-height: 1.5; margin: 0 0 8px 0;'>
      Đơn hàng của bạn đang được xử lý.
   </p>
   <p style='color: #64748b; font-size: 13px; line-height: 1.5; margin: 0 0 8px 0;'>
      Bạn có thể tra cứu tiến trình đơn hàng bằng cách copy đường dẫn bên dưới và dán vào trình duyệt:
   </p>
   <p style='color: #0f172a; font-size: 12px; line-height: 1.6; margin: 0; background:#ffffff; border:1px solid #e2e8f0; border-radius:8px; padding:10px 12px; word-break:break-all; user-select:all; -webkit-user-select:all;'>
      {fe}/look-up
   </p>
</div>

    </div>
    
    <div style='background-color: #f8fafc; padding: 15px; text-align: center; color: #94a3b8; font-size: 12px;'>
      &copy; {DateTime.Now.Year} MES Printing System
    </div>

  </div>
</body>
</html>";

            await _emailService.SendAsync(
                req.customer_email,
                $"[MES] Xác nhận thanh toán thành công - Đơn #AM{req.order_request_id:D6}",
                html
            );
        }

        public async Task<PayOsResultDto> CreateOrReuseDepositLinkAsync(int requestId, int estimateId, int? quoteId, CancellationToken ct = default)
        {
            var req = await _requestRepo.GetByIdAsync(requestId)
                      ?? throw new InvalidOperationException("Request not found");

            if (string.Equals(req.process_status, "Rejected", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Request has been Rejected. Cannot create payment link.");

            if (string.Equals(req.process_status, "Accepted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Request has been Accepted. Cannot create payment link.");

            var est = await _estimateRepo.GetByIdAsync(estimateId)
              ?? throw new InvalidOperationException("Cost estimate not found");

            if (est.order_request_id != requestId)
                throw new InvalidOperationException("Estimate does not belong to this request");

            var pending = await _payment.GetLatestPendingByRequestIdAndEstimateIdAsync(requestId, estimateId, ct);
            if (pending != null)
            {
                PayOsResultDto? liveInfo = null;
                PayOsResultDto? savedInfo = null;

                try
                {
                    liveInfo = await _payOs.GetPaymentLinkInformationAsync(pending.order_code, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Cannot fetch PayOS live info. RequestId={RequestId}, EstimateId={EstimateId}, OrderCode={OrderCode}",
                        requestId, estimateId, pending.order_code);
                }

                if (!string.IsNullOrWhiteSpace(pending.payos_raw))
                {
                    try
                    {
                        savedInfo = PayOsRawMapper.FromPayment(pending);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Cannot parse saved payos_raw. RequestId={RequestId}, EstimateId={EstimateId}, OrderCode={OrderCode}",
                            requestId, estimateId, pending.order_code);
                    }
                }

                var merged = MergePayOsResult(liveInfo, savedInfo, pending);

                if (IsReusablePayOsStatus(merged.status))
                {
                    if (!string.IsNullOrWhiteSpace(merged.check_out_url))
                        return merged;

                    return merged;
                }
            }

            var backendUrl = _config["Deal:BaseUrl"]!;
            var feBase = _config["Deal:BaseUrlFe"] ?? "https://sep490-fe.vercel.app";

            var amount = (int)Math.Round(est.deposit_amount, 0) / 100;

            const int maxAttempt = 9;
            Exception? last = null;

            for (int attempt = 1; attempt <= maxAttempt; attempt++)
            {
                int orderCode = checked(requestId * 10 + attempt);
                var description = $"MES{orderCode}";
                var returnUrl =
                            $"{backendUrl}/api/requests/payos/return" +
                            $"?request_id={requestId}&order_code={orderCode}&estimate_id={estimateId}" +
                            (quoteId.HasValue && quoteId.Value > 0 ? $"&quote_id={quoteId.Value}" : "");
                var cancelUrl = $"{feBase}/reject-deal/{requestId}?status=cancel";

                try
                {
                    var payos = await _payOs.CreatePaymentLinkAsync(
                        orderCode: orderCode,
                        amount: amount,
                        description: description,
                        buyerName: req.customer_name ?? "Khach hang",
                        buyerEmail: req.customer_email ?? "",
                        buyerPhone: req.customer_phone ?? "",
                        returnUrl: returnUrl,
                        cancelUrl: cancelUrl,
                        ct: ct);

                    var now = AppTime.NowVnUnspecified();
                    await _payment.UpsertPendingAsync(new payment
                    {
                        order_request_id = requestId,
                        provider = "PAYOS",
                        order_code = orderCode,
                        amount = payos.amount ?? amount,
                        currency = "VND",
                        status = "PENDING",
                        payos_payment_link_id = payos.payment_link_id,
                        payos_transaction_id = payos.transaction_id,
                        payos_raw = payos.raw_json,
                        created_at = now,
                        updated_at = now,
                        estimate_id = estimateId,
                        quote_id = (quoteId.HasValue && quoteId.Value > 0) ? quoteId.Value : null
                    }, ct);

                    await _payment.SaveChangesAsync(ct);
                    return payos;
                }
                catch (PayOsException ex) when (IsDuplicateOrderCode(ex.Message))
                {
                    last = ex;
                    continue;
                }
            }

            throw new InvalidOperationException($"Cannot create PayOS link after retries. Last error: {last?.Message}");
        }

        private static bool IsDuplicateOrderCode(string msg)
        {
            msg = (msg ?? "").ToLowerInvariant();
            return msg.Contains("ordercode") && (msg.Contains("exists") || msg.Contains("tồn tại") || msg.Contains("231"));
        }

        private async Task<int> GetOrCreatePayOsOrderCodeAsync(
    int orderRequestId,
    CancellationToken ct = default,
    int maxAttempt = 9)
        {
            for (int attempt = 1; attempt <= maxAttempt; attempt++)
            {
                int orderCode = checked(orderRequestId * 10 + attempt);

                var info = await _payOs.GetPaymentLinkInformationAsync(orderCode, ct);

                if (info == null) return orderCode;

                var st = (info.status ?? "").ToUpperInvariant();
                if (st == "PENDING" || st == "PAID" || st == "SUCCESS")
                    return orderCode;

                if (st == "CANCELLED" || st == "EXPIRED")
                    continue;
            }

            throw new InvalidOperationException("Cannot allocate orderCode: attempts exhausted.");
        }

        private static bool IsReusablePayOsStatus(string? status)
        {
            var st = (status ?? "").Trim().ToUpperInvariant();
            return st is "PENDING" or "PROCESSING" or "PAID" or "SUCCESS";
        }

        private static PayOsResultDto MergePayOsResult(
            PayOsResultDto? live,
            PayOsResultDto? saved,
            payment? dbPayment = null)
        {
            return new PayOsResultDto
            {
                expired_at = live?.expired_at ?? saved?.expired_at,

                check_out_url = !string.IsNullOrWhiteSpace(live?.check_out_url)
                    ? live!.check_out_url
                    : saved?.check_out_url,

                qr_code = !string.IsNullOrWhiteSpace(live?.qr_code)
                    ? live!.qr_code
                    : saved?.qr_code,

                account_number = !string.IsNullOrWhiteSpace(live?.account_number)
                    ? live!.account_number
                    : saved?.account_number,

                account_name = !string.IsNullOrWhiteSpace(live?.account_name)
                    ? live!.account_name
                    : saved?.account_name,

                bin = !string.IsNullOrWhiteSpace(live?.bin)
                    ? live!.bin
                    : saved?.bin,

                amount = live?.amount
                    ?? saved?.amount
                    ?? (dbPayment != null ? (int?)decimal.ToInt32(dbPayment.amount) : null),

                status = !string.IsNullOrWhiteSpace(live?.status)
                    ? live!.status
                    : (!string.IsNullOrWhiteSpace(saved?.status) ? saved!.status : dbPayment?.status),

                description = !string.IsNullOrWhiteSpace(live?.description)
                    ? live!.description
                    : saved?.description,

                payment_link_id = !string.IsNullOrWhiteSpace(live?.payment_link_id)
                    ? live!.payment_link_id
                    : (!string.IsNullOrWhiteSpace(saved?.payment_link_id) ? saved!.payment_link_id : dbPayment?.payos_payment_link_id),

                transaction_id = !string.IsNullOrWhiteSpace(live?.transaction_id)
                    ? live!.transaction_id
                    : (!string.IsNullOrWhiteSpace(saved?.transaction_id) ? saved!.transaction_id : dbPayment?.payos_transaction_id),

                raw_json = !string.IsNullOrWhiteSpace(live?.raw_json)
                    ? live!.raw_json
                    : (!string.IsNullOrWhiteSpace(saved?.raw_json) ? saved!.raw_json : dbPayment?.payos_raw),

                order_code = (live?.order_code ?? 0) > 0
                    ? live!.order_code
                    : ((saved?.order_code ?? 0) > 0 ? saved!.order_code : dbPayment?.order_code ?? 0)
            };
        }

        private async Task<string?> ResolveConsultantEmailAsync(order_request req, CancellationToken ct = default)
        {
            if (req.assigned_consultant.HasValue)
            {
                var assignedUser = await _userRepo.GetByIdAsync(req.assigned_consultant.Value, ct);
                if (!string.IsNullOrWhiteSpace(assignedUser?.email))
                    return assignedUser.email.Trim();
            }

            return _config["Deal:ConsultantEmail"]?.Trim();
        }
    }
}
