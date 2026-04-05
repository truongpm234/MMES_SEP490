using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using AMMS.Shared.DTOs.Requests;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Infrastructure.Repositories
{
    public class CostEstimateRepository : ICostEstimateRepository
    {
        private readonly AppDbContext _db;

        public CostEstimateRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task AddAsync(cost_estimate entity)
        {
            await _db.cost_estimates.AddAsync(entity);
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
        public Task<order_request?> GetOrderRequestTrackingAsync(int orderRequestId, CancellationToken ct = default)
        {
            return _db.order_requests
                .AsTracking()
                .FirstOrDefaultAsync(x => x.order_request_id == orderRequestId, ct);
        }

        public async Task<cost_estimate?> GetByOrderRequestIdAsync(int orderRequestId)
        {
            return await _db.cost_estimates
                .Include(x => x.order_request)
                .Include(x => x.process_costs)
                .Where(x => x.order_request_id == orderRequestId && x.is_active)
                .OrderByDescending(x => x.estimate_id)
                .FirstOrDefaultAsync(x => x.order_request_id == orderRequestId);
        }

        public async Task<cost_estimate?> GetByIdAsync(int id)
        {
            return await _db.cost_estimates
                .Include(x => x.process_costs)
                .FirstOrDefaultAsync(x => x.estimate_id == id);
        }

        public async Task UpdateAsync(cost_estimate entity)
        {
            _db.cost_estimates.Update(entity);
            await Task.CompletedTask;
        }

        public async Task<DepositByRequestResponse?> GetDepositByRequestIdAsync(int requestId, CancellationToken ct = default)
        {
            return await _db.cost_estimates
                .AsNoTracking()
                .Where(x => x.order_request_id == requestId)
                .OrderByDescending(x => x.created_at)
                .ThenByDescending(x => x.estimate_id)
                .Select(x => new DepositByRequestResponse
                {
                    order_request_id = x.order_request_id,
                    deposit_amount = x.deposit_amount
                })
                .FirstOrDefaultAsync(ct);
        }

        public async Task<bool> OrderRequestExistsAsync(int order_request_id)
        {
            return await _db.order_requests.AnyAsync(x => x.order_request_id == order_request_id);
        }

        public async Task<List<RequestEstimateDto>> GetAllEstimatesFlatByRequestIdAsync(int requestId, CancellationToken ct = default)
        {
            var requestExists = await _db.order_requests
                .AsNoTracking()
                .AnyAsync(x => x.order_request_id == requestId, ct);

            if (!requestExists)
                return new List<RequestEstimateDto>();

            var q =
                from r in _db.order_requests.AsNoTracking()
                join ce in _db.cost_estimates.AsNoTracking()
                    on r.order_request_id equals ce.order_request_id
                where r.order_request_id == requestId
                orderby ce.estimate_id descending
                select new RequestEstimateDto
                {
                    order_request_id = r.order_request_id,
                    previous_estimate_id = ce.previous_estimate_id,
                    customer_name = r.customer_name,
                    customer_phone = r.customer_phone,
                    customer_email = r.customer_email,
                    delivery_date = r.delivery_date,
                    product_name = r.product_name,
                    quantity = r.quantity,
                    description = r.description,
                    design_file_path = r.design_file_path,
                    order_request_date = r.order_request_date,
                    detail_address = r.detail_address,
                    process_status = r.process_status,
                    product_type = r.product_type,
                    number_of_plates = r.number_of_plates,
                    production_processes = ce.production_processes,
                    order_id = r.order_id,
                    quote_id = r.quote_id,
                    product_length_mm = r.product_length_mm,
                    product_width_mm = r.product_width_mm,
                    product_height_mm = r.product_height_mm,
                    glue_tab_mm = r.glue_tab_mm,
                    bleed_mm = r.bleed_mm,
                    is_one_side_box = r.is_one_side_box,
                    print_width_mm = r.print_width_mm,
                    print_length_mm = r.print_length_mm,
                    is_send_design = r.is_send_design,
                    note = r.note,
                    reason = r.reason,
                    accepted_estimate_id = r.accepted_estimate_id,
                    estimate_id = ce.estimate_id,
                    estimate_order_request_id = ce.order_request_id,
                    paper_cost = ce.paper_cost,
                    paper_sheets_used = ce.paper_sheets_used,
                    paper_unit_price = ce.paper_unit_price,
                    ink_cost = ce.ink_cost,
                    ink_weight_kg = ce.ink_weight_kg,
                    ink_rate_per_m2 = ce.ink_rate_per_m2,
                    coating_glue_cost = ce.coating_glue_cost,
                    coating_glue_weight_kg = ce.coating_glue_weight_kg,
                    coating_glue_rate_per_m2 = ce.coating_glue_rate_per_m2,
                    estimate_paper_code = !string.IsNullOrWhiteSpace(ce.paper_alternative) ? ce.paper_alternative : ce.paper_code,
                    estimate_paper_name = !string.IsNullOrWhiteSpace(ce.paper_alternative) ? ce.paper_alternative : ce.paper_name,
                    estimate_wave_type = !string.IsNullOrWhiteSpace(ce.wave_alternative) ? ce.wave_alternative : ce.wave_type,
                    estimate_coating_type = ce.coating_type,
                    paper_alternative = ce.paper_alternative,
                    wave_alternative = ce.wave_alternative,
                    mounting_glue_cost = ce.mounting_glue_cost,
                    mounting_glue_weight_kg = ce.mounting_glue_weight_kg,
                    mounting_glue_rate_per_m2 = ce.mounting_glue_rate_per_m2,
                    lamination_cost = ce.lamination_cost,
                    lamination_weight_kg = ce.lamination_weight_kg,
                    lamination_rate_per_m2 = ce.lamination_rate_per_m2,
                    material_cost = ce.material_cost,
                    base_cost = ce.base_cost,
                    is_rush = ce.is_rush,
                    rush_percent = ce.rush_percent,
                    rush_amount = ce.rush_amount,
                    days_early = ce.days_early,
                    subtotal = ce.subtotal,
                    discount_percent = ce.discount_percent,
                    discount_amount = ce.discount_amount,
                    final_total_cost = ce.final_total_cost,
                    estimated_finish_date = ce.estimated_finish_date,
                    desired_delivery_date = ce.desired_delivery_date,
                    created_at = ce.created_at,
                    sheets_required = ce.sheets_required,
                    sheets_waste = ce.sheets_waste,
                    sheets_total = ce.sheets_total,
                    n_up = ce.n_up,
                    total_area_m2 = ce.total_area_m2,
                    design_cost = ce.design_cost,
                    cost_note = ce.cost_note,
                    deposit_amount = ce.deposit_amount,
                    estimate_waved_sheet_used = ce.wave_sheets_used,
                };

            return await q.ToListAsync(ct);
        }

        public async Task<List<cost_estimate>> GetAllByOrderRequestIdAsync(int orderRequestId, CancellationToken ct = default)
        {
            return await _db.cost_estimates
                .AsNoTracking()
                .Include(x => x.process_costs)
                .Where(x => x.order_request_id == orderRequestId && x.is_active)
                .OrderByDescending(x => x.created_at)
                .ThenByDescending(x => x.estimate_id)
                .ToListAsync(ct);
        }
        public async Task<int> DeactivateAllByRequestIdAsync(int orderRequestId, CancellationToken ct = default)
        {
            return await _db.cost_estimates
                .Where(x => x.order_request_id == orderRequestId && x.is_active)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(x => x.is_active, false),
                    ct);
        }

        public async Task NormalizeActiveDraftEstimatesAsync(int orderRequestId, int currentEstimateId, CancellationToken ct = default)
        {
            var keepIds = await _db.cost_estimates
                .AsNoTracking()
                .Where(x => x.order_request_id == orderRequestId &&
                            (x.is_active || x.estimate_id == currentEstimateId))
                .OrderByDescending(x => x.estimate_id)
                .Select(x => x.estimate_id)
                .Take(2)
                .ToListAsync(ct);

            await _db.cost_estimates
                .Where(x => x.order_request_id == orderRequestId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(x => x.is_active, x => keepIds.Contains(x.estimate_id)), ct);
        }

        public async Task<cost_estimate?> GetTrackingByIdAsync(int estimateId, CancellationToken ct = default)
        {
            return await _db.cost_estimates
                .FirstOrDefaultAsync(x => x.estimate_id == estimateId, ct);
        }

        public async Task<bool> EstimateBelongsToRequestAsync(int estimateId, int requestId, CancellationToken ct = default)
        {
            return await _db.cost_estimates
                .AsNoTracking()
                .AnyAsync(x => x.estimate_id == estimateId && x.order_request_id == requestId, ct);
        }

        public async Task<List<int>> GetTopActiveEstimateIdsByRequestIdAsync(int requestId, int take = 2, CancellationToken ct = default)
        {
            if (take <= 0) take = 2;

            return await _db.cost_estimates
                .AsNoTracking()
                .Where(x => x.order_request_id == requestId && x.is_active)
                .OrderByDescending(x => x.estimate_id)
                .Take(take)
                .Select(x => x.estimate_id)
                .ToListAsync(ct);
        }

        public async Task<CostSummaryByRequestDto?> GetCostSummaryByRequestIdAsync(int requestId, CancellationToken ct = default)
        {
            return await _db.cost_estimates
                .AsNoTracking()
                .Where(x => x.order_request_id == requestId)
                .OrderByDescending(x => x.created_at)
                .ThenByDescending(x => x.estimate_id)
                .Select(x => new CostSummaryByRequestDto
                {
                    order_request_id = x.order_request_id,
                    final_total_cost = x.final_total_cost,
                    deposit_amount = x.deposit_amount
                })
                .FirstOrDefaultAsync(ct);
        }

        public async Task<cost_estimate?> GetFirstActiveTrackingByRequestIdAsync(int requestId, CancellationToken ct = default)
        {
            return await _db.cost_estimates
                .FirstOrDefaultAsync(x => x.order_request_id == requestId && x.is_active, ct);
        }

        public async Task<ContractCheckEstimateInfoDto?> GetContractCheckEstimateInfoAsync(int requestId, int? acceptedEstimateId, CancellationToken ct = default)
        {
            var query = _db.cost_estimates
                .AsNoTracking()
                .Where(x => x.order_request_id == requestId);

            if (acceptedEstimateId.HasValue && acceptedEstimateId.Value > 0)
            {
                query = query.Where(x => x.estimate_id == acceptedEstimateId.Value);
            }
            else
            {
                query = query
                    .OrderByDescending(x => x.is_active)
                    .ThenByDescending(x => x.estimate_id);
            }

            return await query
                .Select(x => new ContractCheckEstimateInfoDto
                {
                    estimate_id = x.estimate_id,
                    consultant_contract_path = x.consultant_contract_path,
                    customer_signed_contract_path = x.customer_signed_contract_path
                })
                .FirstOrDefaultAsync(ct);
        }
    }
}
