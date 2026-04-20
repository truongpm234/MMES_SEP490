using AMMS.Infrastructure.Entities;
using AMMS.Shared.DTOs.Planning;
using AMMS.Shared.DTOs.ProductionCalendars;

namespace AMMS.Application.Interfaces
{
    public interface IProductionCalendarService
    {
        Task<bool> IsWorkingDayAsync(DateTime date, CancellationToken ct = default);
        Task<ProductionCalendarDto?> GetByDateAsync(DateTime date, CancellationToken ct = default);
        Task<List<production_calendar>> GetAllDate();
        Task<List<ProductionCalendarDto>> GetRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);

        Task CreateAsync(CreateProductionCalendarRequest dto, CancellationToken ct = default); // thêm
        Task<bool> UpdateAsync(DateTime date, UpdateProductionCalendarRequest dto, CancellationToken ct = default); // thêm
        Task<bool> UpdateNonWorkingDayAsync(DateTime date, bool isNonWorkingDay, CancellationToken ct = default); // thêm
        Task<bool> UpdateManualOverrideAsync(DateTime date, bool isManualOverride, CancellationToken ct = default); // thêm

        Task UpsertAsync(ProductionCalendarDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(DateTime date, CancellationToken ct = default);
    }
}
