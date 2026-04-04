using AMMS.Infrastructure.Entities;
using AMMS.Shared.DTOs.Boms;
using AMMS.Shared.DTOs.Common;
using AMMS.Shared.DTOs.Materials;

namespace AMMS.Application.Interfaces
{
    public interface IMaterialService
    {
        Task<List<material>> GetAllAsync();
        Task<material?> GetByIdAsync(int id);
        Task UpdateAsync(material material);
        Task<MaterialTypePaperDto> GetAllPaperTypeAsync();
        Task<PagedResultLite<MaterialShortageDto>> GetShortageForAllOrdersPagedAsync(int page, int pageSize, CancellationToken ct = default);
        Task<List<material>> GetMaterialByTypeSongAsync();
        Task<MaterialTypeGlueDto> GetAllBoiGlueTypeAsync();
        Task<MaterialTypeGlueDto> GetAllDanGlueTypeAsync();
        Task<MaterialTypeGlueDto> GetAllPhuGlueTypeAsync();
        Task<bool> IncreaseStockAsync(int materialId, decimal quantity);
        Task<bool> DecreaseStockAsync(int materialId, decimal quantity);
    }
}
