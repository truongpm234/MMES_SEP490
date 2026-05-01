using AMMS.Shared.DTOs.Products;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Interfaces
{
    public interface IProductReceiptService
    {
        Task<CreateProductReceiptResponse> CreateAsync(CreateProductReceiptDto dto, int? createdBy, CancellationToken ct = default);
    }
}
