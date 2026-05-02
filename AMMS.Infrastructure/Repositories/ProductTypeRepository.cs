using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.ProductTypes;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Infrastructure.Repositories
{
    public class ProductTypeRepository : IProductTypeRepository
    {
        private readonly AppDbContext _db;

        public ProductTypeRepository(AppDbContext db)
        {
            _db = db;
        }

        public Task<List<product_type>> GetAllAsync()
            => _db.product_types.AsNoTracking().ToListAsync();

        public Task<product_type?> GetByCodeAsync(string code)
            => _db.product_types.AsNoTracking().FirstOrDefaultAsync(x => x.code == code);

        public async Task<ProductTypeDetailDto?> GetProductTypeDetailAsync(int productTypeId, CancellationToken ct = default)
        {
            var pt = await _db.product_types
                .AsNoTracking()
                .Where(x => x.product_type_id == productTypeId)
                .Select(x => new { x.product_type_id, x.code, x.name, x.description, x.packaging_standard })
                .FirstOrDefaultAsync(ct);

            if (pt == null) return null;

            var templates = await _db.product_templates
                .AsNoTracking()
                .Where(t => t.product_type_id == productTypeId && t.is_active == true)
                .OrderBy(t => t.template_code)
                .Select(t => new ProductTemplateDto
                {
                    design_profile_id = t.design_profile_id,
                    template_code = t.template_code,
                    template_name = t.template_name,

                    product_length_mm = t.product_length_mm,
                    product_width_mm = t.product_width_mm,
                    product_height_mm = t.product_height_mm,
                    glue_tab_mm = t.glue_tab_mm,
                    bleed_mm = t.bleed_mm,

                    print_width_mm = t.print_width_mm,
                    print_length_mm = t.print_length_mm,

                    coating_type = t.coating_type,
                    wave_type = t.wave_type,
                    number_of_plates = t.number_of_plates,
                    is_active = t.is_active
                })
                .ToListAsync(ct);
            
            return new ProductTypeDetailDto
            {
                product_type_id = pt.product_type_id,
                code = pt.code,
                name = pt.name,
                description = pt.description,
                packaging_standard = pt.packaging_standard,
                templates = templates
            };
        }

        public async Task<int?> GetIdByCodeAsync(string code, CancellationToken ct = default)
        {
            code = (code ?? "").Trim();

            if (string.IsNullOrWhiteSpace(code))
                return null;

            return await _db.product_types
                .AsNoTracking()
                .Where(x => x.code == code)
                .Select(x => (int?)x.product_type_id)
                .FirstOrDefaultAsync(ct);
        }
    }
}
