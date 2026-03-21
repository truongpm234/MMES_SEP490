using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Email;
using AMMS.Shared.DTOs.Estimates;
using AMMS.Shared.DTOs.Requests;

namespace AMMS.Application.Services
{
    public class EstimateService : IEstimateService
    {
        private readonly ICostEstimateRepository _estimateRepo;
        private readonly IQuoteRepository _quoteRepo;
        private readonly IUploadFileService _uploadFileService;
        private readonly IAccessService _accessService;

        public EstimateService(ICostEstimateRepository costEstimateRepository, IQuoteRepository quoteRepo, IUploadFileService uploadFileService, IAccessService accessService)
        {
            _estimateRepo = costEstimateRepository;
            _quoteRepo = quoteRepo;
            _uploadFileService = uploadFileService;
            _accessService = accessService;
        }

        public async Task UpdateFinalCostAsync(int orderRequestId, decimal? finalCostInput)
        {
            await _accessService.EnsureCanAccessAssignedRequestAsync(orderRequestId);

            var estimate = await _estimateRepo.GetByOrderRequestIdAsync(orderRequestId)
                ?? throw new Exception("Estimate not found for this order_request_id");

            if (finalCostInput is null)
                throw new ArgumentException("final_total_cost is required");

            var finalCost = finalCostInput.Value;

            if (finalCost < 0m)
                throw new ArgumentException("final_total_cost must be >= 0");

            estimate.final_total_cost = RoundToThousand(finalCost);

            await _estimateRepo.SaveChangesAsync();
        }

        private static decimal RoundToThousand(decimal value)
        {
            return Math.Round(value / 1000m, MidpointRounding.AwayFromZero) * 1000m;
        }

        public async Task<cost_estimate?> GetEstimateByIdAsync(int estimateId)
        {
            return await _estimateRepo.GetByIdAsync(estimateId);
        }

        public Task<DepositByRequestResponse?> GetDepositByRequestIdAsync(int requestId, CancellationToken ct = default) 
            => _estimateRepo.GetDepositByRequestIdAsync(requestId, ct);

        public async Task<bool> OrderRequestExistsAsync(int orderRequestId)
        {
            return await _estimateRepo.OrderRequestExistsAsync(orderRequestId);
        }

