using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Common;
using AMMS.Shared.DTOs.SubProduct;
using AMMS.Shared.Helpers;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Services
{
    public class SubProductService : ISubProductService
    {
        private readonly ISubProductRepository _repo;

        public SubProductService(ISubProductRepository repo)
        {
            _repo = repo;
        }

        public async Task<SubProductDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var entity = await _repo.GetByIdAsync(id, ct);
            if (entity == null) return null;

            return new SubProductDto
            {
                id = entity.id,
                product_type_id = entity.product_type_id,
                product_type_name = entity.product_type?.name,
                width = entity.width,
                length = entity.length,
                product_process = entity.product_process,
                quantity = entity.quantity,
                is_active = entity.is_active,
                description = entity.description,
                updated_at = entity.updated_at
            };
        }

        public Task<PagedResultLite<SubProductDto>> GetPagedAsync(int page, int pageSize, bool? isActive = null, CancellationToken ct = default)
            => _repo.GetPagedAsync(page, pageSize, isActive, ct);

        public async Task<UpdateSubProductResponse> UpdateAsync(int id, UpdateSubProductDto dto, CancellationToken ct = default)
        {
            var entity = await _repo.GetByIdTrackingAsync(id, ct);
            if (entity == null)
            {
                return new UpdateSubProductResponse
                {
                    success = false,
                    message = "Sub product not found",
                    id = id
                };
            }

            if (dto.product_type_id.HasValue)
            {
                if (dto.product_type_id.Value <= 0)
                    throw new ArgumentException("product_type_id must be > 0");

                var productTypeExists = await _repo.ProductTypeExistsAsync(dto.product_type_id.Value, ct);
                if (!productTypeExists)
                    throw new ArgumentException($"product_type_id {dto.product_type_id.Value} not found");

                entity.product_type_id = dto.product_type_id.Value;
            }

            if (dto.width.HasValue)
            {
                if (dto.width.Value < 0)
                    throw new ArgumentException("width must be >= 0");

                entity.width = dto.width.Value;
            }

            if (dto.length.HasValue)
            {
                if (dto.length.Value < 0)
                    throw new ArgumentException("length must be >= 0");

                entity.length = dto.length.Value;
            }

            if (dto.quantity.HasValue)
            {
                if (dto.quantity.Value < 0)
                    throw new ArgumentException("quantity must be >= 0");

                entity.quantity = dto.quantity.Value;
            }

            if (dto.product_process != null)
                entity.product_process = string.IsNullOrWhiteSpace(dto.product_process)
                    ? null
                    : dto.product_process.Trim();

            if (dto.description != null)
                entity.description = string.IsNullOrWhiteSpace(dto.description)
                    ? null
                    : dto.description.Trim();

            if (dto.is_active.HasValue)
                entity.is_active = dto.is_active.Value;

            entity.updated_at = AppTime.NowVnUnspecified();

            await _repo.SaveChangesAsync(ct);

            return new UpdateSubProductResponse
            {
                success = true,
                message = "Sub product updated successfully",
                id = entity.id,
                updated_at = entity.updated_at
            };
        }

        public async Task<CreateSubProductResponse> CreateAsync(CreateSubProductDto dto, CancellationToken ct = default)
        {
            if (dto.product_type_id <= 0)
                throw new ArgumentException("product_type_id is required");

            var productTypeExists = await _repo.ProductTypeExistsAsync(dto.product_type_id, ct);
            if (!productTypeExists)
                throw new ArgumentException($"product_type_id {dto.product_type_id} not found");

            if (dto.width.HasValue && dto.width.Value < 0)
                throw new ArgumentException("width must be >= 0");

            if (dto.length.HasValue && dto.length.Value < 0)
                throw new ArgumentException("length must be >= 0");

            if (dto.quantity.HasValue && dto.quantity.Value < 0)
                throw new ArgumentException("quantity must be >= 0");

            var entity = new sub_product
            {
                product_type_id = dto.product_type_id,
                width = dto.width,
                length = dto.length,
                product_process = string.IsNullOrWhiteSpace(dto.product_process) ? null : dto.product_process.Trim(),
                quantity = dto.quantity ?? 0,
                is_active = dto.is_active ?? true,
                description = string.IsNullOrWhiteSpace(dto.description) ? null : dto.description.Trim(),
                updated_at = AppTime.NowVnUnspecified()
            };

            await _repo.AddAsync(entity, ct);
            await _repo.SaveChangesAsync(ct);

            return new CreateSubProductResponse
            {
                success = true,
                message = "Sub product created successfully",
                id = entity.id
            };
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var entity = await _repo.GetByIdTrackingAsync(id, ct);
            if (entity == null)
                return false;

            entity.is_active = false;
            entity.updated_at = AppTime.NowVnUnspecified();

            await _repo.SaveChangesAsync(ct);
            return true;
        }
    }
}
