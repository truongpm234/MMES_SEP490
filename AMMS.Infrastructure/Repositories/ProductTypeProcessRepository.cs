using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Repositories
{
    public class ProductTypeProcessRepository : IProductTypeProcessRepository
    {
        private readonly AppDbContext _db;
        public ProductTypeProcessRepository(AppDbContext db) => _db = db;

        public Task<List<product_type_process>> GetActiveByProductTypeIdAsync(int productTypeId)
            => _db.product_type_processes
                .Where(x => x.product_type_id == productTypeId && (x.is_active ?? true))
                .OrderBy(x => x.seq_num)
                .ToListAsync();

        public async Task DeleteAllByProductTypeIdAsync(int productTypeId)
        {
            var old = await _db.product_type_processes
                .Where(x => x.product_type_id == productTypeId)
                .ToListAsync();

            _db.product_type_processes.RemoveRange(old);
        }

        public Task AddRangeAsync(IEnumerable<product_type_process> items)
        {
            _db.product_type_processes.AddRange(items);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync() => _db.SaveChangesAsync();

        public async Task<List<product_type_process>> GetActiveByProductTypeIdAsync(int productTypeId, CancellationToken ct = default)
        {
            return await _db.product_type_processes
                .AsNoTracking()
                .Where(x => x.product_type_id == productTypeId && (x.is_active ?? true))
                .OrderBy(x => x.seq_num)
                .ToListAsync(ct);
        }
    }

}