        public async Task<int> SaveFeCostEstimateAsync(CostEstimateInsertRequest req, CancellationToken ct = default)
        {
            await _accessService.EnsureCanAccessAssignedRequestAsync(req.order_request_id, ct);

            if (req.order_request_id <= 0)
                throw new ArgumentException("order_request_id must be > 0");

            var orderReq = await _estimateRepo.GetOrderRequestTrackingAsync(req.order_request_id, ct);
            if (orderReq == null)
                throw new Exception("Order request not found");

            cost_estimate? previousEstimate = null;

            if (req.previous_estimate_id.HasValue)
            {
                previousEstimate = await _estimateRepo.GetByIdAsync(req.previous_estimate_id.Value);
                if (previousEstimate == null)
                    throw new ArgumentException("previous_estimate_id not found");

                if (previousEstimate.order_request_id != req.order_request_id)
                    throw new ArgumentException("previous_estimate_id must belong to the same order_request_id");
            }

            var now = AppTime.NowVnUnspecified();

            var entity = new cost_estimate
            {
                order_request_id = req.order_request_id,
                previous_estimate_id = previousEstimate?.estimate_id,
                created_at = ToUnspecified(req.created_at ?? now),
                is_active = true,
                estimated_finish_date = ToUnspecified(req.estimated_finish_date ?? now),
                production_processes = req.production_processes?.Trim(),
                desired_delivery_date = ToUnspecified(req.desired_delivery_date ?? (orderReq.delivery_date ?? now)),
                contract_file_path = string.IsNullOrWhiteSpace(req.contract_file_path) ? null : req.contract_file_path.Trim(),
                contract_uploaded_at = string.IsNullOrWhiteSpace(req.contract_file_path) ? null : AppTime.NowVnUnspecified(),
            };

            static void SetIfHasValue<T>(T? value, Action<T> setter) where T : struct
            {
                if (value.HasValue) setter(value.Value);
            }

            static void SetIfNotNull(string? value, Action<string> setter)
            {
                if (value != null) setter(value);
            }

            SetIfHasValue(req.paper_cost, v => entity.paper_cost = v);
            SetIfHasValue(req.paper_sheets_used, v => entity.paper_sheets_used = v);
            SetIfHasValue(req.paper_unit_price, v => entity.paper_unit_price = v);

            SetIfHasValue(req.ink_cost, v => entity.ink_cost = v);
            SetIfHasValue(req.ink_weight_kg, v => entity.ink_weight_kg = v);
            SetIfHasValue(req.ink_rate_per_m2, v => entity.ink_rate_per_m2 = v);

            SetIfHasValue(req.coating_glue_cost, v => entity.coating_glue_cost = v);
            SetIfHasValue(req.coating_glue_weight_kg, v => entity.coating_glue_weight_kg = v);
            SetIfHasValue(req.coating_glue_rate_per_m2, v => entity.coating_glue_rate_per_m2 = v);
            SetIfNotNull(req.paper_code, v => entity.paper_code = v);
            SetIfNotNull(req.paper_name, v => entity.paper_name = v);
            SetIfNotNull(req.coating_type, v => entity.coating_type = v);
            SetIfNotNull(req.wave_type, v => entity.wave_type = v);

            SetIfHasValue(req.mounting_glue_cost, v => entity.mounting_glue_cost = v);
            SetIfHasValue(req.mounting_glue_weight_kg, v => entity.mounting_glue_weight_kg = v);
            SetIfHasValue(req.mounting_glue_rate_per_m2, v => entity.mounting_glue_rate_per_m2 = v);

            SetIfHasValue(req.lamination_cost, v => entity.lamination_cost = v);
            SetIfHasValue(req.lamination_weight_kg, v => entity.lamination_weight_kg = v);
            SetIfHasValue(req.lamination_rate_per_m2, v => entity.lamination_rate_per_m2 = v);

            SetIfHasValue(req.material_cost, v => entity.material_cost = v);
            SetIfHasValue(req.base_cost, v => entity.base_cost = v);

            if (req.is_rush.HasValue) entity.is_rush = req.is_rush.Value;
            SetIfHasValue(req.rush_percent, v => entity.rush_percent = v);
            SetIfHasValue(req.rush_amount, v => entity.rush_amount = v);
            SetIfHasValue(req.days_early, v => entity.days_early = v);

            SetIfHasValue(req.subtotal, v => entity.subtotal = v);
            SetIfHasValue(req.discount_percent, v => entity.discount_percent = v);
            SetIfHasValue(req.discount_amount, v => entity.discount_amount = v);
            SetIfHasValue(req.final_total_cost, v => entity.final_total_cost = v);

            SetIfHasValue(req.sheets_required, v => entity.sheets_required = v);
            SetIfHasValue(req.sheets_waste, v => entity.sheets_waste = v);
            SetIfHasValue(req.sheets_total, v => entity.sheets_total = v);
            SetIfHasValue(req.n_up, v => entity.n_up = v);
            SetIfHasValue(req.total_area_m2, v => entity.total_area_m2 = v);

            SetIfHasValue(req.design_cost, v => entity.design_cost = v);
            SetIfNotNull(req.cost_note, v => entity.cost_note = v);

            SetIfHasValue(req.bleed_mm, v => orderReq.bleed_mm = v);
            SetIfHasValue(req.glue_tab_mm, v => orderReq.glue_tab_mm = v);
            if (req.is_one_side_box.HasValue) orderReq.is_one_side_box = req.is_one_side_box.Value;
            SetIfHasValue(req.print_height_mm, v => orderReq.print_height_mm = v);
            SetIfHasValue(req.print_width_mm, v => orderReq.print_width_mm = v);

            if (req.process_costs != null)
            {
                int sheetsBase = entity.sheets_required > 0
                    ? entity.sheets_required
                    : (entity.sheets_total > 0 ? entity.sheets_total : 1);

                decimal totalArea = entity.total_area_m2 > 0 ? entity.total_area_m2 : 0m;

                foreach (var p in req.process_costs)
                {
                    var pcode = (p.process_code ?? "").Trim().ToUpperInvariant();
                    decimal qty = p.quantity ?? 0m;
                    decimal unitPrice = p.unit_price ?? 0m;

                    decimal totalCost = (p.total_cost.HasValue && p.total_cost.Value > 0)
                        ? p.total_cost.Value
                        : (qty * unitPrice);

                    var unit = (p.unit ?? "").Trim();

                    if (pcode is "IN" or "PHU" or "CAN")
                    {
                        var qtySheets = (decimal)sheetsBase;
                        var unitPriceSheet = sheetsBase > 0 ? (totalCost / qtySheets) : 0m;

                        if (totalCost <= 0 && unitPrice > 0 && totalArea > 0)
                        {
                            totalCost = unitPrice * totalArea;
                            unitPriceSheet = sheetsBase > 0 ? (totalCost / qtySheets) : 0m;
                        }

                        entity.process_costs.Add(new cost_estimate_process
                        {
                            process_code = p.process_code,
                            process_name = p.process_name ?? p.process_code,
                            quantity = qtySheets,
                            unit = "tờ",
                            unit_price = unitPriceSheet,
                            total_cost = totalCost,
                            note = p.note,
                            created_at = AppTime.NowVnUnspecified()
                        });

                        continue;
                    }

                    entity.process_costs.Add(new cost_estimate_process
                    {
                        process_code = p.process_code,
                        process_name = p.process_name ?? p.process_code,
                        quantity = qty,
                        unit = string.IsNullOrWhiteSpace(unit) ? "" : unit,
                        unit_price = unitPrice,
                        total_cost = totalCost,
                        note = p.note,
                        created_at = AppTime.NowVnUnspecified()
                    });
                }
            }

            await _estimateRepo.AddAsync(entity);
            await _estimateRepo.SaveChangesAsync();
            await _estimateRepo.NormalizeActiveDraftEstimatesAsync(entity.order_request_id, entity.estimate_id, ct);
            return entity.estimate_id;
        }

