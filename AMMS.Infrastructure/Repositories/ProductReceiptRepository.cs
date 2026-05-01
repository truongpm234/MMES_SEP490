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
    public class ProductReceiptRepository : IProductReceiptRepository
    {
        private readonly AppDbContext _db;

        public ProductReceiptRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<string> GenerateNextCodeAsync(CancellationToken ct = default)
        {
            var prefix = "PR";
            var today = DateTime.Now.ToString("yyyyMMdd");

            var lastCode = await _db.product_receipts
                .AsNoTracking()
                .Where(x => x.code.StartsWith(prefix + today))
                .OrderByDescending(x => x.code)
                .Select(x => x.code)
                .FirstOrDefaultAsync(ct);

            var nextNumber = 1;
            if (!string.IsNullOrWhiteSpace(lastCode) && lastCode.Length >= (prefix.Length + today.Length + 4))
            {
                var numPart = lastCode.Substring(prefix.Length + today.Length);
                if (int.TryParse(numPart, out var parsed))
                    nextNumber = parsed + 1;
            }

            return $"{prefix}{today}{nextNumber:D4}";
        }

        public async Task AddReceiptAsync(product_receipt entity, CancellationToken ct = default)
            => await _db.product_receipts.AddAsync(entity, ct);

        public async Task AddReceiptItemsAsync(IEnumerable<product_receipt_item> items, CancellationToken ct = default)
            => await _db.product_receipt_items.AddRangeAsync(items, ct);

        public Task<product?> GetProductByIdTrackingAsync(int productId, CancellationToken ct = default)
            => _db.products.FirstOrDefaultAsync(x => x.product_id == productId, ct);

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => _db.SaveChangesAsync(ct);
    }
}
