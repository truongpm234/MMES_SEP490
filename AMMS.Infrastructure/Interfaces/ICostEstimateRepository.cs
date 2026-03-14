using AMMS.Infrastructure.Entities;
using AMMS.Shared.DTOs.Estimates;
using AMMS.Shared.DTOs.Requests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Interfaces
{
    public interface ICostEstimateRepository
    {
        Task AddAsync(cost_estimate entity);
        Task SaveChangesAsync();
        Task<cost_estimate?> GetByOrderRequestIdAsync(int orderRequestId);
        Task<cost_estimate?> GetByIdAsync(int id);
        Task UpdateAsync(cost_estimate entity);
        Task<order_request?> GetOrderRequestTrackingAsync(int orderRequestId, CancellationToken ct = default);
        Task<DepositByRequestResponse?> GetDepositByRequestIdAsync(int requestId, CancellationToken ct = default);
        Task<bool> OrderRequestExistsAsync(int order_request_id);
        Task<List<RequestEstimateDto>> GetAllEstimatesFlatByRequestIdAsync(int requestId, CancellationToken ct = default);
        Task<List<cost_estimate>> GetAllByOrderRequestIdAsync(int orderRequestId, CancellationToken ct = default);
        Task<int> DeactivateAllByRequestIdAsync(int orderRequestId, CancellationToken ct = default);
        Task NormalizeActiveDraftEstimatesAsync(int orderRequestId, int currentEstimateId, CancellationToken ct = default);
        Task<cost_estimate?> GetTrackingByIdAsync(int estimateId, CancellationToken ct = default);
        Task<bool> EstimateBelongsToRequestAsync(int estimateId, int requestId, CancellationToken ct = default);
    }
}

