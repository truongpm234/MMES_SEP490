using AMMS.Infrastructure.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Interfaces
{
    public interface IProductService
    {
        Task<product> GetByIdAsync(int productId, CancellationToken ct = default);
        Task<List<product>> GetAllActiveAsync(CancellationToken ct = default);
    }
}
