using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Products;
using AMMS.Shared.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Services
{
    public class ProductReceiptService : IProductReceiptService
    {
        private readonly IProductReceiptRepository _repo;

        public ProductReceiptService(IProductReceiptRepository repo)
        {
            _repo = repo;
        }

        public async Task<CreateProductReceiptResponse> CreateAsync(CreateProductReceiptDto dto, int? createdBy, CancellationToken ct = default)
        {
            if (dto == null)
                throw new ArgumentException("Payload is required");

            if (dto.items == null || dto.items.Count == 0)
                throw new ArgumentException("Phiếu nhập kho phải có ít nhất 1 sản phẩm");

            foreach (var item in dto.items)
            {
                if (item.product_id <= 0)
                    throw new ArgumentException("product_id phải lớn hơn 0");

                if (item.qty_received <= 0)
                    throw new ArgumentException("qty_received phải lớn hơn 0");
            }

            var now = AppTime.NowVnUnspecified();
            var code = await _repo.GenerateNextCodeAsync(ct);

            var receipt = new product_receipt
            {
                code = code,
                created_at = now,
                created_by = createdBy,
                note = string.IsNullOrWhiteSpace(dto.note) ? null : dto.note.Trim()
            };

            await _repo.AddReceiptAsync(receipt, ct);
            await _repo.SaveChangesAsync(ct);

            var items = new List<product_receipt_item>();

            foreach (var item in dto.items)
            {
                var product = await _repo.GetProductByIdTrackingAsync(item.product_id, ct);
                if (product == null)
                    throw new ArgumentException($"Không tìm thấy product_id = {item.product_id}");

                product.stock_qty += item.qty_received;
                product.updated_at = now;

                items.Add(new product_receipt_item
                {
                    receipt_id = receipt.receipt_id,
                    product_id = item.product_id,
                    qty_received = item.qty_received,
                    note = string.IsNullOrWhiteSpace(item.note) ? null : item.note.Trim()
                });
            }

            await _repo.AddReceiptItemsAsync(items, ct);
            await _repo.SaveChangesAsync(ct);

            return new CreateProductReceiptResponse
            {
                success = true,
                message = "Tạo phiếu nhập kho thành phẩm thành công",
                receipt_id = receipt.receipt_id,
                code = receipt.code,
                created_at = receipt.created_at
            };
        }
    }
}
