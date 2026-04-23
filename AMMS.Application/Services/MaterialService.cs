using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Boms;
using AMMS.Shared.DTOs.Common;
using AMMS.Shared.DTOs.Materials;
using AMMS.Shared.DTOs.Orders;

namespace AMMS.Application.Services
{
    public class MaterialService : IMaterialService
    {
        private readonly IMaterialRepository _materialRepository;
        private readonly ICostEstimateRepository _estimateRepo;
        private readonly IRequestRepository _reqRepo;
        public MaterialService(IMaterialRepository materialRepository, ICostEstimateRepository costEstimateRepository, IRequestRepository requestRepository)
        {
            _materialRepository = materialRepository;
            _estimateRepo = costEstimateRepository;
            _reqRepo = requestRepository;
        }

        public async Task<List<material>> GetAllAsync()
        {
            return await _materialRepository.GetAll();
        }

        public async Task<List<material>> GetMaterialByTypeMangAsync()
        {
            return await _materialRepository.GetMaterialByTypeMangAsync();
        }

        public async Task<material?> GetByIdAsync(int id)
        {
            return await _materialRepository.GetByIdAsync(id);
        }

        public async Task UpdateAsync(material material)
        {
            await _materialRepository.GetByIdAsync(material.material_id);
            await _materialRepository.UpdateAsync(material);
            await _materialRepository.SaveChangeAsync();
        }

        public Task<MaterialTypePaperDto> GetAllPaperTypeAsync()
        {
            var res = _materialRepository.GetAllPaperTypeAsync();
            return res;
        }

        public async Task<List<material>> GetMaterialByTypeSongAsync()
        {
            return await _materialRepository.GetMaterialByTypeSongAsync();
        }

        public async Task<MaterialTypeGlueDto> GetAllPhuGlueTypeAsync()
        {
            return await _materialRepository.GetAllPhuGlueTypeAsync();
        }

        public async Task<MaterialTypeGlueDto> GetAllBoiGlueTypeAsync()
        {
            return await _materialRepository.GetAllBoiGlueTypeAsync();
        }

        public async Task<MaterialTypeGlueDto> GetAllDanGlueTypeAsync()
        {
            return await _materialRepository.GetAllDanGlueTypeAsync();
        }
        public Task<PagedResultLite<MaterialShortageDto>> GetShortageForAllOrdersPagedAsync(
            int page, int pageSize, CancellationToken ct = default) =>
            _materialRepository.GetShortageForAllOrdersPagedAsync(page, pageSize, ct);
        public async Task<bool> IncreaseStockAsync(int materialId, decimal quantity)
        {
            if (quantity <= 0)
                throw new ArgumentException("Quantity phải lớn hơn 0.");

            return await _materialRepository.IncreaseStockAsync(materialId, quantity);
        }

        public async Task<bool> DecreaseStockAsync(int materialId, decimal quantity)
        {
            if (quantity <= 0)
                throw new ArgumentException("Quantity phải lớn hơn 0.");

            return await _materialRepository.DecreaseStockAsync(materialId, quantity);
        }

        public Task<PagedResultLite<MaterialStockAlertDto>> GetMaterialStockAlertsPagedAsync(
    int page, int pageSize, decimal nearMinThresholdPercent = 0.2m, CancellationToken ct = default) =>
    _materialRepository.GetMaterialStockAlertsPagedAsync(page, pageSize, nearMinThresholdPercent, ct);
        public async Task<OrderMaterialsResponse?> GetMaterialsByOrderIdAsync(int orderId, CancellationToken ct = default)
        {
            return await _materialRepository.GetMaterialsByOrderIdAsync(orderId, ct);
        }
    }
}
