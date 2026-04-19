using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AMMS.Infrastructure.Entities;

namespace AMMS.Infrastructure.Interfaces
{
    public interface IProductionCalendarRepository
    {
        Task<production_calendar?> GetByDateAsync(DateTime date, CancellationToken ct = default);
        Task<production_calendar?> GetByDateTrackingAsync(DateTime date, CancellationToken ct = default); // thêm
        Task<List<production_calendar>> GetRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
        Task AddAsync(production_calendar entity, CancellationToken ct = default); // thêm
        Task UpsertAsync(production_calendar entity, CancellationToken ct = default);
        Task<bool> DeleteByDateAsync(DateTime date, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct = default);
    }
}