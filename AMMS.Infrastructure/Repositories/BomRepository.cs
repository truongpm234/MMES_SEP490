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
    public class BomRepository : IBomRepository
    {
        private readonly AppDbContext _context;
        public BomRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<bom>> GetAllAsync()
        {
            return await _context.boms.ToListAsync();
        }

        public async Task<List<bom>> GetByIdAsync(int id)
        {
            return await _context.boms
                .Where(b => b.bom_id == id)
                .ToListAsync();
        }

        public async Task AddBomAsync(bom entity)
        {
            await _context.boms.AddAsync(entity);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }

        public async Task<List<bom>> GetByOrderItemIdsAndEstimateIdAsync(
            List<int> orderItemIds,
            int estimateId,
            CancellationToken ct = default)
        {
            if (orderItemIds == null || orderItemIds.Count == 0)
                return new List<bom>();

            return await _context.boms
                .Where(x => orderItemIds.Contains((int)x.order_item_id)
                         && x.source_estimate_id == estimateId)
                .ToListAsync(ct);
        }
    }
}
