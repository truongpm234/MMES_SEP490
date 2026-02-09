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
    public class ProductRepository : IProductRepository
    {
        private readonly AppDbContext _db;

        public ProductRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<product> GetByIdAsync(int productId, CancellationToken ct = default)
        {
            return await _db.products.Where(p => p.product_id == productId).FirstOrDefaultAsync(ct);
        }

        public async Task<List<product>> GetAllActiveAsync(CancellationToken ct = default)
        {
            return await _db.products
                .Where(p => p.is_active == true)
                .OrderBy(p => p.name)
                .ToListAsync(ct);
        }
    }
}
