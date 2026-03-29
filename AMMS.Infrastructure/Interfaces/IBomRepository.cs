using AMMS.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Interfaces
{
    public interface IBomRepository
    {
        Task<List<bom>> GetAllAsync();
        Task<List<bom>> GetByIdAsync(int id);
        Task AddBomAsync(bom bom);
        Task SaveChangesAsync();
        Task<List<bom>> GetByOrderItemIdsAndEstimateIdAsync(List<int> orderItemIds, int estimateId, CancellationToken ct = default);
    }

}
