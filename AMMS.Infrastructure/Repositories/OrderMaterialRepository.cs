using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Orders;
using AMMS.Shared.Helpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Repositories
{
    public class OrderMaterialRepository : IOrderMaterialRepository
    {
        private readonly AppDbContext _db;
        public OrderMaterialRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<OrderMaterialsResponse?> GetMaterialsByOrderIdAsync(int orderId, CancellationToken ct = default)
        {
            var header = await (
                from o in _db.orders.AsNoTracking()
                join q in _db.quotes.AsNoTracking()
                    on o.quote_id equals q.quote_id into qj
                from q in qj.DefaultIfEmpty()

                join r in _db.order_requests.AsNoTracking()
                    on q.order_request_id equals r.order_request_id into rj
                from r in rj.DefaultIfEmpty()

                join ce in _db.cost_estimates.AsNoTracking().Where(x => x.is_active)
                    on r.order_request_id equals ce.order_request_id into cej
                from ce in cej
                    .OrderByDescending(x => x.estimate_id)
                    .Take(1)
                    .DefaultIfEmpty()

                where o.order_id == orderId
                select new { o, r, ce }
            ).FirstOrDefaultAsync(ct);

            if (header == null || header.r == null || header.ce == null)
                return null;

            var o1 = header.o;
            var r1 = header.r;
            var ce1 = header.ce;

            var paperCode = ce1.paper_code;
            var displayPaperCode = EstimateMaterialAlternativeHelper.ResolvePaperCode(
                ce1.paper_alternative,
                ce1.paper_code);

            var displayWaveType = EstimateMaterialAlternativeHelper.ResolveWaveType(
                ce1.wave_alternative,
                ce1.wave_type);

            var paperName = ce1.paper_name;

            if (!string.IsNullOrWhiteSpace(displayPaperCode))
            {
                var pm = await _db.materials.AsNoTracking()
                    .Where(m => m.code == displayPaperCode)
                    .Select(m => new { m.code, m.name, m.unit })
                    .FirstOrDefaultAsync(ct);

                if (pm != null)
                    paperName = pm.name;
                else
                    paperName = displayPaperCode;
            }
            if (!string.IsNullOrWhiteSpace(paperCode))
            {
                var pm = await _db.materials.AsNoTracking()
                    .Where(m => m.code == paperCode)
                    .Select(m => new { m.code, m.name, m.unit })
                    .FirstOrDefaultAsync(ct);

                if (pm != null)
                {
                    paperName = pm.name;
                }
            }

            var items = new List<OrderMaterialLineDto>();

            // PAPER
            if (ce1.sheets_total > 0)
            {
                items.Add(new OrderMaterialLineDto
                {
                    material_group = "Giấy",
                    material_code = displayPaperCode,
                    material_name = paperName ?? "Giấy",
                    unit = "tờ",
                    quantity = ce1.sheets_total
                });
            }

            // INK
            if (ce1.ink_weight_kg > 0)
            {
                items.Add(new OrderMaterialLineDto
                {
                    material_group = "Mực in các loại",
                    material_code = "INK",
                    material_name = "Mực in",
                    unit = "kg",
                    quantity = ce1.ink_weight_kg
                });
            }

            // COATING GLUE
            if (ce1.coating_glue_weight_kg > 0)
            {
                items.Add(new OrderMaterialLineDto
                {
                    material_group = "Keo phủ",
                    material_code = ce1.coating_type,
                    material_name = "Keo phủ",
                    unit = "kg",
                    quantity = ce1.coating_glue_weight_kg
                });
            }

            // MOUNTING GLUE
            if (ce1.mounting_glue_weight_kg > 0)
            {
                items.Add(new OrderMaterialLineDto
                {
                    material_group = "Keo bồi",
                    material_code = "KEO_BOI",
                    material_name = "Keo bồi",
                    unit = "kg",
                    quantity = ce1.mounting_glue_weight_kg
                });
            }

            // LAMINATION
            if (ce1.lamination_weight_kg > 0)
            {
                items.Add(new OrderMaterialLineDto
                {
                    material_group = "Màng cán",
                    material_code = "MANG_12MIC",
                    material_name = "Màng cán 12 mic",
                    unit = "kg",
                    quantity = ce1.lamination_weight_kg
                });
            }

            // WAVE
            if (!string.IsNullOrWhiteSpace(displayWaveType))
            {
                items.Add(new OrderMaterialLineDto
                {
                    material_group = "Loại sóng",
                    material_code = displayWaveType,
                    material_name = $"Sóng {displayWaveType}",
                    unit = "Tờ",
                    quantity = (int)ce1.wave_sheets_used
                });
            }

            return new OrderMaterialsResponse
            {
                order_id = orderId,
                order_code = o1.code,
                order_request_id = r1.order_request_id,
                items = items
            };
        }
    }
}
