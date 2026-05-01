using AMMS.Infrastructure.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Interfaces
{
    public interface IProductReceiptRepository
    {
        Task<string> GenerateNextCodeAsync(CancellationToken ct = default);
        Task AddReceiptAsync(product_receipt entity, CancellationToken ct = default);
        Task AddReceiptItemsAsync(IEnumerable<product_receipt_item> items, CancellationToken ct = default);
        Task<product?> GetProductByIdTrackingAsync(int productId, CancellationToken ct = default);
        Task<int> SaveChangesAsync(CancellationToken ct = default);
    }
}
