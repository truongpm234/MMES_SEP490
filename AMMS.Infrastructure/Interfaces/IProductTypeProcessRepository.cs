using AMMS.Infrastructure.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Interfaces
{
    public interface IProductTypeProcessRepository
    {
        Task<List<product_type_process>> GetActiveByProductTypeIdAsync(int productTypeId);
        Task DeleteAllByProductTypeIdAsync(int productTypeId);
        Task AddRangeAsync(IEnumerable<product_type_process> items);
        Task SaveChangesAsync();
        Task<List<product_type_process>> GetActiveByProductTypeIdAsync(int productTypeId, CancellationToken ct = default);
    }
}
