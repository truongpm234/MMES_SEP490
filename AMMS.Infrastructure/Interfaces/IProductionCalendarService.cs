using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AMMS.Shared.DTOs.Planning;

namespace AMMS.Application.Interfaces
{
    public interface IProductionCalendarService
    {
        Task<bool> IsWorkingDayAsync(DateTime date, CancellationToken ct = default);
        Task<ProductionCalendarDto?> GetByDateAsync(DateTime date, CancellationToken ct = default);
        Task<List<ProductionCalendarDto>> GetRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
        Task UpsertAsync(ProductionCalendarDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(DateTime date, CancellationToken ct = default);
    }
}
