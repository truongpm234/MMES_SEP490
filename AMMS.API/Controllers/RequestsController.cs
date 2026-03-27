using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Application.Services;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Email;
using AMMS.Shared.DTOs.PayOS;
using AMMS.Shared.DTOs.Requests;
using AMMS.Shared.DTOs.Requests.AMMS.Shared.DTOs.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static AMMS.Shared.DTOs.Auth.Auth;

namespace AMMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RequestsController : ControllerBase
    {
        private readonly IRequestService _service;
        private readonly IDealService _dealService;
        private readonly IPaymentsService _paymentService;
        private readonly IProductionSchedulingService _schedulingService;
        private readonly AppDbContext _db;
        private readonly ISmsOtpService _smsOtp;
        private readonly IConfiguration _config;
        private readonly IPayOsService _payos;
        private readonly ILogger<RequestsController> _logger;
        public RequestsController(
            IRequestService service,
            IDealService dealService,
            IPaymentsService paymentService,
            AppDbContext db,
            IProductionSchedulingService schedulingService,
            ISmsOtpService smsOtp,
            IConfiguration config, IPayOsService payos, ILogger<RequestsController> logger)
        {
            _service = service;
            _dealService = dealService;
            _paymentService = paymentService;
            _db = db;
            _schedulingService = schedulingService;
            _smsOtp = smsOtp;
            _config = config;
            _payos = payos;
            _logger = logger;
        }
        [HttpPost("create-request-by-consultant")]
        [ProducesResponseType(typeof(CreateRequestResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> CreateOrderRequest([FromBody] CreateResquestConsultant dto)
        {
            var result = await _service.CreateRequestByConsultantAsync(dto);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [HttpPost]
        [ProducesResponseType(typeof(CreateRequestResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> Create([FromBody] CreateResquest req)
        {
            var result = await _service.CreateAsync(req);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [HttpPost("clone-request")]
        public async Task<IActionResult> CloneRequest([FromBody] CloneRequestDto dto, CancellationToken ct)
        {
            try
            {
                if (dto == null || dto.request_id <= 0)
                    return BadRequest(new { message = "request_id is required" });

                var result = await _service.CloneRequestAsync(dto.request_id, ct);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An unexpected error occurred",
                    detail = ex.Message
                });
            }
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(UpdateRequestResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(UpdateRequestResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UpdateRequestResponse>> UpdateAsync(int id, [FromBody] UpdateOrderRequest request)
        {
            var update = await _service.UpdateAsync(id, request);
            return StatusCode(StatusCodes.Status200OK, update);
        }

        [HttpPut("cancel-request")]
        public async Task<IActionResult> Delete([FromBody] CancelRequestDto dto, CancellationToken ct)
        {
            await _service.CancelAsync(dto.id, dto.reason, ct);
            return Ok(new { message = "Cancelled", order_request_id = dto.id });
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(RequestWithCostDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRequestById(int id, CancellationToken ct)
        {
            var requestDto = await _service.GetByIdWithCostAsync(id);

            if (requestDto == null)
                return NotFound(new { message = "Order request not found" });
            if(!requestDto.estimate_finish_date.HasValue)
            {
                requestDto.estimate_finish_date = await _service.RecalculateAndPersistAsync(id, ct);
            }
            return Ok(requestDto);
        }

        [HttpGet("paged")]
        public async Task<IActionResult> GetPaged([FromQuery] int page, [FromQuery] int pageSize)
        {
            var result = await _service.GetPagedAsync(page, pageSize);
            return Ok(result);
        }

        [HttpPut("approval")]
        public async Task<IActionResult> Approval([FromBody] RequestApprovalUpdateDto dto, CancellationToken ct)
        {
            try
            {
                if (dto == null)
                    return BadRequest(new { message = "Request body is required" });

                if (dto.request_id <= 0)
                    return BadRequest(new { message = "request_id must be greater than 0" });

                if (string.IsNullOrWhiteSpace(dto.status))
                    return BadRequest(new { message = "status is required" });

                var validStatuses = new[] { "Verified", "Declined" };
                if (!validStatuses.Contains(dto.status, StringComparer.OrdinalIgnoreCase))
                    return BadRequest(new
                    {
                        message = $"Invalid status. Allowed values: {string.Join(", ", validStatuses)}"
                    });

                await _service.UpdateApprovalAsync(dto, ct);

                return Ok(new
                {
                    message = "Updated approval",
                    status = dto.status,
                    request_id = dto.request_id
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An unexpected error occurred",
                    detail = ex.Message
                });
            }
        }

        [HttpPost("send-deal")]
        public async Task<IActionResult> SendDealEmail([FromBody] SendDealEmailRequest req)
        {
            try
            {
                await _dealService.SendDealAndEmailAsync(req.request_id);
                return Ok(new { message = "Báo giá đã được hệ thống gửi.", request_id = req.request_id });
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ SendDealEmail failed:");
                Console.WriteLine(ex);

                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    message = "Send email failed",
                    detail = ex.Message,
                    request_id = req.request_id
                });
            }
        }

        [HttpGet("accept-pay")]
        public async Task<IActionResult> AcceptPay([FromQuery] int orderRequestId, [FromQuery] string token)
        {
            var req = await _service.GetByIdAsync(orderRequestId);
            if (req == null)
                return NotFound(new { message = "Order request not found" });

            if (string.Equals(req.process_status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                var fe = "https://sep490-fe.vercel.app";
                return Redirect($"{fe}/deal-invalid?orderRequestId={orderRequestId}&reason=rejected");
            }

            var now = AppTime.NowVnUnspecified();
            var validation = ValidateQuotePaymentWindow(req, now);
            if (!validation.ok)
                return BadRequest(new { message = validation.message, orderRequestId });

            var checkoutUrl = await _dealService.AcceptAndCreatePayOsLinkAsync(orderRequestId);
            return Redirect(checkoutUrl);
        }

        [HttpPost("confirm-webhook")]
        public async Task<IActionResult> ConfirmWebhook(CancellationToken ct)
        {
            await _payos.ConfirmWebhookAsync(ct);
            return Ok(new { ok = true });
        }

        [AllowAnonymous]
        [HttpGet("payos/status-by-request-id")]
        public async Task<IActionResult> CheckAndProcessPayOsPayment(
    [FromQuery] int request_id,
    [FromQuery] int estimate_id,
    [FromQuery] int? quote_id,
    CancellationToken ct)
        {
            if (request_id <= 0)
                return BadRequest(new { message = "request_id is required" });

            if (estimate_id <= 0)
                return BadRequest(new { message = "estimate_id is required" });

            var latest = await _paymentService.GetLatestByRequestIdAndEstimateIdAsync(request_id, estimate_id, ct);

            if (latest == null)
            {
                return Ok(new
                {
                    paid = false,
                    processed = false,
                    message = "Payment not found"
                });
            }

            if (string.Equals(latest.status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new
                {
                    paid = true,
                    processed = true,
                    status = "PAID",
                    order_code = latest.order_code,
                    estimate_id = latest.estimate_id,
                    quote_id = latest.quote_id
                });
            }

            var info = await _payos.GetPaymentLinkInformationAsync(latest.order_code, ct);

            var isPaid =
                string.Equals(info?.status, "PAID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info?.status, "SUCCESS", StringComparison.OrdinalIgnoreCase);

            if (!isPaid)
            {
                return Ok(new
                {
                    paid = false,
                    processed = false,
                    status = info?.status ?? latest.status ?? "PENDING",
                    order_code = latest.order_code,
                    estimate_id = latest.estimate_id,
                    quote_id = latest.quote_id
                });
            }

            var result = await ProcessPaidAsync(
                orderRequestId: request_id,
                orderCode: latest.order_code,
                amount: info?.amount ?? (long)latest.amount,
                paymentLinkId: info?.payment_link_id ?? latest.payos_payment_link_id,
                transactionId: info?.transaction_id ?? latest.payos_transaction_id,
                rawJson: info?.raw_json ?? latest.payos_raw ?? "{}",
                estimateIdFromQuery: latest.estimate_id ?? estimate_id,
                quoteIdFromQuery: latest.quote_id ?? quote_id,
                paymentRepo: HttpContext.RequestServices.GetRequiredService<IPaymentRepository>(),
                ct: ct
            );

            var latestAfter = await _paymentService.GetLatestByRequestIdAndEstimateIdAsync(request_id, estimate_id, ct);

            return Ok(new
            {
                paid = result.ok,
                processed = result.ok,
                message = result.message,
                status = latestAfter?.status ?? info?.status ?? "PAID",
                order_code = latest.order_code,
                estimate_id = latestAfter?.estimate_id ?? latest.estimate_id,
                quote_id = latestAfter?.quote_id ?? latest.quote_id
            });
        }

        [HttpPost("reject")]
        public async Task<IActionResult> RejectDeal([FromBody] RejectDealRequest dto, CancellationToken ct)
        {
            // 1) Lấy order_request
            var req = await _service.GetByIdAsync(dto.order_request_id);
            if (req == null)
                return NotFound(new { message = "Order request not found" });

            // 2) chanự reject nếu accept rồi
            if (string.Equals(req.process_status, "Accepted", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Order has already been accepted, cannot reject." });

            // 3) Validate OTP
            if (string.IsNullOrWhiteSpace(dto.otp))
                return BadRequest(new { message = "OTP is required to reject this deal." });

            // 4) Xác định phone để verify OTP
            var phone = dto.phone;
            if (string.IsNullOrWhiteSpace(phone))
                phone = req.customer_phone ?? "";

            if (string.IsNullOrWhiteSpace(phone))
                return BadRequest(new { message = "Customer phone is missing, cannot verify OTP." });

            // 5) Verify OTP qua SMS
            var verifyReq = new VerifyOtpSmsRequest(phone, dto.otp);

            var verifyRes = await _smsOtp.VerifyOtpAsync(verifyReq, ct);
            if (!verifyRes.success || !verifyRes.valid)
                return BadRequest(new { message = verifyRes.message ?? "Invalid or expired OTP" });

            // 6) Update status + gửi mail consultant
            await _dealService.RejectDealAsync(dto.order_request_id, dto.reason ?? "Customer rejected");

            return Ok(new { ok = true });
        }


        [HttpGet("payos/return")]
        public async Task<IActionResult> PayOsReturn(
    [FromQuery(Name = "order_code")] long? orderCode,
    [FromQuery] string? status,
    [FromServices] IPaymentRepository paymentRepo,
    CancellationToken ct)
        {
            var fe = _config["Deal:BaseUrlFe"];

            try
            {
                long oc = orderCode ?? 0;

                // hỗ trợ backward compatibility nếu chỗ nào cũ vẫn gọi ?orderCode=
                if (oc <= 0 &&
                    long.TryParse(Request.Query["orderCode"], out var legacyOrderCode))
                {
                    oc = legacyOrderCode;
                }

                if (oc <= 0)
                    return Redirect($"{fe}/payment-result?payos=invalid_order_code");

                var payment = await paymentRepo.GetByOrderCodeAsync(oc, ct);
                if (payment == null)
                    return Redirect($"{fe}/payment-result?payos=payment_not_found&orderCode={oc}");

                var req = await _service.GetByIdAsync(payment.order_request_id);
                if (req == null)
                    return Redirect($"{fe}/payment-result?payos=request_not_found&orderCode={oc}");

                var paidByQuery =
                    string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase);

                PayOsResultDto? info = null;
                try
                {
                    info = await HttpContext.RequestServices
                        .GetRequiredService<IPayOsService>()
                        .GetPaymentLinkInformationAsync(oc, ct);
                }
                catch
                {
                }

                var paidByApi =
                    string.Equals(info?.status, "PAID", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(info?.status, "SUCCESS", StringComparison.OrdinalIgnoreCase);

                var isPaid = paidByApi || paidByQuery;

                if (isPaid)
                {
                    await ProcessPaidAsync(
                        orderRequestId: payment.order_request_id,
                        orderCode: oc,
                        amount: info?.amount ?? (long)payment.amount,
                        paymentLinkId: info?.payment_link_id ?? payment.payos_payment_link_id,
                        transactionId: info?.transaction_id ?? payment.payos_transaction_id,
                        rawJson: info?.raw_json ?? payment.payos_raw ?? "{}",
                        estimateIdFromQuery: payment.estimate_id,
                        quoteIdFromQuery: payment.quote_id,
                        paymentRepo: paymentRepo,
                        ct: ct);
                }

                if (string.Equals(payment.payment_type, "Remaining", StringComparison.OrdinalIgnoreCase) && req.order_id.HasValue)
                {
                    //return Redirect($"{fe}/payment/{req.order_id.Value}?payos={(isPaid ? "paid" : "pending")}&orderCode={oc}");
                    return Redirect($"{fe}/request-detail/{payment.order_request_id}?payos={(isPaid ? "paid" : "pending")}&orderCode={oc}");
                }

                return Redirect($"{fe}/request-detail/{payment.order_request_id}?payos={(isPaid ? "paid" : "pending")}&orderCode={oc}");
            }
            catch (Exception ex)
            {
                var msg = Uri.EscapeDataString(ex.Message ?? "unknown");
                return Redirect($"{fe}/payment-result?payos=error&message={msg}");
            }
        }

        [HttpGet("payos/cancel")]
        public IActionResult PayOsCancel([FromQuery] int orderRequestId, [FromQuery] long orderCode)
        {
            var fe = "https://sep490-fe.vercel.app";
            return Redirect($"{fe}");
        }

        [AllowAnonymous]
        [HttpPost("/api/payos/webhook")]
        public async Task<IActionResult> PayOsWebhook(
    [FromBody] JsonElement raw,
    [FromServices] IPaymentRepository paymentRepo,
    CancellationToken ct)
        {
            try
            {
                if (!raw.TryGetProperty("data", out var dataNode))
                    return Ok(new { ok = true, ignored = true, reason = "missing_data" });

                var rootCode = raw.TryGetProperty("code", out var rc) ? (rc.GetString() ?? "") : "";
                var rootSuccess = raw.TryGetProperty("success", out var rs) && rs.ValueKind == JsonValueKind.True;

                var dataCode = dataNode.TryGetProperty("code", out var dc) ? (dc.GetString() ?? "") : "";
                var dataStatus = dataNode.TryGetProperty("status", out var st) ? (st.GetString() ?? "") : "";
                var dataDesc = dataNode.TryGetProperty("desc", out var dd) ? (dd.GetString() ?? "") : "";

                var isPaid =
                    rootSuccess &&
                    string.Equals(rootCode, "00", StringComparison.OrdinalIgnoreCase) &&
                    (
                        string.Equals(dataCode, "00", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dataStatus, "PAID", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dataStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                        dataDesc.Contains("thành công", StringComparison.OrdinalIgnoreCase)
                    );

                if (!isPaid)
                    return Ok(new { ok = true, ignored = true, rootCode, dataCode, dataStatus, dataDesc });

                var checksumKey = _config["PayOS:ChecksumKey"];
                if (!string.IsNullOrWhiteSpace(checksumKey))
                {
                    var signature = raw.TryGetProperty("signature", out var sig) ? (sig.GetString() ?? "") : "";
                    if (!IsValidPayOsSignature(dataNode, signature, checksumKey))
                        return Ok(new { ok = true, ignored = true, reason = "invalid_signature" });
                }

                long orderCode =
                    dataNode.TryGetProperty("orderCode", out var oc) && oc.ValueKind == JsonValueKind.Number
                        ? oc.GetInt64()
                        : 0;

                long amount =
                    dataNode.TryGetProperty("amount", out var am) && am.ValueKind == JsonValueKind.Number
                        ? am.GetInt64()
                        : 0;

                var paymentLinkId = dataNode.TryGetProperty("paymentLinkId", out var pl) ? pl.GetString() : null;
                var transactionId = dataNode.TryGetProperty("reference", out var rf) ? rf.GetString() : null;

                if (orderCode <= 0)
                    return Ok(new { ok = true, ignored = true, reason = "invalid_orderCode" });

                var payment = await paymentRepo.GetByOrderCodeAsync(orderCode, ct);
                if (payment == null)
                    return Ok(new { ok = true, ignored = true, reason = "payment_not_found" });

                var (processed, message) = await ProcessPaidAsync(
                    payment.order_request_id,
                    orderCode,
                    amount,
                    paymentLinkId,
                    transactionId,
                    raw.ToString(),
                    payment.estimate_id,
                    payment.quote_id,
                    paymentRepo,
                    ct);

                if (!processed)
                {
                    _logger.LogError(
                        "PayOsWebhook processed=false. RequestId={RequestId}, OrderCode={OrderCode}, Message={Message}, Raw={Raw}",
                        payment.order_request_id, orderCode, message, raw.ToString());
                }

                return Ok(new
                {
                    ok = true,
                    processed,
                    message,
                    orderRequestId = payment.order_request_id,
                    orderCode
                });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = true, processed = false, error = ex.Message });
            }
        }

        private static bool IsValidPayOsSignature(JsonElement dataNode, string signature, string checksumKey)
        {
            var dict = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (var p in dataNode.EnumerateObject())
            {
                string val = p.Value.ValueKind switch
                {
                    JsonValueKind.Null => "",
                    JsonValueKind.Undefined => "",
                    JsonValueKind.String => p.Value.GetString() ?? "",
                    JsonValueKind.Number => p.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => p.Value.GetRawText()
                };

                if (val is "null" or "undefined") val = "";
                dict[p.Name] = val;
            }

            var dataStr = string.Join("&", dict.Select(kv => $"{kv.Key}={kv.Value}"));
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(checksumKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataStr));
            var hex = Convert.ToHexString(hash).ToLowerInvariant();

            return string.Equals(hex, signature ?? "", StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet("stats/email/accepted")]
        public async Task<IActionResult> GetEmailStatsByAccepted(
            [FromQuery] int page,
            [FromQuery] int pageSize,
            CancellationToken ct)
        {
            var result = await _service.GetEmailsByAcceptedCountPagedAsync(page, pageSize, ct);
            return Ok(result);
        }

        [HttpGet("design-file/{id:int}")]
        public async Task<IActionResult> GetDesignFile(int id, CancellationToken ct)
        {
            var result = await _service.GetDesignFileAsync(id, ct);
            if (result == null)
                return NotFound(new { message = "Order request not found" });

            return Ok(result);
        }
        private async Task<(bool ok, string message)> ProcessPaidAsync(
    int orderRequestId,
    long orderCode,
    long amount,
    string? paymentLinkId,
    string? transactionId,
    string rawJson,
    int? estimateIdFromQuery,
    int? quoteIdFromQuery,
    IPaymentRepository paymentRepo,
    CancellationToken ct)
        {
            var existingPayment = await paymentRepo.GetByOrderCodeAsync(orderCode, ct);

            var paymentType = existingPayment?.payment_type ?? "Deposit";

            if (string.Equals(paymentType, "Remaining", StringComparison.OrdinalIgnoreCase))
            {
                return await ProcessRemainingPaidAsync(
                    orderCode,
                    amount,
                    paymentLinkId,
                    transactionId,
                    rawJson,
                    paymentRepo,
                    ct);
            }

            return await ProcessDepositPaidAsync(
                orderRequestId,
                orderCode,
                amount,
                paymentLinkId,
                transactionId,
                rawJson,
                estimateIdFromQuery,
                quoteIdFromQuery,
                paymentRepo,
                ct);
        }

        private async Task<(bool ok, string message)> ProcessDepositPaidAsync(
    int orderRequestId,
    long orderCode,
    long amount,
    string? paymentLinkId,
    string? transactionId,
    string rawJson,
    int? estimateIdFromQuery,
    int? quoteIdFromQuery,
    IPaymentRepository paymentRepo,
    CancellationToken ct)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                var req = await _service.GetRequestForUpdateAsync(orderRequestId, ct);
                if (req == null)
                    return (false, $"order_request_id={orderRequestId} not found");

                var now = AppTime.NowVnUnspecified();

                var validation = ValidateQuotePaymentWindow(req, now);
                if (!validation.ok)
                {
                    if (!(req.order_id != null &&
                          string.Equals(req.process_status, "Accepted", StringComparison.OrdinalIgnoreCase)))
                    {
                        await tx.RollbackAsync(ct);
                        return (false, validation.message);
                    }
                }

                await UpsertPaidPaymentRowAsync(
                    orderRequestId, orderCode, amount,
                    paymentLinkId, transactionId, rawJson,
                    estimateIdFromQuery, quoteIdFromQuery,
                    paymentType: "Deposit",
                    paymentRepo: paymentRepo, ct: ct);

                var paidPayment = await _db.payments
                    .Where(p => p.provider == "PAYOS" && p.order_code == orderCode)
                    .OrderByDescending(p => p.payment_id)
                    .FirstOrDefaultAsync(ct);

                if (paidPayment == null)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Payment row not found");
                }

                var resolvedEstimateId =
                    (paidPayment.estimate_id.HasValue && paidPayment.estimate_id.Value > 0) ? paidPayment.estimate_id.Value :
                    (estimateIdFromQuery.HasValue && estimateIdFromQuery.Value > 0) ? estimateIdFromQuery.Value : 0;

                if (resolvedEstimateId <= 0)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Cannot resolve estimate_id for this payment/order_code");
                }

                var resolvedQuoteId =
                    (quoteIdFromQuery.HasValue && quoteIdFromQuery.Value > 0) ? quoteIdFromQuery.Value :
                    (paidPayment.quote_id.HasValue && paidPayment.quote_id.Value > 0) ? paidPayment.quote_id.Value : 0;

                var est = await _db.cost_estimates
                    .FirstOrDefaultAsync(x => x.estimate_id == resolvedEstimateId
                                           && x.order_request_id == orderRequestId, ct);

                if (est == null)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Cost estimate not found for paid payment");
                }

                if (req.accepted_estimate_id == null)
                    req.accepted_estimate_id = resolvedEstimateId;
                else if (req.accepted_estimate_id != resolvedEstimateId)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Request already accepted with a different estimate.");
                }

                if (resolvedQuoteId > 0)
                    req.quote_id = resolvedQuoteId;

                if (!string.Equals(req.process_status, "Accepted", StringComparison.OrdinalIgnoreCase))
                    req.process_status = "Accepted";

                await _db.cost_estimates
                    .Where(x => x.order_request_id == orderRequestId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.is_active, x => x.estimate_id == resolvedEstimateId), ct);

                if (resolvedQuoteId > 0)
                {
                    await _db.quotes
                        .Where(x => x.order_request_id == orderRequestId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.status,
                                x => x.quote_id == resolvedQuoteId ? "Accepted" : "Rejected"), ct);
                }

                await _db.SaveChangesAsync(ct);

                int orderId;

                if (req.order_id == null)
                {
                    var convert = await _service.ConvertToOrderInCurrentTransactionAsync(orderRequestId);
                    if (!convert.Success || convert.OrderId == null)
                    {
                        await tx.RollbackAsync(ct);
                        return (false, "ConvertToOrder failed: " + convert.Message);
                    }

                    orderId = convert.OrderId.Value;

                    req = await _service.GetRequestForUpdateAsync(orderRequestId, ct)
                        ?? throw new InvalidOperationException("Request disappeared after convert");
                }
                else
                {
                    orderId = req.order_id.Value;
                }

                var ord = await _db.orders.FirstOrDefaultAsync(x => x.order_id == orderId, ct);
                if (ord == null)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Order not found after convert");
                }

                if (!ord.layout_confirmed)
                {
                    ord.layout_confirmed = false;
                    ord.status = "LayoutPending";
                }

                ord.payment_status = "Paid";

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                try
                {
                    await _dealService.NotifyConsultantPaidAsync(orderRequestId, paidPayment.amount, now);
                    await _dealService.NotifyCustomerPaidAsync(orderRequestId, paidPayment.amount, now);
                }
                catch
                {
                }

                return (true, $"Deposit payment recorded successfully: order_id={orderId}");
            });
        }

        private async Task<(bool ok, string message)> ProcessRemainingPaidAsync(
    long orderCode,
    long amount,
    string? paymentLinkId,
    string? transactionId,
    string rawJson,
    IPaymentRepository paymentRepo,
    CancellationToken ct)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                var existing = await _db.payments
                    .FirstOrDefaultAsync(p => p.provider == "PAYOS" && p.order_code == orderCode, ct);

                if (existing == null)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Remaining payment row not found");
                }

                var now = AppTime.NowVnUnspecified();

                existing.status = "PAID";
                existing.payment_type = "Remaining";
                existing.paid_at ??= now;
                existing.updated_at = now;

                if (existing.amount <= 0 && amount > 0)
                    existing.amount = (decimal)amount;

                if (!string.IsNullOrWhiteSpace(paymentLinkId))
                    existing.payos_payment_link_id = paymentLinkId;

                if (!string.IsNullOrWhiteSpace(transactionId))
                    existing.payos_transaction_id = transactionId;

                if (!string.IsNullOrWhiteSpace(rawJson))
                    existing.payos_raw = rawJson;

                var req = await _service.GetRequestForUpdateAsync(existing.order_request_id, ct);
                if (req == null)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Order request not found for remaining payment");
                }

                if (!req.order_id.HasValue)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Order has not been created for remaining payment");
                }

                var ord = await _db.orders.FirstOrDefaultAsync(x => x.order_id == req.order_id.Value, ct);
                if (ord == null)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Order not found for remaining payment");
                }

                var prod = await _db.productions
                    .Where(x => x.order_id == ord.order_id)
                    .OrderByDescending(x => x.prod_id)
                    .FirstOrDefaultAsync(ct);

                req.process_status = "Paid";
                ord.status = "Paid";
                ord.payment_status = "Paid";

                if (prod != null)
                    prod.status = "Paid";

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return (true, $"Remaining payment recorded successfully: order_id={ord.order_id}");
            });
        }

        private async Task UpsertPaidPaymentRowAsync(
    int orderRequestId,
    long orderCode,
    long amount,
    string? paymentLinkId,
    string? transactionId,
    string rawJson,
    int? estimateIdFromQuery,
    int? quoteIdFromQuery,
    string paymentType,
    IPaymentRepository paymentRepo,
    CancellationToken ct)
        {
            var now = AppTime.NowVnUnspecified();

            var existing = await _db.payments
                .FirstOrDefaultAsync(p => p.provider == "PAYOS" && p.order_code == orderCode, ct);

            if (existing != null)
            {
                existing.status = "PAID";
                existing.payment_type = paymentType;
                existing.paid_at ??= now;

                if (existing.amount <= 0 && amount > 0)
                    existing.amount = (decimal)amount;

                if (!string.IsNullOrWhiteSpace(paymentLinkId))
                    existing.payos_payment_link_id = paymentLinkId;

                if (!string.IsNullOrWhiteSpace(transactionId))
                    existing.payos_transaction_id = transactionId;

                if (!string.IsNullOrWhiteSpace(rawJson))
                    existing.payos_raw = rawJson;

                if ((existing.estimate_id == null || existing.estimate_id <= 0) &&
                    estimateIdFromQuery.HasValue && estimateIdFromQuery.Value > 0)
                {
                    existing.estimate_id = estimateIdFromQuery.Value;
                }

                if ((existing.quote_id == null || existing.quote_id <= 0) &&
                    quoteIdFromQuery.HasValue && quoteIdFromQuery.Value > 0)
                {
                    existing.quote_id = quoteIdFromQuery.Value;
                }

                existing.updated_at = now;

                await _db.SaveChangesAsync(ct);
                return;
            }

            await paymentRepo.AddAsync(new payment
            {
                order_request_id = orderRequestId,
                provider = "PAYOS",
                payment_type = paymentType,
                order_code = orderCode,
                amount = (decimal)amount,
                currency = "VND",
                status = "PAID",
                estimate_id = (estimateIdFromQuery.HasValue && estimateIdFromQuery.Value > 0)
                    ? estimateIdFromQuery.Value
                    : (int?)null,
                quote_id = (quoteIdFromQuery.HasValue && quoteIdFromQuery.Value > 0)
                    ? quoteIdFromQuery.Value
                    : (int?)null,
                paid_at = now,
                payos_payment_link_id = paymentLinkId,
                payos_transaction_id = transactionId,
                payos_raw = rawJson,
                created_at = now,
                updated_at = now
            }, ct);

            await paymentRepo.SaveChangesAsync(ct);
        }
        
        [HttpGet("payos-deposit/{request_id:int}")]
        public async Task<IActionResult> GetPayOsDeposit(
    [FromRoute] int request_id,
    [FromQuery] int? quote_id,
    [FromQuery] int estimate_id,
    CancellationToken ct)
        {
            try
            {
                var req = await _service.GetByIdAsync(request_id);
                if (req == null)
                    return NotFound(new { message = "Request not found" });

                if (estimate_id <= 0)
                    return BadRequest(new { message = "estimate_id is required" });

                if (string.Equals(req.process_status, "Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        message = "Request has been rejected. Payment link is not available.",
                        request_id
                    });
                }

                var now = AppTime.NowVnUnspecified();
                var validation = ValidateQuotePaymentWindow(req, now);
                if (!validation.ok)
                {
                    return BadRequest(new
                    {
                        message = validation.message,
                        request_id,
                        verified_at = req.verified_at,
                        quote_expires_at = req.quote_expires_at
                    });
                }

                var est = await _db.cost_estimates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.estimate_id == estimate_id && x.order_request_id == request_id, ct);

                if (est == null)
                    return BadRequest(new { message = "Estimate not found for this request" });

                var quote = await _db.quotes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.quote_id == quote_id && x.order_request_id == request_id, ct);

                if (quote == null)
                    return BadRequest(new { message = "Quote not found" });

                var dto = await _dealService.CreateOrReuseDepositLinkAsync(request_id, estimate_id, quote_id, ct);
                dto.expired_at = req.quote_expires_at;
                dto.status ??= "PENDING";

                return Ok(dto);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("full-data-by-request_id/{request_id:int}")]
        [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetFullDataByRequestId(int request_id, CancellationToken ct)
        {
            var result = await _service.GetInformationRequestById(request_id, ct);
            if (result == null)
                return NotFound(new { message = "Order request not found" });

            return Ok(result);
        }

        [HttpPut("submit-estimate-for-approval")]
        public async Task<IActionResult> SubmitEstimateForApproval([FromBody] SubmitForApprovalRequestDto dto)
        {
            try
            {
                if (dto == null || dto.request_id <= 0)
                    return BadRequest(new { message = "request_id is required" });

                await _service.SubmitEstimateForApprovalAsync(dto);

                return Ok(new
                {
                    message = "Submitted for approval",
                    request_id = dto.request_id,
                    new_status = "Processing"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("get-deal-price-inProcess/{request_id:int}")]
        [ProducesResponseType(typeof(RequestWithTwoEstimatesDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCompareQuotes(int request_id, CancellationToken ct)
        {
            var dto = await _service.GetCompareQuotesAsync(request_id, ct);
            if (dto == null)
                return NotFound(new { message = "Order request not found" });

            return Ok(dto);
        }

        [HttpPut("consultant-message-to-customer")]
        public async Task<IActionResult> UpdateConsultantMessageToCustomer([FromBody] UpdateConsultantMessageToCustomerDto dto, CancellationToken ct)
        {
            try
            {
                if (dto == null)
                    return BadRequest(new { message = "Request body is required" });

                if (dto.request_id <= 0)
                    return BadRequest(new { message = "request_id must be greater than 0" });

                await _service.UpdateConsultantMessageToCustomerAsync(dto.request_id, dto.message, ct);

                return Ok(new
                {
                    message = "Updated consultant message to customer successfully",
                    request_id = dto.request_id
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An unexpected error occurred",
                    detail = ex.Message
                });
            }
        }

        private static bool IsPayableStatus(string? status)
        {
            return string.Equals(status, "Verified", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Waiting", StringComparison.OrdinalIgnoreCase);
        }

        private static (bool ok, string message) ValidateQuotePaymentWindow(order_request req, DateTime now)
        {
            if (!IsPayableStatus(req.process_status))
                return (false, "Only request with process_status is verified or waiting can start payment");

            if (!req.quote_expires_at.HasValue)
                return (false, "Quote expiry time has not been initialized");

            if (now > req.quote_expires_at.Value)
                return (false, $"Quote expired at {req.quote_expires_at:yyyy-MM-dd HH:mm:ss}");

            return (true, "");
        }

        [HttpPut("{id:int}/delivery-note")]
        public async Task<IActionResult> UpdateDeliveryNote(int id, [FromBody] UpdateDeliveryNoteRequest req, CancellationToken ct)
        {
            var ok = await _service.UpdateDeliveryNoteAsync(id, req.delivery_note, ct);

            if (!ok)
                return NotFound(new { message = "OrderRequest not found" });

            return NoContent();
        }
        [HttpPut("designer-confirm-layout")]
        public async Task<IActionResult> DesignerConfirmLayout(
    [FromBody] ConfirmLayoutRequestDto dto,
    CancellationToken ct)
        {
            try
            {
                if (dto == null || dto.request_id <= 0)
                    return BadRequest(new { message = "request_id is required" });

                var strategy = _db.Database.CreateExecutionStrategy();
                int orderId = 0;

                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database.BeginTransactionAsync(ct);

                    var req = await _service.GetRequestForUpdateAsync(dto.request_id, ct);
                    if (req == null)
                        throw new InvalidOperationException("Order request not found");

                    if (!req.order_id.HasValue || req.order_id.Value <= 0)
                        throw new InvalidOperationException("Order has not been created yet");

                    if (!string.Equals(req.process_status, "Accepted", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Only paid/accepted requests can be layout-confirmed");

                    orderId = req.order_id.Value;

                    var ord = await _db.orders.FirstOrDefaultAsync(x => x.order_id == orderId, ct);
                    if (ord == null)
                        throw new InvalidOperationException("Order not found");

                    if (!ord.layout_confirmed)
                    {
                        ord.layout_confirmed = true;

                        if (string.IsNullOrWhiteSpace(ord.status) ||
                            string.Equals(ord.status, "LayoutPending", StringComparison.OrdinalIgnoreCase))
                        {
                            ord.status = "Scheduled";
                        }

                        await _db.SaveChangesAsync(ct);
                    }

                    await tx.CommitAsync(ct);
                });

                await EnsureProductionAndTasksCreatedAsync(orderId, ct);

                return Ok(new
                {
                    ok = true,
                    request_id = dto.request_id,
                    order_id = orderId,
                    message = "Designer confirmed layout. Production flow released successfully."
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    ok = false,
                    message = ex.Message
                });
            }
        }

        private async Task EnsureProductionAndTasksCreatedAsync(int orderId, CancellationToken ct)
        {
            var req = await _db.order_requests
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.order_id == orderId, ct);

            if (req == null)
                throw new InvalidOperationException("Order request not found for this order");

            var ord = await _db.orders
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.order_id == orderId, ct);

            if (ord == null)
                throw new InvalidOperationException("Order not found");

            if (!ord.layout_confirmed)
                throw new InvalidOperationException("Layout has not been confirmed yet");

            var prod = await _db.productions
                .AsNoTracking()
                .Where(p => p.order_id == orderId && p.end_date == null)
                .OrderByDescending(p => p.prod_id)
                .FirstOrDefaultAsync(ct);

            var hasTasks = prod != null &&
                           await _db.tasks.AsNoTracking().AnyAsync(t => t.prod_id == prod.prod_id, ct);

            if (prod != null && hasTasks)
                return;

            var productTypeId = await _db.product_types
                .Where(x => x.code == req.product_type)
                .Select(x => x.product_type_id)
                .FirstOrDefaultAsync(ct);

            if (productTypeId <= 0)
                throw new InvalidOperationException("product_type invalid (cannot map product_type_id)");

            var item = await _db.order_items
                .AsNoTracking()
                .Where(x => x.order_id == orderId)
                .OrderBy(x => x.item_id)
                .FirstOrDefaultAsync(ct);

            await _schedulingService.ScheduleOrderAsync(
                orderId: orderId,
                productTypeId: productTypeId,
                productionProcessCsv: item?.production_process,
                managerId: 3
            );

            var trackedOrder = await _db.orders.FirstOrDefaultAsync(x => x.order_id == orderId, ct);
            if (trackedOrder != null &&
                string.Equals(trackedOrder.status, "LayoutPending", StringComparison.OrdinalIgnoreCase))
            {
                trackedOrder.status = "Scheduled";
                await _db.SaveChangesAsync(ct);
            }
        }
    }
}
