using AMMS.Infrastructure.Entities;

namespace AMMS.Infrastructure.Interfaces
{
    public interface IPaymentRepository
    {
        Task AddAsync(payment entity, CancellationToken ct = default);
        Task<payment?> GetPaidByProviderOrderCodeAsync(string provider, long orderCode, CancellationToken ct = default);
        Task<int> SaveChangesAsync(CancellationToken ct = default);
        Task<bool> IsPaidAsync(int orderRequestId, CancellationToken ct = default);
        Task<payment?> GetLatestByRequestIdAsync(int orderRequestId, CancellationToken ct = default);
        Task<payment?> GetLatestPendingByRequestIdAsync(int requestId, CancellationToken ct);
        Task UpsertPendingAsync(payment p, CancellationToken ct);
        Task<payment?> GetLatestPendingByRequestIdAndEstimateIdAsync(int requestId, int estimateId, CancellationToken ct = default);
        Task<payment?> GetByOrderCodeAsync(long orderCode, CancellationToken ct = default);
        Task<payment?> GetLatestByRequestIdAndEstimateIdAsync(int requestId, int estimateId, CancellationToken ct = default);
        Task<payment?> GetLatestPendingByRequestIdAndTypeAsync(int requestId, string paymentType, CancellationToken ct = default);
        Task<payment?> GetLatestByRequestIdAndTypeAsync(int requestId, string paymentType, CancellationToken ct = default);
    }
}
