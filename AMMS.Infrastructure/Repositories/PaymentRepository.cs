using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Infrastructure.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly AppDbContext _db;
        public PaymentRepository(AppDbContext db) => _db = db;

        public Task AddAsync(payment entity, CancellationToken ct = default)
            => _db.payments.AddAsync(entity, ct).AsTask();

        public Task<payment?> GetPaidByProviderOrderCodeAsync(string provider, long orderCode, CancellationToken ct = default)
            => _db.payments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.provider == provider && x.order_code == orderCode && x.status == "PAID", ct);

        public Task<bool> IsPaidAsync(int orderRequestId, CancellationToken ct = default)
            => _db.payments.AsNoTracking()
                .AnyAsync(x => x.order_request_id == orderRequestId && x.status == "PAID", ct);

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => _db.SaveChangesAsync(ct);

        public Task<payment?> GetLatestByRequestIdAsync(int orderRequestId, CancellationToken ct = default)
        {
            return _db.payments
                .AsNoTracking()
                .Where(p => p.order_request_id == orderRequestId && p.provider == "PAYOS")
                .OrderByDescending(p => p.paid_at ?? DateTime.MinValue)
                .ThenByDescending(p => p.payment_id)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<payment?> GetLatestPendingByRequestIdAsync(int requestId, CancellationToken ct)
        {
            return await _db.payments
                .AsNoTracking()
                .Where(x =>
                    x.provider == "PAYOS" &&
                    x.order_request_id == requestId &&
                    x.payment_type == PaymentTypes.Deposit &&
                    x.status == "PENDING")
                .OrderByDescending(x => x.created_at)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<payment?> GetLatestPendingByRequestIdAndTypeAsync(int requestId, string paymentType, CancellationToken ct = default)
        {
            return await _db.payments
                .AsNoTracking()
                .Where(x =>
                    x.provider == "PAYOS" &&
                    x.order_request_id == requestId &&
                    x.payment_type == paymentType &&
                    x.status == "PENDING")
                .OrderByDescending(x => x.created_at)
                .ThenByDescending(x => x.payment_id)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<payment?> GetLatestByRequestIdAndTypeAsync(int requestId, string paymentType, CancellationToken ct = default)
        {
            return await _db.payments
                .AsNoTracking()
                .Where(x =>
                    x.provider == "PAYOS" &&
                    x.order_request_id == requestId &&
                    x.payment_type == paymentType)
                .OrderByDescending(x => x.paid_at ?? x.created_at)
                .ThenByDescending(x => x.payment_id)
                .FirstOrDefaultAsync(ct);
        }

        public async Task UpsertPendingAsync(payment p, CancellationToken ct)
        {
            var existing = await _db.payments
                .FirstOrDefaultAsync(x => x.provider == p.provider && x.order_code == p.order_code, ct);

            if (existing == null)
            {
                await _db.payments.AddAsync(p, ct);
            }
            else
            {
                existing.status = "PENDING";
                existing.amount = p.amount;
                existing.payment_type = p.payment_type;
                existing.payos_payment_link_id = p.payos_payment_link_id;
                existing.payos_transaction_id = p.payos_transaction_id;
                existing.payos_raw = p.payos_raw;
                existing.updated_at = p.updated_at;

                if ((existing.estimate_id == null || existing.estimate_id <= 0) && p.estimate_id > 0)
                    existing.estimate_id = p.estimate_id;

                if ((existing.quote_id == null || existing.quote_id <= 0) && p.quote_id > 0)
                    existing.quote_id = p.quote_id;
            }
        }

        public async Task<payment?> GetLatestPendingByRequestIdAndEstimateIdAsync(int requestId, int estimateId, CancellationToken ct = default)
        {
            return await _db.payments
                .AsNoTracking()
                .Where(p =>
                    p.order_request_id == requestId &&
                    p.estimate_id == estimateId &&
                    p.provider == "PAYOS" &&
                    p.payment_type == PaymentTypes.Deposit &&
                    p.status == "PENDING")
                .OrderByDescending(p => p.created_at)
                .ThenByDescending(p => p.payment_id)
                .FirstOrDefaultAsync(ct);
        }

        public Task<payment?> GetByOrderCodeAsync(long orderCode, CancellationToken ct = default)
        {
            return _db.payments.AsNoTracking()
                .Where(p => p.provider == "PAYOS" && p.order_code == orderCode)
                .OrderByDescending(p => p.payment_id)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<payment?> GetLatestByRequestIdAndEstimateIdAsync(
            int requestId,
            int estimateId,
            CancellationToken ct = default)
        {
            return await _db.payments
                .AsNoTracking()
                .Where(p =>
                    p.provider == "PAYOS" &&
                    p.order_request_id == requestId &&
                    p.estimate_id == estimateId &&
                    p.payment_type == PaymentTypes.Deposit)
                .OrderByDescending(p => p.payment_id)
                .FirstOrDefaultAsync(ct);
        }
    }
}