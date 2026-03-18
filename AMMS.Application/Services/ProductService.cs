using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Services
{
    public class ProductService : IProductService
    {
        private readonly IProductRepository _repo;
        public ProductService(IProductRepository repo)
        {
            _repo = repo;
        }

        public async Task<product> GetByIdAsync(int productId, CancellationToken ct = default)
        {
            return await _repo.GetByIdAsync(productId, ct);
        }

        public async Task<List<product>> GetAllActiveAsync(CancellationToken ct = default) 
        {
            return await _repo.GetAllActiveAsync(ct);
        }
    }
}
