using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Services
{
    public class PaymentsService : IPaymentsService
    {
        private readonly AppDbContext _db;
        private readonly IRequestService _requestService;
        private readonly IDealService _dealService;
        private readonly IPaymentRepository _paymentRepo;
        private readonly ILogger<PaymentsService> _logger;
        public PaymentsService(
            AppDbContext db,
            IRequestService requestService,
            IDealService dealService,
            IPaymentRepository paymentRepo,
            ILogger<PaymentsService> logger)
        {
            _db = db;
            _requestService = requestService;
            _dealService = dealService;
            _paymentRepo = paymentRepo;
            _logger = logger;
        }

        public Task<payment?> GetPaidByProviderOrderCodeAsync(string provider, long orderCode, CancellationToken ct = default)
        {
            return _paymentRepo.GetPaidByProviderOrderCodeAsync(provider, orderCode, ct);
        }

        public Task<payment?> GetLatestByRequestIdAsync(int orderRequestId, CancellationToken ct = default)
        {
            return _paymentRepo.GetLatestByRequestIdAsync(orderRequestId, ct);
        }

        public async Task<payment?> GetLatestPendingByRequestIdAsync(int requestId, CancellationToken ct)
        {
            return await _paymentRepo.GetLatestPendingByRequestIdAsync(requestId, ct);
        }

        public async Task UpsertPendingAsync(payment p, CancellationToken ct)
        {
            await _paymentRepo.UpsertPendingAsync(p, ct);
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            return _paymentRepo.SaveChangesAsync(ct);
        }

        public async Task<payment?> GetLatestPendingByRequestIdAndEstimateIdAsync(int requestId, int estimateId, CancellationToken ct = default)
        {
            return await _paymentRepo.GetLatestPendingByRequestIdAndEstimateIdAsync(requestId, estimateId, ct);
        }

        public async Task<payment?> GetByOrderCodeAsync(long orderCode, CancellationToken ct = default)
        {
            return await _paymentRepo.GetByOrderCodeAsync(orderCode, ct);
        }

        public async Task<payment?> GetLatestByRequestIdAndEstimateIdAsync(int requestId, int estimateId, CancellationToken ct = default)
        {
            return await _paymentRepo.GetLatestByRequestIdAndEstimateIdAsync(requestId, estimateId, ct);
        }

        public async Task<payment?> GetLatestPendingByRequestIdAndTypeAsync(int requestId, string paymentType, CancellationToken ct = default)
        {
            return await _paymentRepo.GetLatestPendingByRequestIdAndTypeAsync(requestId, paymentType, ct);
        }

        public async Task<payment?> GetLatestByRequestIdAndTypeAsync(int requestId, string paymentType, CancellationToken ct = default)
        {
            return await _paymentRepo.GetLatestByRequestIdAndTypeAsync(requestId, paymentType, ct);
        }

        public async Task<(bool ok, string message)> ProcessPaidAsync(
            int orderRequestId,
            long orderCode,
            long amount,
            string? paymentLinkId,
            string? transactionId,
            string rawJson,
            int? estimateIdFromQuery,
            int? quoteIdFromQuery,
            CancellationToken ct = default)
        {
            var existingPayment = await _paymentRepo.GetByOrderCodeAsync(orderCode, ct);
            var paymentType = existingPayment?.payment_type ?? "Deposit";

            if (string.Equals(paymentType, "Remaining", StringComparison.OrdinalIgnoreCase))
            {
                return await ProcessRemainingPaidAsync(
                    orderCode,
                    amount,
                    paymentLinkId,
                    transactionId,
                    rawJson,
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
                ct);
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

        private async Task<(bool ok, string message)> ProcessDepositPaidAsync(
            int orderRequestId,
            long orderCode,
            long amount,
            string? paymentLinkId,
            string? transactionId,
            string rawJson,
            int? estimateIdFromQuery,
            int? quoteIdFromQuery,
            CancellationToken ct)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                var req = await _db.order_requests
                    .FirstOrDefaultAsync(x => x.order_request_id == orderRequestId, ct);

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

                var paymentBefore = await _db.payments
                    .FirstOrDefaultAsync(p => p.provider == "PAYOS" && p.order_code == orderCode, ct);

                var shouldSendPaidEmail = paymentBefore == null ||
                                          !string.Equals(paymentBefore.status, "PAID", StringComparison.OrdinalIgnoreCase);

                var currentPayment = await _db.payments
                    .AsNoTracking()
                    .Where(p => p.provider == "PAYOS" && p.order_code == orderCode)
                    .OrderByDescending(p => p.payment_id)
                    .FirstOrDefaultAsync(ct);

                var resolvedEstimateId =
                    (currentPayment?.estimate_id).GetValueOrDefault() > 0
                        ? currentPayment!.estimate_id!.Value
                        : (estimateIdFromQuery.GetValueOrDefault() > 0 ? estimateIdFromQuery!.Value : 0);

                if (resolvedEstimateId <= 0)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Cannot resolve estimate_id for this payment/order_code");
                }

                var resolvedQuoteId =
                    (currentPayment?.quote_id).GetValueOrDefault() > 0
                        ? currentPayment!.quote_id!.Value
                        : (quoteIdFromQuery.GetValueOrDefault() > 0 ? quoteIdFromQuery!.Value : 0);

                if (resolvedQuoteId <= 0)
                {
                    resolvedQuoteId = await _db.quotes
                        .AsNoTracking()
                        .Where(x => x.order_request_id == orderRequestId && x.estimate_id == resolvedEstimateId)
                        .OrderByDescending(x => x.quote_id)
                        .Select(x => (int?)x.quote_id)
                        .FirstOrDefaultAsync(ct) ?? 0;
                }

                var est = await _db.cost_estimates
                    .FirstOrDefaultAsync(x => x.estimate_id == resolvedEstimateId
                                           && x.order_request_id == orderRequestId, ct);

                if (est == null)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Cost estimate not found for paid payment");
                }

                var wasAccepted = string.Equals(req.process_status, "Accepted", StringComparison.OrdinalIgnoreCase);

                if (!wasAccepted)
                    req.process_status = "Accepted";

                if (req.accepted_estimate_id == null)
                    req.accepted_estimate_id = resolvedEstimateId;
                else if (req.accepted_estimate_id != resolvedEstimateId)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Request already accepted with a different estimate.");
                }

                if (resolvedQuoteId > 0)
                    req.quote_id = resolvedQuoteId;

                await _db.SaveChangesAsync(ct);

                await UpsertPaidPaymentRowAsync(
                    orderRequestId,
                    orderCode,
                    amount,
                    paymentLinkId,
                    transactionId,
                    rawJson,
                    resolvedEstimateId,
                    resolvedQuoteId > 0 ? resolvedQuoteId : null,
                    paymentType: "Deposit",
                    ct: ct);

                var paidPayment = await _db.payments
                    .Where(p => p.provider == "PAYOS" && p.order_code == orderCode)
                    .OrderByDescending(p => p.payment_id)
                    .FirstOrDefaultAsync(ct);

                if (paidPayment == null)
                {
                    await tx.RollbackAsync(ct);
                    return (false, "Payment row not found");
                }

                if (!string.Equals(paidPayment.status, "PAID", StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync(ct);
                    return (false, $"Payment status was not updated to PAID. Current status = {paidPayment.status}");
                }

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

                if (!req.order_id.HasValue)
                {
                    var convert = await _requestService.ConvertToOrderInCurrentTransactionAsync(orderRequestId);
                    if (!convert.Success || !convert.OrderId.HasValue)
                    {
                        await tx.RollbackAsync(ct);
                        return (false, convert.Message ?? "Convert to order failed");
                    }

                    req.order_id = convert.OrderId.Value;
                }

                if (req.order_id.HasValue)
                {
                    var ord = await _db.orders.FirstOrDefaultAsync(x => x.order_id == req.order_id.Value, ct);
                    if (ord == null)
                    {
                        await tx.RollbackAsync(ct);
                        return (false, "Order not found");
                    }

                    ord.payment_status = "Deposited";

                    if (!ord.layout_confirmed &&
                        string.IsNullOrWhiteSpace(ord.status))
                    {
                        ord.status = "LayoutPending";
                    }
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                if (shouldSendPaidEmail)
                {
                    try
                    {
                        await _dealService.NotifyConsultantPaidAsync(orderRequestId, paidPayment.amount, now);
                        await _dealService.NotifyCustomerPaidAsync(orderRequestId, paidPayment.amount, now);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Send paid email failed. RequestId={RequestId}, OrderCode={OrderCode}",
                            orderRequestId, orderCode);
                    }
                }

                return (true, "Deposit payment recorded successfully. Request is Accepted.");
            });
        }

        private async Task<(bool ok, string message)> ProcessRemainingPaidAsync(
            long orderCode,
            long amount,
            string? paymentLinkId,
            string? transactionId,
            string rawJson,
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

                var req = await _requestService.GetRequestForUpdateAsync(existing.order_request_id, ct);
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

            var newPayment = new payment
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
            };

            await _db.payments.AddAsync(newPayment, ct);
            await _db.SaveChangesAsync(ct);
        }
    }
}
