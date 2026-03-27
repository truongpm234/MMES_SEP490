using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Common;
using AMMS.Shared.DTOs.Requests;
using AMMS.Shared.DTOs.Socket;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Application.Services
{
    public class RequestService : IRequestService
    {
        private readonly IRequestRepository _requestRepo;
        private readonly IOrderRepository _orderRepo;
        private readonly AppDbContext _db;
        private readonly ICostEstimateRepository _estimateRepo;
        private readonly IMaterialRepository _materialRepo;
        private readonly IBomRepository _bomRepo;
        private readonly IRealtimePublisher _rt;
        private readonly IAccessService _currentUser;
        private readonly IUserRepository _userRepo;
        public RequestService(
            IRequestRepository requestRepo,
            IOrderRepository orderRepo,
            ICostEstimateRepository estimateRepo,
            IMaterialRepository materialRepo,
            IBomRepository bomRepo,
            IRealtimePublisher rt,
            AppDbContext db,
            IAccessService currentUser,
            IUserRepository userRepo)
        {
            _requestRepo = requestRepo;
            _orderRepo = orderRepo;
            _estimateRepo = estimateRepo;
            _materialRepo = materialRepo;
            _bomRepo = bomRepo;
            _db = db;
            _rt = rt;
            _currentUser = currentUser;
            _userRepo = userRepo;
        }

        private DateTime? ToDeliveryDate(DateTime? dateTime)
        {
            return AppTime.NormalizeToVnDateOnlyUnspecified(dateTime);
        }

        private static string Trunc20(string? s)
        {
            s = (s ?? "").Trim();
            if (s.Length <= 20) return s;
            return s.Substring(0, 20);
        }

        public async Task<CreateRequestResponse> CreateAsync(CreateResquest req)
        {
            var now = AppTime.NowVnUnspecified();

            var assignedConsultant = await ResolveAssignedConsultantAsync();

            var entity = new order_request
            {
                customer_name = req.customer_name,
                customer_phone = req.customer_phone,
                customer_email = req.customer_email,
                delivery_date = ToDeliveryDate(req.delivery_date),
                product_name = req.product_name,
                quantity = req.quantity,
                description = req.description,
                design_file_path = req.design_file_path,
                order_request_date = now,
                detail_address = req.detail_address,
                process_status = "Pending",
                is_send_design = req.is_send_design,
                product_height_mm = req.product_height_mm,
                product_length_mm = req.product_length_mm,
                product_width_mm = req.product_width_mm,
                preliminary_estimated_price = req.preliminary_estimated_price,
                estimate_finish_date = now.AddDays(7),
                assigned_consultant = assignedConsultant.user_id,
                assigned_at = now
            };

            await _requestRepo.AddAsync(entity);
            await _requestRepo.SaveChangesAsync();

            await _rt.PublishRequestChangedAsync(new(
                request_id: entity.order_request_id,
                old_status: null,
                new_status: entity.process_status,
                action: "created",
                changed_at: now,
                changed_by: null));

            return new CreateRequestResponse
            {
                order_request_id = entity.order_request_id,
                assigned_consultant = entity.assigned_consultant,
                assigned_at = entity.assigned_at,
                assigned_consultant_user = assignedConsultant
            };
        }

        public async Task<CreateRequestResponse> CreateRequestByConsultantAsync(CreateResquestConsultant req)
        {
            var now = AppTime.NowVnUnspecified();

            var assignedConsultant = await ResolveAssignedConsultantAsync();

            var entity = new order_request
            {
                customer_name = req.customer_name,
                customer_phone = req.customer_phone,
                customer_email = req.customer_email,
                detail_address = req.detail_address,
                order_request_date = now,
                process_status = "Pending",

                assigned_consultant = assignedConsultant.user_id,
                assigned_at = now
            };

            await _requestRepo.AddAsync(entity);
            await _requestRepo.SaveChangesAsync();

            await _rt.PublishRequestChangedAsync(new(
                request_id: entity.order_request_id,
                old_status: null,
                new_status: entity.process_status,
                action: "created",
                changed_at: now,
                changed_by: null));

            return new CreateRequestResponse
            {
                order_request_id = entity.order_request_id,
                assigned_consultant = entity.assigned_consultant,
                assigned_at = entity.assigned_at,
                assigned_consultant_user = assignedConsultant
            };
        }

        public async Task<UpdateRequestResponse> UpdateAsync(int id, UpdateOrderRequest req)
        {
            await _currentUser.EnsureCanAccessAssignedRequestAsync(id);
            var entity = await _requestRepo.GetByIdAsync(id);
            var ce = await _estimateRepo.GetByOrderRequestIdAsync(id);
            if (entity == null)
            {
                return new UpdateRequestResponse
                {
                    Success = false,
                    Message = "Order request not found",
                    UpdatedId = id
                };
            }

            entity.customer_name = req.customer_name ?? entity.customer_name;
            entity.customer_phone = req.customer_phone ?? entity.customer_phone;
            entity.customer_email = req.customer_email ?? entity.customer_email;
            entity.product_name = req.product_name ?? entity.product_name;
            entity.quantity = req.quantity ?? entity.quantity;
            entity.description = req.description ?? entity.description;
            entity.design_file_path = req.design_file_path ?? entity.design_file_path;
            entity.detail_address = req.detail_address ?? entity.detail_address;
            entity.delivery_date = ToDeliveryDate(req.delivery_date);
            entity.product_type = req.product_type ?? entity.product_type;
            entity.number_of_plates = req.number_of_plates ?? entity.number_of_plates;
            entity.product_length_mm = req.product_length_mm ?? entity.product_length_mm;
            entity.product_width_mm = req.product_width_mm ?? entity.product_width_mm;
            entity.product_height_mm = req.product_height_mm ?? entity.product_height_mm;
            entity.glue_tab_mm = req.glue_tab_mm ?? entity.glue_tab_mm;
            entity.bleed_mm = req.bleed_mm ?? entity.bleed_mm;
            entity.is_one_side_box = req.is_one_side_box ?? entity.is_one_side_box;
            entity.print_width_mm = req.print_width_mm ?? entity.print_width_mm;
            entity.print_height_mm = req.print_height_mm ?? entity.print_height_mm;
            entity.is_send_design = req.is_send_design ?? entity.is_send_design;

            if (ce != null && !string.IsNullOrWhiteSpace(req.production_processes))
            {
                ce.production_processes = req.production_processes.Trim();
            }

            if (ce != null)
            {
                if (!string.IsNullOrWhiteSpace(req.paper_code))
                    ce.paper_code = req.paper_code.Trim();

                if (!string.IsNullOrWhiteSpace(req.paper_name))
                    ce.paper_name = req.paper_name.Trim();

                if (!string.IsNullOrWhiteSpace(req.wave_type))
                    ce.wave_type = req.wave_type.Trim();

                if (!string.IsNullOrWhiteSpace(req.coating_type))
                    ce.coating_type = req.coating_type.Trim();
            }

            entity.process_status = "Pending";
            entity.verified_at = null;
            entity.quote_expires_at = null;
            entity.accepted_estimate_id = null;

            await _requestRepo.UpdateAsync(entity);
            await _requestRepo.SaveChangesAsync();

            return new UpdateRequestResponse
            {
                Success = true,
                Message = "Order request updated successfully",
                UpdatedId = id,
                UpdatedAt = AppTime.NowVnUnspecified()
            };
        }

        public async Task CancelAsync(int id, string? reason, CancellationToken ct = default)
        {
            await _currentUser.EnsureCanAccessAssignedRequestAsync(id, ct);
            var entity = await _requestRepo.GetByIdAsync(id);
            if (entity == null) return;

            if (entity.order_id != null)
                throw new InvalidOperationException("This request is already linked to an order, cannot cancel.");
            entity.reason = reason;
            await _requestRepo.CancelAsync(id, ct);
            await _requestRepo.SaveChangesAsync();
        }

        public Task<order_request?> GetByIdAsync(int id) => _requestRepo.GetByIdAsync(id);

        public async Task<RequestWithCostDto?> GetByIdWithCostAsync(int id)
        {
            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync();
            return await _requestRepo.GetByIdWithCostAsync(id, consultantUserId);
        }

        public async Task<PagedResultLite<RequestPagedDto>> GetPagedAsync(int page, int pageSize)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;

            var skip = (page - 1) * pageSize;
            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync();

            var list = await _requestRepo.GetPagedAsync(skip, pageSize + 1, consultantUserId);

            var hasNext = list.Count > pageSize;
            var data = hasNext ? list.Take(pageSize).ToList() : list;

            return new PagedResultLite<RequestPagedDto>
            {
                Page = page,
                PageSize = pageSize,
                HasNext = hasNext,
                Data = data
            };
        }

        public async Task<ConvertRequestToOrderResponse> ConvertToOrderAsync(int requestId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                try
                {
                    var result = await ConvertToOrderInternalAsync(requestId);

                    if (!result.Success)
                    {
                        await tx.RollbackAsync();
                        return result;
                    }

                    await tx.CommitAsync();
                    return result;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            });
        }
        public async Task<RequestDetailDto?> GetInformationRequestById(int requestId, CancellationToken ct = default)
        {
            await _currentUser.EnsureCanAccessAssignedRequestAsync(requestId, ct);

            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync(ct);
            return await _requestRepo.GetInformationRequestById(requestId, consultantUserId, ct);
        }
        public async Task<PagedResultLite<RequestSortedDto>> GetSortedByQuantityPagedAsync(
    bool ascending, int page, int pageSize, CancellationToken ct = default)
        {
            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync(ct);
            return await _requestRepo.GetSortedByQuantityPagedAsync(ascending, page, pageSize, consultantUserId, ct);
        }

        public async Task<PagedResultLite<RequestSortedDto>> GetSortedByDatePagedAsync(
    bool ascending, int page, int pageSize, CancellationToken ct = default)
        {
            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync(ct);
            return await _requestRepo.GetSortedByDatePagedAsync(ascending, page, pageSize, consultantUserId, ct);
        }

        public async Task<PagedResultLite<RequestSortedDto>> GetSortedByDeliveryDatePagedAsync(
    bool nearestFirst, int page, int pageSize, CancellationToken ct = default)
        {
            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync(ct);
            return await _requestRepo.GetSortedByDeliveryDatePagedAsync(nearestFirst, page, pageSize, consultantUserId, ct);
        }

        public Task<PagedResultLite<RequestEmailStatsDto>> GetEmailsByAcceptedCountPagedAsync(
            int page, int pageSize, CancellationToken ct = default)
            => _requestRepo.GetEmailsByAcceptedCountPagedAsync(page, pageSize, ct);

        public async Task<PagedResultLite<RequestStockCoverageDto>> GetSortedByStockCoveragePagedAsync(
    int page, int pageSize, CancellationToken ct = default)
        {
            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync(ct);
            return await _requestRepo.GetSortedByStockCoveragePagedAsync(page, pageSize, consultantUserId, ct);
        }

        public async Task<PagedResultLite<RequestSortedDto>> GetByOrderRequestDatePagedAsync(
    DateOnly date, int page, int pageSize, CancellationToken ct = default)
        {
            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync(ct);
            return await _requestRepo.GetByOrderRequestDatePagedAsync(date, page, pageSize, consultantUserId, ct);
        }

        public async Task<PagedResultLite<RequestSortedDto>> SearchPagedAsync(
    string keyword, int page, int pageSize, CancellationToken ct = default)
        {
            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync(ct);
            return await _requestRepo.SearchPagedAsync(keyword, page, pageSize, consultantUserId, ct);
        }

        public async Task<int> CreateOrderRequestAsync(CreateOrderRequestDto dto, CancellationToken ct = default)
        {
            if (dto.quantity is null or <= 0)
                throw new ArgumentException("quantity must be > 0");
            if (string.IsNullOrWhiteSpace(dto.product_name))
                throw new ArgumentException("product_name is required");

            var now = AppTime.NowVnUnspecified();
            var assignedConsultant = await ResolveAssignedConsultantAsync(ct);

            var entity = new order_request
            {
                customer_name = dto.customer_name?.Trim(),
                customer_phone = dto.customer_phone?.Trim(),
                customer_email = dto.customer_email?.Trim(),
                delivery_date = ToDeliveryDate(dto.delivery_date),
                detail_address = dto.detail_address?.Trim(),
                product_name = dto.product_name?.Trim(),
                quantity = dto.quantity,
                description = dto.description,
                design_file_path = dto.design_file_path,
                is_send_design = dto.is_send_design,
                product_type = dto.product_type?.Trim(),
                number_of_plates = dto.number_of_plates,
                product_length_mm = dto.product_length_mm,
                product_width_mm = dto.product_width_mm,
                product_height_mm = dto.product_height_mm,
                glue_tab_mm = dto.glue_tab_mm,
                bleed_mm = dto.bleed_mm,
                is_one_side_box = dto.is_one_side_box,
                print_width_mm = dto.print_width_mm,
                print_height_mm = dto.print_height_mm,
                order_request_date = now,
                process_status = "Pending",

                assigned_consultant = assignedConsultant.user_id,
                assigned_at = now
            };

            await _requestRepo.AddAsync(entity);
            await _requestRepo.SaveChangesAsync();

            return entity.order_request_id;
        }

        public async Task<OrderRequestDesignFileResponse?> GetDesignFileAsync(int orderRequestId, CancellationToken ct = default)
        {
            await _currentUser.EnsureCanAccessAssignedRequestAsync(orderRequestId, ct);

            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync(ct);
            var path = await _requestRepo.GetDesignFilePathAsync(orderRequestId, consultantUserId, ct);

            if (path == null)
                return null;

            return new OrderRequestDesignFileResponse
            {
                order_request_id = orderRequestId,
                design_file_path = path
            };
        }

        public async Task UpdateDesignFilePathAsync(int orderRequestId, string designFilePath, CancellationToken ct = default)
        {
            var entity = await _requestRepo.GetByIdAsync(orderRequestId);
            if (entity == null)
                throw new Exception("Order request not found");

            entity.design_file_path = designFilePath;
            entity.is_send_design = true;

            await _requestRepo.UpdateAsync(entity);
            await _requestRepo.SaveChangesAsync();
        }
        public async Task<int> DeleteDesignFilePathByRequestIdAsync(int orderRequestId, CancellationToken ct = default)
        {
            return await _requestRepo.DeleteDesignFilePathByRequestIdAsync(orderRequestId, ct);
        }

        public async Task UpdateApprovalAsync(RequestApprovalUpdateDto dto, CancellationToken ct = default)
        {
            if (dto.request_id <= 0) throw new ArgumentException("request_id is required");

            var req = await _requestRepo.GetByIdAsync(dto.request_id);
            if (req == null) throw new InvalidOperationException("Order request not found");

            var oldStatus = req.process_status;

            var st = (dto.status ?? "").Trim();

            st = st.Equals("verified", StringComparison.OrdinalIgnoreCase) ? "Verified" :
                 st.Equals("processing", StringComparison.OrdinalIgnoreCase) ? "Processing" :
                 st.Equals("declined", StringComparison.OrdinalIgnoreCase) ? "Declined" :
                 st;

            if (st is not ("Processing" or "Verified" or "Declined"))
                throw new ArgumentException("status must be Processing | Verified | Declined");

            if ((st == "Verified" || st == "Declined") &&
                !string.Equals(req.process_status, "Processing", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Request must be Processing before manager decision");
            }

            var now = AppTime.NowVnUnspecified();

            req.process_status = st;

            if (dto.note != null)
                req.note = dto.note;

            if (st == "Verified")
            {
                req.verified_at = now;
                req.quote_expires_at = now.AddDays(7);
            }
            else
            {
                req.verified_at = null;
                req.quote_expires_at = null;
            }

            if (st == "Declined")
            {
                req.accepted_estimate_id = null;
                await _estimateRepo.DeactivateAllByRequestIdAsync(dto.request_id, ct);
            }

            await _requestRepo.SaveChangesAsync();

            await _rt.PublishRequestChangedAsync(new(
                request_id: req.order_request_id,
                old_status: oldStatus,
                new_status: req.process_status,
                action: (st == "Verified") ? "manager_verified" : "manager_declined",
                changed_at: now,
                changed_by: null
            ));
        }

        public async Task SubmitEstimateForApprovalAsync(SubmitForApprovalRequestDto input)
        {
            await _currentUser.EnsureCanAccessAssignedRequestAsync(input.request_id);
            if (input.request_id <= 0)
                throw new ArgumentException("request_id is required");

            var req = await _requestRepo.GetByIdAsync(input.request_id);
            if (req == null)
                throw new InvalidOperationException("Order request not found");

            var oldStatus = req.process_status;

            var st = (req.process_status ?? "").Trim();
            if (st.Equals("Accepted", StringComparison.OrdinalIgnoreCase) ||
                st.Equals("Rejected", StringComparison.OrdinalIgnoreCase) ||
                st.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot submit when process_status = {req.process_status}");
            }

            var keepIds = await _db.cost_estimates
                .AsNoTracking()
                .Where(x => x.order_request_id == input.request_id && x.is_active)
                .OrderByDescending(x => x.estimate_id)
                .Select(x => x.estimate_id)
                .Take(2)
                .ToListAsync();

            if (keepIds.Count == 0)
                throw new InvalidOperationException("No active estimate found. Please create or revise estimate before submit.");

            await _db.cost_estimates
                .Where(x => x.order_request_id == input.request_id)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(x => x.is_active, x => keepIds.Contains(x.estimate_id)));

            var normalizedNote = string.IsNullOrWhiteSpace(input.consultant_note)
                ? null
                : input.consultant_note.Trim();

            req.consultant_note = normalizedNote;
            req.process_status = "Processing";
            req.verified_at = null;
            req.quote_expires_at = null;
            req.accepted_estimate_id = null;

            await _requestRepo.SaveChangesAsync();

            await _rt.PublishRequestNoteChangedAsync(new(
                request_id: req.order_request_id,
                consultant_note: req.consultant_note,
                changed_at: AppTime.NowVnUnspecified()
            ));

            await _rt.PublishRequestChangedAsync(new(
                request_id: req.order_request_id,
                old_status: oldStatus,
                new_status: req.process_status,
                action: "submitted_for_approval",
                changed_at: AppTime.NowVnUnspecified(),
                changed_by: null
            ));
        }

        public async Task<RequestWithTwoEstimatesDto?> GetCompareQuotesAsync(int requestId, CancellationToken ct = default)
        {
            if (requestId <= 0) return null;

            await _currentUser.EnsureCanAccessAssignedRequestAsync(requestId, ct);
            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync(ct);

            return await _requestRepo.GetActiveEstimatesInProcessAsync(requestId, consultantUserId, ct);
        }

        public async Task<CloneRequestResponseDto> CloneRequestAsync(int requestId, CancellationToken ct = default)
        {
            await _currentUser.EnsureCanAccessAssignedRequestAsync(requestId, ct);
            if (requestId <= 0)
                throw new ArgumentException("request_id is required");

            var source = await _requestRepo.GetByIdAsync(requestId);
            if (source == null)
                throw new InvalidOperationException("Order request not found");

            var consultantUserId = await _currentUser.GetConsultantScopeUserIdAsync(ct);
            var activeEstimates = await _requestRepo.GetActiveEstimatesWithProcessesByRequestIdAsync(requestId, consultantUserId, ct);

            var now = AppTime.NowVnUnspecified();

            var clonedAssignedConsultantId = source.assigned_consultant ?? await _requestRepo.GetLeastLoadedConsultantUserIdAsync(ct);

            var clonedRequest = new order_request
            {
                customer_name = source.customer_name,
                customer_phone = source.customer_phone,
                customer_email = source.customer_email,
                delivery_date = source.delivery_date,
                product_name = source.product_name,
                quantity = source.quantity,
                description = source.description,
                design_file_path = source.design_file_path,
                order_request_date = now,
                detail_address = source.detail_address,

                process_status = "Pending",

                product_type = source.product_type,
                number_of_plates = source.number_of_plates,

                order_id = null,
                quote_id = null,
                accepted_estimate_id = null,

                product_length_mm = source.product_length_mm,
                product_width_mm = source.product_width_mm,
                product_height_mm = source.product_height_mm,
                glue_tab_mm = source.glue_tab_mm,
                bleed_mm = source.bleed_mm,
                is_one_side_box = source.is_one_side_box,
                print_width_mm = source.print_width_mm,
                print_height_mm = source.print_height_mm,
                is_send_design = source.is_send_design,

                note = null,
                reason = null,
                verified_at = null,
                quote_expires_at = null,
                consultant_note = source.consultant_note,

                assigned_consultant = clonedAssignedConsultantId,
                assigned_at = clonedAssignedConsultantId.HasValue ? now : null
            };

            await _requestRepo.AddAsync(clonedRequest);
            await _requestRepo.SaveChangesAsync();

            var clonedEstimateIds = new List<int>();
            var clonedEstimateMap = new Dictionary<int, cost_estimate>();

            foreach (var est in activeEstimates.OrderBy(x => x.estimate_id))
            {
                var clonedEstimate = new cost_estimate
                {
                    order_request_id = clonedRequest.order_request_id,

                    previous_estimate_id = null,

                    paper_cost = est.paper_cost,
                    paper_sheets_used = est.paper_sheets_used,
                    paper_unit_price = est.paper_unit_price,

                    ink_cost = est.ink_cost,
                    ink_weight_kg = est.ink_weight_kg,
                    ink_rate_per_m2 = est.ink_rate_per_m2,

                    coating_glue_cost = est.coating_glue_cost,
                    coating_glue_weight_kg = est.coating_glue_weight_kg,
                    coating_glue_rate_per_m2 = est.coating_glue_rate_per_m2,
                    coating_type = est.coating_type,

                    mounting_glue_cost = est.mounting_glue_cost,
                    mounting_glue_weight_kg = est.mounting_glue_weight_kg,
                    mounting_glue_rate_per_m2 = est.mounting_glue_rate_per_m2,

                    lamination_cost = est.lamination_cost,
                    lamination_weight_kg = est.lamination_weight_kg,
                    lamination_rate_per_m2 = est.lamination_rate_per_m2,

                    material_cost = est.material_cost,
                    base_cost = est.base_cost,

                    is_rush = est.is_rush,
                    rush_percent = est.rush_percent,
                    rush_amount = est.rush_amount,
                    days_early = est.days_early,

                    subtotal = est.subtotal,
                    discount_percent = est.discount_percent,
                    discount_amount = est.discount_amount,
                    final_total_cost = est.final_total_cost,

                    estimated_finish_date = est.estimated_finish_date,
                    desired_delivery_date = est.desired_delivery_date,
                    created_at = now,

                    sheets_required = est.sheets_required,
                    sheets_waste = est.sheets_waste,
                    sheets_total = est.sheets_total,
                    n_up = est.n_up,
                    total_area_m2 = est.total_area_m2,

                    design_cost = est.design_cost,
                    cost_note = est.cost_note,

                    is_active = true,

                    paper_code = est.paper_code,
                    paper_name = est.paper_name,
                    wave_type = est.wave_type,
                    production_processes = est.production_processes
                };

                if (est.process_costs != null && est.process_costs.Count > 0)
                {
                    foreach (var pc in est.process_costs)
                    {
                        clonedEstimate.process_costs.Add(new cost_estimate_process
                        {
                            process_code = pc.process_code,
                            process_name = pc.process_name,
                            quantity = pc.quantity,
                            unit = pc.unit,
                            unit_price = pc.unit_price,
                            total_cost = pc.total_cost,
                            note = pc.note,
                            created_at = now
                        });
                    }
                }

                await _estimateRepo.AddAsync(clonedEstimate);
                await _estimateRepo.SaveChangesAsync();

                clonedEstimateIds.Add(clonedEstimate.estimate_id);
                clonedEstimateMap[est.estimate_id] = clonedEstimate;
            }

            foreach (var oldEst in activeEstimates)
            {
                if (!oldEst.previous_estimate_id.HasValue)
                    continue;

                if (!clonedEstimateMap.TryGetValue(oldEst.estimate_id, out var newEst))
                    continue;

                if (!clonedEstimateMap.TryGetValue(oldEst.previous_estimate_id.Value, out var newPrevEst))
                    continue;

                newEst.previous_estimate_id = newPrevEst.estimate_id;
            }

            await _estimateRepo.SaveChangesAsync();

            await _rt.PublishRequestChangedAsync(new(
                request_id: clonedRequest.order_request_id,
                old_status: null,
                new_status: clonedRequest.process_status,
                action: "cloned_from_request",
                changed_at: now,
                changed_by: null
            ));

            return new CloneRequestResponseDto
            {
                source_request_id = requestId,
                cloned_request_id = clonedRequest.order_request_id,
                cloned_estimate_ids = clonedEstimateIds,
                message = "Cloned request successfully"
            };
        }

        public async Task UpdateConsultantMessageToCustomerAsync(int requestId, string? message, CancellationToken ct = default)
        {
            await _currentUser.EnsureCanAccessAssignedRequestAsync(requestId, ct);
            if (requestId <= 0)
                throw new ArgumentException("request_id is required");

            var req = await _requestRepo.GetByIdAsync(requestId);
            if (req == null)
                throw new InvalidOperationException("Order request not found");

            if (!string.Equals(req.process_status, "Verified", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only Verified request can update consultant message to customer");

            req.message_to_customer = string.IsNullOrWhiteSpace(message)
                ? null
                : message.Trim();

            await _requestRepo.SaveChangesAsync();
        }

        private async Task<string> NormalizeProductionProcessCsvAsync(
    int productTypeId,
    string? rawCsv,
    CancellationToken ct = default)
        {
            var allSteps = await _db.product_type_processes
                .AsNoTracking()
                .Where(x => x.product_type_id == productTypeId && (x.is_active ?? true))
                .OrderBy(x => x.seq_num)
                .ToListAsync(ct);

            var selected = ProductionProcessSelectionHelper.ResolveFixedRoute(
                allSteps,
                x => x.process_code,
                rawCsv);

            return ProductionProcessSelectionHelper.BuildCsv(selected, x => x.process_code);
        }

        public async Task<order_request?> GetRequestForUpdateAsync(int orderRequestId, CancellationToken ct)
        {
            return await _requestRepo.GetRequestForUpdateAsync(orderRequestId, ct);
        }

        private async Task<ConvertRequestToOrderResponse> ConvertToOrderInternalAsync(int requestId)
        {
            var req = await _requestRepo.GetByIdAsync(requestId);
            if (req == null)
            {
                return new ConvertRequestToOrderResponse
                {
                    Success = false,
                    Message = "Order request not found",
                    RequestId = requestId
                };
            }

            if (!string.Equals(req.process_status?.Trim(), "Accepted", StringComparison.OrdinalIgnoreCase))
            {
                return new ConvertRequestToOrderResponse
                {
                    Success = false,
                    Message = "Only process_status = 'Accepted' can be converted to order",
                    RequestId = requestId
                };
            }

            if (req.quote_id == null)
            {
                return new ConvertRequestToOrderResponse
                {
                    Success = false,
                    Message = "No quote found for this request",
                    RequestId = requestId
                };
            }

            if (req.order_id != null || await _requestRepo.AnyOrderLinkedAsync(requestId))
            {
                return new ConvertRequestToOrderResponse
                {
                    Success = true,
                    Message = "This request was already converted",
                    RequestId = requestId,
                    OrderId = req.order_id
                };
            }

            var est = await _estimateRepo.GetByOrderRequestIdAsync(requestId);
            if (est == null)
            {
                return new ConvertRequestToOrderResponse
                {
                    Success = false,
                    Message = "Cost estimate not found for this request",
                    RequestId = requestId
                };
            }

            var ptCode = (req.product_type ?? "").Trim();
            if (string.IsNullOrWhiteSpace(ptCode))
            {
                return new ConvertRequestToOrderResponse
                {
                    Success = false,
                    Message = "order_request.product_type is missing, cannot map product_type_id",
                    RequestId = requestId
                };
            }

            var productTypeId = await _db.product_types
                .AsNoTracking()
                .Where(x => x.code == ptCode)
                .Select(x => (int?)x.product_type_id)
                .FirstOrDefaultAsync();

            if (productTypeId == null)
            {
                return new ConvertRequestToOrderResponse
                {
                    Success = false,
                    Message = $"Product type code '{ptCode}' not found in product_types",
                    RequestId = requestId
                };
            }

            var normalizedProcessCsv = await NormalizeProductionProcessCsvAsync(productTypeId.Value, est.production_processes);

            var newOrder = new order
            {
                code = "TMP-ORD",
                order_date = AppTime.NowVnUnspecified(),
                delivery_date = req.delivery_date,
                status = "LayoutPending",
                payment_status = "Deposited",
                quote_id = req.quote_id,
                total_amount = est.final_total_cost,
                is_enough = null,
                is_buy = false,
                layout_confirmed = false
            };

            await _orderRepo.AddOrderAsync(newOrder);
            await _orderRepo.SaveChangesAsync();

            newOrder.code = $"ORD-{newOrder.order_id:00000}";
            _orderRepo.Update(newOrder);
            await _orderRepo.SaveChangesAsync();

            var newItem = new order_item
            {
                order_id = newOrder.order_id,
                product_name = req.product_name,
                quantity = req.quantity ?? 0,
                design_url = req.design_file_path,
                product_type_id = productTypeId,
                paper_code = est.paper_code,
                production_process = normalizedProcessCsv,
                paper_name = est.paper_name,
                glue_type = est.coating_type,
                wave_type = est.wave_type,
                est_paper_sheets_total = est.sheets_total,
                est_ink_weight_kg = est.ink_weight_kg,
                est_coating_glue_weight_kg = est.coating_glue_weight_kg,
                est_mounting_glue_weight_kg = est.mounting_glue_weight_kg,
                est_lamination_weight_kg = est.lamination_weight_kg,
                height_mm = req.product_height_mm,
                length_mm = req.product_length_mm,
                width_mm = req.product_width_mm
            };

            await _orderRepo.AddOrderItemAsync(newItem);
            await _orderRepo.SaveChangesAsync();

            material? paperMaterial = null;
            if (!string.IsNullOrWhiteSpace(est.paper_code))
                paperMaterial = await _materialRepo.GetByCodeAsync(est.paper_code!);

            var qty = (decimal)(newItem.quantity <= 0 ? 1 : newItem.quantity);
            var bomTasks = new List<Task>();

            if (est.sheets_total > 0)
                bomTasks.Add(_bomRepo.AddBomAsync(new bom
                {
                    order_item_id = newItem.item_id,
                    material_id = paperMaterial?.material_id,
                    material_code = Trunc20(est.paper_code ?? "PAPER"),
                    material_name = est.paper_name ?? "Giấy",
                    unit = "tờ",
                    qty_total = est.sheets_total,
                    qty_per_product = est.sheets_total / qty,
                    source_estimate_id = est.estimate_id
                }));

            if (est.ink_weight_kg > 0)
                bomTasks.Add(_bomRepo.AddBomAsync(new bom
                {
                    order_item_id = newItem.item_id,
                    material_code = Trunc20("INK"),
                    material_name = "Mực in",
                    unit = "kg",
                    qty_total = est.ink_weight_kg,
                    qty_per_product = est.ink_weight_kg / qty,
                    source_estimate_id = est.estimate_id
                }));

            if (est.coating_glue_weight_kg > 0)
                bomTasks.Add(_bomRepo.AddBomAsync(new bom
                {
                    order_item_id = newItem.item_id,
                    material_code = Trunc20(est.coating_type ?? "COATING_GLUE"),
                    material_name = "Keo phủ",
                    unit = "kg",
                    qty_total = est.coating_glue_weight_kg,
                    qty_per_product = est.coating_glue_weight_kg / qty,
                    source_estimate_id = est.estimate_id
                }));

            if (est.mounting_glue_weight_kg > 0)
            {
                var codeBoi = string.IsNullOrWhiteSpace(est.wave_type) ? "MOUNTING_GLUE" : $"BOI_{est.wave_type}";
                bomTasks.Add(_bomRepo.AddBomAsync(new bom
                {
                    order_item_id = newItem.item_id,
                    material_code = Trunc20(codeBoi),
                    material_name = "Keo bồi",
                    unit = "kg",
                    qty_total = est.mounting_glue_weight_kg,
                    qty_per_product = est.mounting_glue_weight_kg / qty,
                    source_estimate_id = est.estimate_id
                }));
            }

            if (est.lamination_weight_kg > 0)
                bomTasks.Add(_bomRepo.AddBomAsync(new bom
                {
                    order_item_id = newItem.item_id,
                    material_code = Trunc20("LAMINATION"),
                    material_name = "Màng cán",
                    unit = "kg",
                    qty_total = est.lamination_weight_kg,
                    qty_per_product = est.lamination_weight_kg / qty,
                    source_estimate_id = est.estimate_id
                }));

            await Task.WhenAll(bomTasks);
            await _bomRepo.SaveChangesAsync();

            var bomHasNullMaterial = await (
                from oi in _db.order_items.AsNoTracking()
                join b in _db.boms.AsNoTracking() on oi.item_id equals b.order_item_id
                where oi.order_id == newOrder.order_id
                select b.material_id
            ).AnyAsync(x => x == null);

            bool isEnough;
            if (bomHasNullMaterial)
            {
                isEnough = false;
            }
            else
            {
                isEnough = await (
                    from oi in _db.order_items.AsNoTracking()
                    join b in _db.boms.AsNoTracking() on oi.item_id equals b.order_item_id
                    join m in _db.materials.AsNoTracking() on b.material_id equals m.material_id
                    where oi.order_id == newOrder.order_id
                    group new { oi, b, m } by b.material_id into g
                    select new
                    {
                        Required = g.Sum(x =>
                            ((decimal)x.oi.quantity)
                            * (x.b.qty_per_product ?? 0m)
                            * (1m + ((x.b.wastage_percent ?? 0m) / 100m))
                        ),
                        StockQty = g.Max(x => x.m.stock_qty ?? 0m)
                    }
                ).AllAsync(x => x.StockQty >= x.Required);
            }

            newOrder.is_enough = isEnough;
            _orderRepo.Update(newOrder);
            await _orderRepo.SaveChangesAsync();

            req.order_id = newOrder.order_id;
            await _requestRepo.UpdateAsync(req);
            await _requestRepo.SaveChangesAsync();

            return new ConvertRequestToOrderResponse
            {
                Success = true,
                Message = "Converted order request to order successfully",
                RequestId = requestId,
                OrderId = newOrder.order_id,
                OrderItemId = newItem.item_id,
                OrderCode = newOrder.code
            };
        }

        public Task<ConvertRequestToOrderResponse> ConvertToOrderInCurrentTransactionAsync(int requestId)
        {
            return ConvertToOrderInternalAsync(requestId);
        }

        public Task<int?> GetConsultantScopeUserIdAsync(CancellationToken ct = default)
    => _currentUser.GetConsultantScopeUserIdAsync(ct);

        public Task EnsureCanAccessAssignedRequestAsync(int requestId, CancellationToken ct = default)
            => _currentUser.EnsureCanAccessAssignedRequestAsync(requestId, ct);

        public async Task<bool> UpdateDeliveryNoteAsync(int orderRequestId, string note, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(note))
                note = "";

            return await _requestRepo.UpdateDeliveryNoteAsync(orderRequestId, note.Trim(), ct);
        }
        private async Task<AMMS.Shared.DTOs.User.AssignedConsultantSummaryDto> ResolveAssignedConsultantAsync(CancellationToken ct = default)
        {
            int? assignedConsultantId;

            if (_currentUser.IsConsultant && _currentUser.UserId.HasValue)
            {
                assignedConsultantId = _currentUser.UserId.Value;
            }
            else
            {
                assignedConsultantId = await _requestRepo.GetLeastLoadedConsultantUserIdAsync(ct);
            }

            if (!assignedConsultantId.HasValue)
                throw new InvalidOperationException("Hiện không có consultant nào đang hoạt động để nhận request.");

            var consultant = await _userRepo.GetAssignedConsultantSummaryAsync(assignedConsultantId.Value, ct);

            if (consultant == null)
                throw new InvalidOperationException("Consultant được gán không tồn tại.");

            if (consultant.role_id != 2)
                throw new InvalidOperationException("User được gán không phải consultant.");

            if (consultant.is_active != true)
                throw new InvalidOperationException("Consultant được gán hiện không hoạt động.");

            return consultant;
        }

        public async Task<DateTime?> CalculateAsync(int orderRequestId, CancellationToken ct = default)
        {
            return await _requestRepo.CalculateAsync(orderRequestId, ct);
        }
        public async Task<DateTime?> RecalculateAndPersistAsync(int orderRequestId, CancellationToken ct = default)
        {
            return await _requestRepo.RecalculateAndPersistAsync(orderRequestId, ct);
        }
    }
}