        public static DateTime ToUnspecified(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Unspecified)
                return dt;

            return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        }

        public async Task<List<RequestEstimateDto>> GetAllEstimatesFlatByRequestIdAsync(int requestId, CancellationToken ct = default)
        {
            await _accessService.EnsureCanAccessAssignedRequestAsync(requestId, ct);
            return await _estimateRepo.GetAllEstimatesFlatByRequestIdAsync(requestId, ct);
        }

        public async Task<QuoteEmailComparePreviewResponse> BuildPreviewAsync(int requestId, CancellationToken ct = default)
        {
            await _accessService.EnsureCanAccessAssignedRequestAsync(requestId, ct);
            return await _quoteRepo.BuildPreviewAsync(requestId, ct);
        }

        public async Task<string> UploadContractFileAsync(int requestId, int estimateId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default)
        {
            if (requestId <= 0)
                throw new ArgumentException("request_id must be > 0");

            if (estimateId <= 0)
                throw new ArgumentException("estimate_id must be > 0");

            if (fileStream == null)
                throw new ArgumentException("file is required");

            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("fileName is required");

            var orderReq = await _estimateRepo.GetOrderRequestTrackingAsync(requestId, ct);
            if (orderReq == null)
                throw new InvalidOperationException("Order request not found");

            var estimate = await _estimateRepo.GetTrackingByIdAsync(estimateId, ct);
            if (estimate == null)
                throw new InvalidOperationException("Cost estimate not found");

            if (estimate.order_request_id != requestId)
                throw new InvalidOperationException("Estimate does not belong to request_id");

            var safeFileName = Path.GetFileName(fileName);
            var module = $"contracts/request_{requestId}/estimate_{estimateId}";

            var uploadedUrl = await _uploadFileService.UploadAsync(
                fileStream,
                safeFileName,
                contentType,
                module);

            estimate.contract_file_path = uploadedUrl;
            estimate.contract_uploaded_at = AppTime.NowVnUnspecified();

            await _estimateRepo.SaveChangesAsync();

            return uploadedUrl;
        }
    }
}