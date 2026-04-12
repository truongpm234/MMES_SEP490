using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Infrastructure.Repositories
{
    public class EstimateConfigRepository : IEstimateConfigRepository
    {
        private readonly AppDbContext _db;

        public EstimateConfigRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<decimal?> GetNumberAsync(string configGroup, string configKey, CancellationToken ct = default)
        {
            return await _db.estimate_config
                .AsNoTracking()
                .Where(x => x.config_group == configGroup && x.config_key == configKey)
                .Select(x => x.value_num)
                .FirstOrDefaultAsync(ct);
        }
    }
}
