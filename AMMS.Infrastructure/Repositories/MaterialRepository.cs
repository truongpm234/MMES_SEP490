using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Common;
using AMMS.Shared.DTOs.Materials;
using AMMS.Shared.DTOs.Orders;
using AMMS.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Infrastructure.Repositories
{
    public class MaterialRepository : IMaterialRepository
    {
        private readonly AppDbContext _db;
        public MaterialRepository(AppDbContext db) => _db = db;

        public Task<material?> GetByCodeAsync(string code)
        {
            var c = (code ?? "").Trim().ToLower();
            return _db.materials.AsNoTracking()
                .FirstOrDefaultAsync(m => m.code.ToLower() == c);
        }

        public async Task<List<material>> GetAll()
        {
            return await _db.materials.AsNoTracking().ToListAsync();
        }

        public async Task<material> GetByIdAsync(int id)
        {
            return await _db.materials
                .FirstOrDefaultAsync(m => m.material_id == id);
        }

        public async Task UpdateAsync(material entity)
        {
            _db.materials.Update(entity);
            await Task.CompletedTask;
        }

        public async Task SaveChangeAsync()
        {
            await _db.SaveChangesAsync();
        }

        public async Task<List<material>> GetMaterialByTypeSongAsync()
        {
            return await _db.materials
                .Where(m => m.type != null && m.type == "Sóng")
                .ToListAsync();
        }

        public async Task<List<material>> GetMaterialByTypeMangAsync()
        {
            return await _db.materials
                .AsNoTracking()
                .Where(m => m.type != null && m.type == "Màng")
                .OrderBy(m => m.name)
                .ToListAsync();
        }

        public async Task<PagedResultLite<MaterialShortageDto>> GetShortageForAllOrdersPagedAsync(
     int page,
     int pageSize,
     CancellationToken ct = default)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;
            var skip = (page - 1) * pageSize;

            var today = DateTime.Now.Date;
            var historyStartDate = today.AddDays(-30);
            var historyEndDate = today;

            var bomRows = await (
                from b in _db.boms.AsNoTracking()
                join oi in _db.order_items.AsNoTracking()
                    on b.order_item_id equals oi.item_id
                join m in _db.materials.AsNoTracking()
                    on b.material_id equals m.material_id
                select new
                {
                    m.material_id,
                    m.code,
                    m.name,
                    m.unit,
                    StockQty = m.stock_qty ?? 0m,
                    OrderQty = (decimal)(oi.quantity),
                    QtyPerProduct = b.qty_per_product ?? 0m,
                    WastagePercent = b.wastage_percent ?? 0m
                }
            ).ToListAsync(ct);

            if (!bomRows.Any())
            {
                return new PagedResultLite<MaterialShortageDto>
                {
                    Page = page,
                    PageSize = pageSize,
                    HasNext = false,
                    Data = new List<MaterialShortageDto>()
                };
            }

            var usageLast30List = await _db.stock_moves
                .AsNoTracking()
                .Where(s =>
                    s.type == "OUT" &&
                    s.move_date >= historyStartDate &&
                    s.move_date <= historyEndDate &&
                    s.material_id != null)
                .GroupBy(s => s.material_id!.Value)
                .Select(g => new
                {
                    MaterialId = g.Key,
                    UsageLast30Days = g.Sum(s => s.qty ?? 0m)
                })
                .ToListAsync(ct);

            var usageDict = usageLast30List
                .ToDictionary(x => x.MaterialId, x => x.UsageLast30Days);

            var allMaterials = bomRows
                .GroupBy(x => new
                {
                    x.material_id,
                    x.code,
                    x.name,
                    x.unit,
                    x.StockQty
                })
                .Select(g =>
                {
                    decimal requiredQty = 0m;

                    foreach (var r in g)
                    {
                        var orderQty = r.OrderQty;
                        var qtyPerProduct = r.QtyPerProduct;
                        var wastePercent = r.WastagePercent;

                        // baseQty = số sp * định mức
                        var baseQty = orderQty * qtyPerProduct;

                        // factor = 1 + % hao hụt / 100
                        var factor = 1m + (wastePercent / 100m);

                        var lineRequired = baseQty * factor;
                        if (lineRequired < 0m) lineRequired = 0m;

                        requiredQty += lineRequired;
                    }

                    var materialId = g.Key.material_id;
                    var stockQty = g.Key.StockQty;

                    // Usage 30 ngày gần nhất
                    usageDict.TryGetValue(materialId, out var usageLast30);
                    var safetyQty = usageLast30 * 0.30m;   // 30% usage 30 ngày

                    // Tổng nhu cầu = Required + safety (30% usage)
                    var totalNeeded = requiredQty + safetyQty;

                    var shortageQty = totalNeeded > stockQty
                        ? (totalNeeded - stockQty)
                        : 0m;

                    var needToBuyQty = shortageQty;

                    return new
                    {
                        MaterialId = materialId,
                        g.Key.code,
                        g.Key.name,
                        g.Key.unit,
                        StockQty = stockQty,
                        RequiredQty = requiredQty,
                        ShortageQty = shortageQty,
                        NeedToBuyQty = needToBuyQty
                    };
                })
                .Where(x => x.ShortageQty > 0m)
                .OrderByDescending(x => x.ShortageQty)
                .ThenBy(x => x.name)
                .ToList();

            var paged = allMaterials
                .Skip(skip)
                .Take(pageSize + 1)
                .ToList();

            var hasNext = paged.Count > pageSize;
            if (hasNext)
                paged = paged.Take(pageSize).ToList();

            var dtoList = paged
    .Select(x => new MaterialShortageDto
    {
        MaterialId = x.MaterialId,
        Code = x.code,
        Name = x.name,
        Unit = x.unit,
        StockQty = x.StockQty,
        RequiredQty = x.RequiredQty,
        ShortageQty = x.ShortageQty,
        NeedToBuyQty = x.NeedToBuyQty
    })
    .ToList();

            return new PagedResultLite<MaterialShortageDto>
            {
                Page = page,
                PageSize = pageSize,
                HasNext = hasNext,
                Data = dtoList
            };
        }
        public async Task<MaterialTypePaperDto> GetAllPaperTypeAsync()
        {
            var response = new MaterialTypePaperDto();
            var materialsPaperType = _db.materials.Where(n => n.type == "Giấy").ToList();
            var paperMaxStockProduct = _db.materials.Where(m => m.type == "Giấy").OrderByDescending(m => m.stock_qty).Select(m => m.name).FirstOrDefault();
            foreach (var material in materialsPaperType)
            {
                var paperTypedto = new PaperTypeDto();
                paperTypedto.Code = material.code;
                paperTypedto.Name = material.name;
                paperTypedto.StockQty = material.stock_qty;
                paperTypedto.Price = material.cost_price;
                paperTypedto.description = material.description;
                paperTypedto.material_class = material.material_class;
                response.PaperTypes.Add(paperTypedto);
            }
            response.MostStockPaperNames = paperMaxStockProduct;
            return response;
        }

        public async Task<MaterialTypeGlueDto> GetAllBoiGlueTypeAsync()
        {
            var response = new MaterialTypeGlueDto();
            var materialsPaperType = _db.materials.Where(n => n.type == "Keo bồi").ToList();
            var paperMaxStockProduct = _db.materials.Where(m => m.code == "KEO_BOI").OrderByDescending(m => m.stock_qty).Select(m => m.name).FirstOrDefault();
            foreach (var material in materialsPaperType)
            {
                var waveTypedto = new GlueTypeDto();
                waveTypedto.Code = material.code;
                waveTypedto.Name = material.name;
                waveTypedto.StockQty = material.stock_qty;
                waveTypedto.Price = material.cost_price;
                waveTypedto.description = material.description;
                waveTypedto.material_class = material.material_class;
                response.GlueTypes.Add(waveTypedto);
            }
            response.MostStockGlueNames = paperMaxStockProduct;
            return response;
        }

        public async Task<MaterialTypeGlueDto> GetAllDanGlueTypeAsync()
        {
            var response = new MaterialTypeGlueDto();
            var materialsPaperType = _db.materials.Where(n => n.type == "Keo dán").ToList();
            var paperMaxStockProduct = _db.materials.Where(m => m.code == "KEO_DAN").OrderByDescending(m => m.stock_qty).Select(m => m.name).FirstOrDefault();
            foreach (var material in materialsPaperType)
            {
                var waveTypedto = new GlueTypeDto();
                waveTypedto.Code = material.code;
                waveTypedto.Name = material.name;
                waveTypedto.StockQty = material.stock_qty;
                waveTypedto.Price = material.cost_price;
                waveTypedto.description = material.description;
                waveTypedto.material_class = material.material_class;
                response.GlueTypes.Add(waveTypedto);
            }
            response.MostStockGlueNames = paperMaxStockProduct;
            return response;
        }

        public async Task<MaterialTypeGlueDto> GetAllPhuGlueTypeAsync()
        {
            var response = new MaterialTypeGlueDto();
            var materialsPaperType = _db.materials.Where(n => n.code == "KEO_PHU_NUOC" || n.code == "KEO_PHU_DAU").ToList();
            var paperMaxStockProduct = _db.materials.Where(m => m.code == "Keo phủ").OrderByDescending(m => m.stock_qty).Select(m => m.name).FirstOrDefault();
            foreach (var material in materialsPaperType)
            {
                var waveTypedto = new GlueTypeDto();
                waveTypedto.Code = material.code;
                waveTypedto.Name = material.name;
                waveTypedto.StockQty = material.stock_qty;
                waveTypedto.Price = material.cost_price;
                waveTypedto.description = material.description;
                waveTypedto.material_class = material.material_class;
                response.GlueTypes.Add(waveTypedto);
            }
            response.MostStockGlueNames = paperMaxStockProduct;
            return response;
        }

        public async Task<bool> IncreaseStockAsync(int materialId, decimal quantity)
        {
            var material = await _db.materials.FirstOrDefaultAsync(x => x.material_id == materialId);
            if (material == null) return false;

            material.stock_qty = (material.stock_qty ?? 0) + quantity;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DecreaseStockAsync(int materialId, decimal quantity)
        {
            var material = await _db.materials.FirstOrDefaultAsync(x => x.material_id == materialId);
            if (material == null) return false;

            var currentStock = material.stock_qty ?? 0;
            if (currentStock < quantity)
                throw new InvalidOperationException("Số lượng tồn kho không đủ để giảm.");

            material.stock_qty = currentStock - quantity;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<PagedResultLite<MaterialStockAlertDto>> GetMaterialStockAlertsPagedAsync(
    int page,
    int pageSize,
    decimal nearMinThresholdPercent = 0.2m,
    CancellationToken ct = default)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;
            if (nearMinThresholdPercent < 0) nearMinThresholdPercent = 0.2m;

            var skip = (page - 1) * pageSize;

            var materials = await _db.materials
                .AsNoTracking()
                .Select(m => new
                {
                    m.material_id,
                    m.code,
                    m.name,
                    m.unit,
                    m.type,
                    m.material_class,
                    StockQty = m.stock_qty ?? 0m,
                    MinStockQty = m.min_stock ?? 0m
                })
                .ToListAsync(ct);

            var alerts = materials
                .Select(m =>
                {
                    var stockQty = m.StockQty;
                    var minStockQty = m.MinStockQty;

                    var nearThreshold = minStockQty + (minStockQty * nearMinThresholdPercent);
                    var gapToMin = stockQty - minStockQty;

                    var isLowStock = stockQty < minStockQty;
                    var isNearMinStock = !isLowStock && stockQty <= nearThreshold;

                    string warningLevel;
                    string? warningMessage;

                    if (isLowStock)
                    {
                        warningLevel = "LOW_STOCK";
                        warningMessage =
                            $"NVL đã dưới min stock. Tồn hiện tại {stockQty:N2} {m.unit}, min stock là {minStockQty:N2} {m.unit}.";
                    }
                    else if (isNearMinStock)
                    {
                        warningLevel = "NEAR_MIN_STOCK";
                        warningMessage =
                            $"NVL gần chạm min stock. Tồn hiện tại {stockQty:N2} {m.unit}, min stock là {minStockQty:N2} {m.unit}.";
                    }
                    else
                    {
                        warningLevel = "NORMAL";
                        warningMessage = null;
                    }

                    return new MaterialStockAlertDto
                    {
                        MaterialId = m.material_id,
                        Code = m.code,
                        Name = m.name,
                        Unit = m.unit,
                        Type = m.type,
                        MaterialClass = m.material_class,
                        StockQty = stockQty,
                        MinStockQty = minStockQty,
                        GapToMinStock = gapToMin,
                        IsLowStock = isLowStock,
                        IsNearMinStock = isNearMinStock,
                        WarningLevel = warningLevel,
                        WarningMessage = warningMessage
                    };
                })
                .Where(x => x.IsLowStock || x.IsNearMinStock)
                .OrderByDescending(x => x.IsLowStock)
                .ThenBy(x => x.GapToMinStock)
                .ThenBy(x => x.Name)
                .ToList();

            var paged = alerts
                .Skip(skip)
                .Take(pageSize + 1)
                .ToList();

            var hasNext = paged.Count > pageSize;
            if (hasNext)
                paged = paged.Take(pageSize).ToList();

            return new PagedResultLite<MaterialStockAlertDto>
            {
                Page = page,
                PageSize = pageSize,
                HasNext = hasNext,
                Data = paged
            };
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

                where o.order_id == orderId
                select new { o, r }
            ).FirstOrDefaultAsync(ct);

            if (header == null || header.r == null)
                return null;

            var o1 = header.o;
            var r1 = header.r;

            cost_estimate? ce1 = null;

            if (r1.accepted_estimate_id.HasValue && r1.accepted_estimate_id.Value > 0)
            {
                ce1 = await _db.cost_estimates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.estimate_id == r1.accepted_estimate_id.Value &&
                        x.order_request_id == r1.order_request_id, ct);
            }

            ce1 ??= await _db.cost_estimates
                .AsNoTracking()
                .Where(x => x.order_request_id == r1.order_request_id)
                .OrderByDescending(x => x.is_active)
                .ThenByDescending(x => x.estimate_id)
                .FirstOrDefaultAsync(ct);

            if (ce1 == null)
                return null;

            var displayPaperCode = EstimateMaterialAlternativeHelper.ResolvePaperCode(
                ce1.paper_alternative,
                ce1.paper_code);

            var displayWaveType = EstimateMaterialAlternativeHelper.ResolveWaveType(
                ce1.wave_alternative,
                ce1.wave_type);

            string? paperName = ce1.paper_name;

            if (!string.IsNullOrWhiteSpace(displayPaperCode))
            {
                paperName = await _db.materials.AsNoTracking()
                    .Where(m => m.code == displayPaperCode)
                    .Select(m => m.name)
                    .FirstOrDefaultAsync(ct) ?? ce1.paper_name ?? displayPaperCode;
            }

            string? displayLaminationCode = ce1.lamination_material_code;
            string? displayLaminationName = ce1.lamination_material_name;

            if (ce1.lamination_material_id.HasValue && string.IsNullOrWhiteSpace(displayLaminationName))
            {
                var laminationMat = await _db.materials
                    .AsNoTracking()
                    .Where(x => x.material_id == ce1.lamination_material_id.Value)
                    .Select(x => new { x.code, x.name })
                    .FirstOrDefaultAsync(ct);

                if (laminationMat != null)
                {
                    displayLaminationCode ??= laminationMat.code;
                    displayLaminationName ??= laminationMat.name;
                }
            }

            if (string.IsNullOrWhiteSpace(displayLaminationName) &&
                !string.IsNullOrWhiteSpace(displayLaminationCode))
            {
                displayLaminationName = await _db.materials
                    .AsNoTracking()
                    .Where(x => x.code == displayLaminationCode)
                    .Select(x => x.name)
                    .FirstOrDefaultAsync(ct) ?? displayLaminationCode;
            }

            var items = new List<OrderMaterialLineDto>();

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

            if (ce1.ink_weight_kg > 0)
            {
                items.Add(new OrderMaterialLineDto
                {
                    material_group = "Mực in",
                    material_code = "INK",
                    material_name = "Mực in",
                    unit = "kg",
                    quantity = ce1.ink_weight_kg
                });
            }

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

            if (ce1.lamination_weight_kg > 0)
            {
                items.Add(new OrderMaterialLineDto
                {
                    material_group = "Màng cán",
                    material_code = displayLaminationCode,
                    material_name = displayLaminationName ?? "Màng cán",
                    unit = "kg",
                    quantity = ce1.lamination_weight_kg
                });
            }

            if (!string.IsNullOrWhiteSpace(displayWaveType))
            {
                items.Add(new OrderMaterialLineDto
                {
                    material_group = "Sóng carton",
                    material_code = displayWaveType,
                    material_name = $"Sóng {displayWaveType}",
                    unit = "tờ",
                    quantity = ce1.wave_sheets_used ?? 0
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
