using AMMS.Infrastructure.Entities;
using AMMS.Shared.DTOs.Common;
using AMMS.Shared.DTOs.Requests;

namespace AMMS.Application.Interfaces
{
    public interface IRequestService
    {
        Task<CreateRequestResponse> CreateAsync(CreateResquest req);
        Task<UpdateRequestResponse> UpdateAsync(int id, UpdateOrderRequest req);
        Task CancelAsync(int id, string? reason, CancellationToken ct = default);
        Task<order_request?> GetByIdAsync(int id);
        Task<PagedResultLite<RequestPagedDto>> GetPagedAsync(int page, int pageSize);
        Task<RequestPagedDto?> GetByOrderIdAsync(int orderId);
        Task<int> DeleteDesignFilePathByRequestIdAsync(int orderRequestId, CancellationToken ct = default);
        Task<ConvertRequestToOrderResponse> ConvertToOrderAsync(int requestId);
        Task<PagedResultLite<RequestSortedDto>> GetSortedByQuantityPagedAsync(
    bool ascending, int page, int pageSize, CancellationToken ct = default);
        Task<PagedResultLite<RequestSortedDto>> GetSortedByDatePagedAsync(
    bool ascending, int page, int pageSize, CancellationToken ct = default);
        Task<PagedResultLite<RequestSortedDto>> GetSortedByDeliveryDatePagedAsync(
    bool nearestFirst, int page, int pageSize, CancellationToken ct = default);
        Task<PagedResultLite<RequestEmailStatsDto>> GetEmailsByAcceptedCountPagedAsync(
   int page, int pageSize, CancellationToken ct = default);
        Task<PagedResultLite<RequestStockCoverageDto>> GetSortedByStockCoveragePagedAsync(
    int page, int pageSize, CancellationToken ct = default);
        Task<PagedResultLite<RequestSortedDto>> GetByOrderRequestDatePagedAsync(
    DateOnly date, int page, int pageSize, CancellationToken ct = default);
        Task<PagedResultLite<RequestSortedDto>> SearchPagedAsync(
    string keyword, int page, int pageSize, CancellationToken ct = default);
        Task<int> CreateOrderRequestAsync(CreateOrderRequestDto dto, CancellationToken ct = default);
        Task<OrderRequestDesignFileResponse?> GetDesignFileAsync(int orderRequestId, CancellationToken ct = default);
        Task UpdateDesignFilePathAsync(int orderRequestId, string designFilePath, CancellationToken ct = default);
        Task<CreateRequestResponse> CreateRequestByConsultantAsync(CreateResquestConsultant req);
        Task<RequestDetailDto?> GetInformationRequestById(int requestId, CancellationToken ct = default);
        Task<RequestWithCostDto?> GetByIdWithCostAsync(int id);
        Task UpdateApprovalAsync(RequestApprovalUpdateDto dto, CancellationToken ct = default);
        Task SubmitEstimateForApprovalAsync(SubmitForApprovalRequestDto input);
        Task<RequestWithTwoEstimatesDto?> GetCompareQuotesAsync(int requestId, CancellationToken ct = default);
        Task<CloneRequestResponseDto> CloneRequestAsync(int requestId, CancellationToken ct = default);
        Task UpdateConsultantMessageToCustomerAsync(int requestId, string? message, CancellationToken ct = default);
        Task<order_request?> GetRequestForUpdateAsync(int orderRequestId, CancellationToken ct);
        Task<ConvertRequestToOrderResponse> ConvertToOrderInCurrentTransactionAsync(int requestId);
        Task<int?> GetConsultantScopeUserIdAsync(CancellationToken ct = default);
        Task EnsureCanAccessAssignedRequestAsync(int requestId, CancellationToken ct = default);
        Task<bool> UpdateDeliveryNoteAsync(int orderId, string note, CancellationToken ct = default);
        Task<DateTime?> CalculateAsync(int orderRequestId, CancellationToken ct = default);
        Task<DateTime?> RecalculateAndPersistAsync(int orderRequestId, CancellationToken ct = default);
        Task<string> UploadPrintReadyFileAsync(int requestId, int? estimateId, Stream fileStream, string fileName, string? contentType, CancellationToken ct = default);
        void QueueRelease(int orderId);
        Task ExecuteAsync(int orderId, CancellationToken ct = default);
    }
}
