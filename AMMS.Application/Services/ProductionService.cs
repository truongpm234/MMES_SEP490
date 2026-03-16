using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Common;
using AMMS.Shared.DTOs.Enums;
using AMMS.Shared.DTOs.Productions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Services
{
    public class ProductionService : IProductionService
    {
        private readonly IProductionRepository _repo;

        public ProductionService(IProductionRepository repo)
        {
            _repo = repo;
        }
        public async Task<NearestDeliveryResponse> GetNearestDeliveryAsync()
        {
            var nearestDate = await _repo.GetNearestDeliveryDateAsync();

            var days = nearestDate == null
                ? 0
                : Math.Max(1, (nearestDate.Value.Date - DateTime.UtcNow.Date).Days);

            return new NearestDeliveryResponse
            {
                nearest_delivery_date = nearestDate,
                days_until_free = days
            };
        }

        public Task<List<string>> GetAllProcessTypeAsync()
        {
            var result = Enum.GetNames(typeof(ProcessType)).ToList();
            return Task.FromResult(result);
        }

        public Task<PagedResultLite<ProducingOrderCardDto>> GetProducingOrdersAsync(int page, int pageSize, int? roleId, CancellationToken ct = default)
           => _repo.GetProducingOrdersAsync(page, pageSize, roleId, ct);

        public async Task<ProductionProgressResponse> GetProgressAsync(int prodId)
        {
            var progress = await _repo.GetProgressAsync(prodId);
            return progress;
        }

        public async Task<ProductionDetailDto?> GetProductionDetailByOrderIdAsync(int orderId, CancellationToken ct = default)
        {
            return await _repo.GetProductionDetailByOrderIdAsync(orderId, ct);
        }

        public async Task<ProductionWasteReportDto?> GetProductionWasteAsync(int prodId, CancellationToken ct = default)
        {
            return await _repo.GetProductionWasteAsync(prodId, ct);
        }
        public async Task<bool> StartProductionByOrderIdAsync(int orderId, CancellationToken ct = default)
        {
            var now = AppTime.NowVnUnspecified();
            var prodId = await _repo.StartProductionByOrderIdAndPromoteFirstTaskAsync(orderId, now, ct);
            return prodId.HasValue;
        }
        public async Task<bool> SetProductionDeliveryAsync(int orderId, CancellationToken ct = default)
        {
            return await _repo.SetProductionDeliveryByOrderIdAsync(orderId, ct);
        }

        public async Task<int?> StartProductionAndPromoteFirstTaskAsync(int orderId, CancellationToken ct = default)
        {
            return await _repo.StartProductionByOrderIdAndPromoteFirstTaskAsync(orderId, AppTime.NowVnUnspecified(), ct);
        }
    }
}
