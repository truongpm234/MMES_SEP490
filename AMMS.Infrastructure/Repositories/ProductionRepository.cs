using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Common;
using AMMS.Shared.DTOs.Productions;
using AMMS.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Infrastructure.Repositories
{
    public class ProductionRepository : IProductionRepository
    {
        private readonly AppDbContext _db;
        private readonly ITaskRepository _taskRepo;

        public ProductionRepository(AppDbContext db, ITaskRepository taskRepo)
        {
            _db = db;
            _taskRepo = taskRepo;
        }

        public async Task<DateTime?> GetNearestDeliveryDateAsync()
        {
            return await (
                from pr in _db.productions.AsNoTracking()
                join o in _db.orders.AsNoTracking() on pr.order_id equals o.order_id
                where pr.actual_start_date != null
                      && pr.end_date == null
                      && o.delivery_date != null
                orderby o.delivery_date
                select o.delivery_date
            ).FirstOrDefaultAsync();
        }

        public Task AddAsync(production p)
        {
            _db.productions.Add(p);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync() => _db.SaveChangesAsync();

        public Task<production?> GetByIdAsync(int prodId)
            => _db.productions.FirstOrDefaultAsync(x => x.prod_id == prodId);

        public async Task<PagedResultLite<ProducingOrderCardDto>> GetProducingOrdersAsync(int page, int pageSize, int? roleId, CancellationToken ct = default)
        {
            NormalizePaging(ref page, ref pageSize);
            var skip = (page - 1) * pageSize;

            var baseRows = await (
                from pr in _db.productions.AsNoTracking()
                join o in _db.orders.AsNoTracking() on pr.order_id equals o.order_id

                join q in _db.quotes.AsNoTracking() on o.quote_id equals q.quote_id into qj
                from q in qj.DefaultIfEmpty()

                join r in _db.order_requests.AsNoTracking()
                    on q.order_request_id equals r.order_request_id into rj
                from r in rj.DefaultIfEmpty()

                where pr.created_at != null && pr.order_id != null
                orderby (pr.planned_start_date ?? pr.created_at) descending, pr.prod_id descending
                select new BaseRow
                {
                    prod_id = pr.prod_id,
                    order_id = o.order_id,
                    code = o.code,
                    delivery_date = o.delivery_date,
                    product_type_id = pr.product_type_id,
                    production_status = pr.status,
                    order_status = o.status,
                    customer_name = !string.IsNullOrWhiteSpace(r.customer_name) ? r.customer_name : "",
                    first_item_product_name = _db.order_items.AsNoTracking()
                        .Where(i => i.order_id == o.order_id)
                        .OrderBy(i => i.item_id)
                        .Select(i => i.product_name)
                        .FirstOrDefault(),

                    first_item_production_process = _db.order_items.AsNoTracking()
                        .Where(i => i.order_id == o.order_id)
                        .OrderBy(i => i.item_id)
                        .Select(i => i.production_process)
                        .FirstOrDefault(),

                    first_item_quantity = _db.order_items.AsNoTracking()
                        .Where(i => i.order_id == o.order_id)
                        .OrderBy(i => i.item_id)
                        .Select(i => (int?)i.quantity)
                        .FirstOrDefault()
                }
            )
            .Skip(skip)
            .Take(pageSize + 1)
            .ToListAsync(ct);

            var hasNext = baseRows.Count > pageSize;
            if (hasNext) baseRows.RemoveAt(baseRows.Count - 1);

            if (baseRows.Count == 0)
            {
                return new PagedResultLite<ProducingOrderCardDto>
                {
                    Page = page,
                    PageSize = pageSize,
                    HasNext = false,
                    Data = new List<ProducingOrderCardDto>()
                };
            }

            var prodIds = baseRows.Select(x => x.prod_id).ToList();

            var taskRows = await _db.tasks.AsNoTracking().Where(t => t.prod_id != null && prodIds.Contains(t.prod_id.Value))
                .Select(t => new TaskRow
                {
                    TaskId = t.task_id,
                    ProdId = t.prod_id!.Value,
                    ProcessId = t.process_id,
                    SeqNum = t.seq_num,
                    Status = t.status,
                    StartTime = t.start_time,
                    EndTime = t.end_time,
                    PlannedStartTime = t.planned_start_time,
                    PlannedEndTime = t.planned_end_time
                })
    .ToListAsync(ct);

            var tasksByProd = taskRows
                .GroupBy(x => x.ProdId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.SeqNum ?? int.MaxValue).ToList()
                );

            var productTypeIds = baseRows
                .Select(x => x.product_type_id)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var stepRows = await _db.product_type_processes.AsNoTracking().Where(p => productTypeIds.Contains(p.product_type_id) && (p.is_active ?? true))
    .Select(p => new StepRow
    {
        ProductTypeId = p.product_type_id,
        ProcessId = p.process_id,
        SeqNum = p.seq_num,
        ProcessName = p.process_name,
        ProcessCode = p.process_code
    })
    .ToListAsync(ct);

            var stepsByProductType = stepRows
                .GroupBy(x => x.ProductTypeId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.SeqNum).ToList()
                );

            var result = new List<ProducingOrderCardDto>();

            foreach (var r in baseRows)
            {
                tasksByProd.TryGetValue(r.prod_id, out var tasks);
                tasks ??= new List<TaskRow>();

                var ptId = r.product_type_id ?? 0;

                stepsByProductType.TryGetValue(ptId, out var steps);
                steps ??= new List<StepRow>();

                steps = ResolveFixedRoute(
                    steps.OrderBy(s => s.SeqNum).ToList(),
                    x => x.ProcessCode,
                    r.first_item_production_process
                );

                var visibleSteps = ProductionSHelper.FilterStepsByRole(steps, roleId);

                if (visibleSteps.Count == 0)
                    continue;

                var stageStatuses = visibleSteps
                    .Select(step =>
                    {
                        var task = tasks.FirstOrDefault(t => t.ProcessId == step.ProcessId)
                                   ?? tasks.FirstOrDefault(t => t.SeqNum == step.SeqNum);

                        return new ProductionStageStatusDto
                        {
                            task_id = task?.TaskId,
                            process_id = step.ProcessId,
                            seq_num = step.SeqNum,
                            process_code = step.ProcessCode,
                            process_name = step.ProcessName,
                            status = ResolveTaskStageStatus(task),
                            start_time = task?.StartTime,
                            end_time = task?.EndTime,
                            planned_start_time = task?.PlannedStartTime,
                            planned_end_time = task?.PlannedEndTime,
                            is_current = false
                        };
                    })
                    .OrderBy(x => x.seq_num ?? int.MaxValue)
                    .ToList();

                var visibleTasks = tasks
                    .Where(t =>
                        visibleSteps.Any(s =>
                            (t.ProcessId.HasValue && t.ProcessId == s.ProcessId) ||
                            (t.SeqNum.HasValue && t.SeqNum == s.SeqNum)))
                    .OrderBy(t => t.SeqNum ?? int.MaxValue)
                    .ToList();

                var currentSeq = GetCurrentSeq(visibleTasks);

                string? currentStage = null;
                string? currentStageStatus = null;

                if (currentSeq.HasValue)
                {
                    var currentStageItem = stageStatuses
                        .FirstOrDefault(x => x.seq_num == currentSeq.Value);

                    if (currentStageItem != null)
                    {
                        currentStage = currentStageItem.process_name;
                        currentStageStatus = currentStageItem.status;
                        currentStageItem.is_current = true;
                    }
                }
                else if (visibleTasks.Count > 0 && visibleTasks.All(x => x.EndTime != null))
                {
                    var lastStage = stageStatuses
                        .OrderBy(x => x.seq_num ?? int.MaxValue)
                        .LastOrDefault();

                    if (lastStage != null)
                    {
                        currentStage = lastStage.process_name;
                        currentStageStatus = lastStage.status;
                        lastStage.is_current = true;
                    }
                }
                else
                {
                    var firstStage = stageStatuses
                        .OrderBy(x => x.seq_num ?? int.MaxValue)
                        .FirstOrDefault();

                    if (firstStage != null)
                    {
                        currentStage = firstStage.process_name;
                        currentStageStatus = firstStage.status;
                        firstStage.is_current = true;
                    }
                }

                var stages = visibleSteps
                    .Select(s => s.ProcessName)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                var progress = ComputeProgressByStages(visibleSteps, currentSeq, visibleTasks);

                result.Add(new ProducingOrderCardDto
                {
                    order_id = r.order_id,
                    code = r.code,
                    customer_name = r.customer_name ?? "",
                    product_name = r.first_item_product_name,
                    quantity = r.first_item_quantity ?? 0,
                    delivery_date = r.delivery_date,
                    progress_percent = progress,
                    current_stage = currentStage,

                    status = r.order_status,
                    production_status = r.production_status,

                    stage_status = currentStageStatus,
                    stages = stages,
                    stage_statuses = stageStatuses
                });
            }

            return new PagedResultLite<ProducingOrderCardDto>
            {
                Page = page,
                PageSize = pageSize,
                HasNext = hasNext,
                Data = result
            };
        }

        public async Task<ProductionProgressResponse> GetProgressAsync(int prodId)
        {
            var tasks = await _db.tasks
                .AsNoTracking()
                .Where(t => t.prod_id == prodId)
                .Select(t => new { t.task_id, t.status })
                .ToListAsync();

            var total = tasks.Count;
            if (total <= 0)
                return new ProductionProgressResponse
                {
                    prod_id = prodId,
                    total_steps = 0,
                    finished_steps = 0,
                    progress_percent = 0
                };

            var taskIds = tasks.Select(x => x.task_id).ToList();

            var finishedTaskIds = await _db.task_logs
                .AsNoTracking()
                .Where(l => l.task_id != null
                    && taskIds.Contains(l.task_id.Value)
                    && l.action_type == "Finished")
                .Select(l => l.task_id!.Value)
                .Distinct()
                .ToListAsync();

            var finished = tasks.Count(t =>
                string.Equals(t.status, "Finished", StringComparison.OrdinalIgnoreCase)
                && finishedTaskIds.Contains(t.task_id));

            var percent = Math.Round((finished * 100m) / total, 1);

            return new ProductionProgressResponse
            {
                prod_id = prodId,
                total_steps = total,
                finished_steps = finished,
                progress_percent = percent
            };
        }

        public async Task<ProductionDetailDto?> GetProductionDetailByOrderIdAsync(int orderId, CancellationToken ct = default)
        {
            var header = await (
                from pr in _db.productions.AsNoTracking()
                join o in _db.orders.AsNoTracking() on pr.order_id equals o.order_id into oj
                from o in oj.DefaultIfEmpty()

                join r in _db.order_requests.AsNoTracking() on o.order_id equals r.order_id into rj
                from r in rj.DefaultIfEmpty()

                join q in _db.quotes.AsNoTracking() on o.quote_id equals q.quote_id into qj
                from q in qj.DefaultIfEmpty()

                where pr.order_id == orderId
                orderby (pr.planned_start_date ?? pr.created_at ?? pr.end_date)
                select new
                {
                    pr,
                    o,
                    customer_name = !string.IsNullOrWhiteSpace(r.customer_name) ? r.customer_name : "Khách hàng",
                    first_item = _db.order_items.AsNoTracking()
                        .Where(i => i.order_id == o.order_id)
                        .OrderBy(i => i.item_id)
                        .Select(i => new
                        {
                            i.item_id,
                            i.product_name,
                            i.quantity,
                            i.production_process,
                            i_length = (int?)EF.Property<int?>(i, "length_mm"),
                            i_width = (int?)EF.Property<int?>(i, "width_mm"),
                            i_height = (int?)EF.Property<int?>(i, "height_mm"),
                            i_ink_weight_kg = (decimal?)i.est_ink_weight_kg
                        })
                        .FirstOrDefault()
                }).FirstOrDefaultAsync(ct);

            if (header == null)
                return null;

            var dto = new ProductionDetailDto
            {
                prod_id = header.pr.prod_id,
                production_code = header.pr.code,
                production_status = header.pr.status,
                created_at = header.pr.created_at,
                planned_start_date = header.pr.planned_start_date,
                actual_start_date = header.pr.actual_start_date,
                end_date = header.pr.end_date,

                order_id = header.o?.order_id,
                order_code = header.o?.code,
                delivery_date = header.o?.delivery_date,
                customer_name = header.customer_name ?? "Khách ẩn tên",

                product_name = header.first_item?.product_name,
                quantity = header.first_item?.quantity ?? 0,

                length_mm = header.first_item?.i_length,
                width_mm = header.first_item?.i_width,
                height_mm = header.first_item?.i_height,
            };

            order_request? orderReq = null;
            cost_estimate? estimate = null;

            int sheetsRequired = 0;
            int sheetsTotal = 0;
            int nUp = 1;

            if (dto.order_id.HasValue)
            {
                orderReq = await _db.order_requests
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.order_id == dto.order_id.Value, ct);

                if (orderReq != null)
                {
                    if (orderReq.accepted_estimate_id.HasValue && orderReq.accepted_estimate_id.Value > 0)
                    {
                        estimate = await _db.cost_estimates
                            .AsNoTracking()
                            .FirstOrDefaultAsync(
                                x => x.estimate_id == orderReq.accepted_estimate_id.Value
                                  && x.order_request_id == orderReq.order_request_id,
                                ct);
                    }

                    estimate ??= await _db.cost_estimates
                        .AsNoTracking()
                        .Where(x => x.order_request_id == orderReq.order_request_id)
                        .OrderByDescending(x => x.is_active)
                        .ThenByDescending(x => x.estimate_id)
                        .FirstOrDefaultAsync(ct);

                    if (estimate != null)
                    {
                        sheetsRequired = estimate.sheets_required;
                        sheetsTotal = estimate.sheets_total;
                        nUp = estimate.n_up > 0 ? estimate.n_up : 1;
                    }
                }
            }

            decimal estInkWeightKg = 0m;
            if (header.first_item?.i_ink_weight_kg.HasValue == true)
                estInkWeightKg = header.first_item.i_ink_weight_kg.Value;

            int? numberOfPlates = orderReq?.number_of_plates;

            var prodId = header.pr.prod_id;

            var tasks = await _db.tasks.AsNoTracking()
                .Where(t => t.prod_id == prodId)
                .Select(t => new
                {
                    t.task_id,
                    t.prod_id,
                    t.seq_num,
                    t.name,
                    t.status,
                    t.machine,
                    t.start_time,
                    t.end_time,
                    t.planned_start_time,
                    t.planned_end_time,
                    t.process_id
                })
                .ToListAsync(ct);

            var lastTask = tasks
                .OrderByDescending(t => t.seq_num ?? 0)
                .ThenByDescending(t => t.task_id)
                .FirstOrDefault();

            if (lastTask != null
                && string.Equals(lastTask.status, "Finished", StringComparison.OrdinalIgnoreCase)
                && lastTask.end_time != null
                && !string.Equals(header.pr.status, "Finished", StringComparison.OrdinalIgnoreCase))
            {
                var prodToUpdate = new production { prod_id = prodId };
                _db.productions.Attach(prodToUpdate);

                prodToUpdate.status = "Finished";
                prodToUpdate.end_date = lastTask.end_time;

                if (header.pr.actual_start_date == null)
                    prodToUpdate.actual_start_date = lastTask.end_time;

                await _db.SaveChangesAsync(ct);

                dto.production_status = "Finished";
                dto.end_date = lastTask.end_time;
                dto.actual_start_date ??= lastTask.end_time;
            }

            var taskIds = tasks.Select(x => x.task_id).ToList();

            var logs = await _db.task_logs.AsNoTracking()
                .Where(l => l.task_id != null && taskIds.Contains(l.task_id.Value))
                .OrderBy(l => l.log_time)
                .Select(l => new TaskLogDto
                {
                    log_id = l.log_id,
                    task_id = l.task_id!.Value,
                    action_type = l.action_type,
                    qty_good = l.qty_good ?? 0,
                    log_time = l.log_time,
                    scanned_code = l.scanned_code
                })
                .ToListAsync(ct);

            var ptId = header.pr.product_type_id;
            List<ProductTypeProcessStepDto> steps = new();

            if (ptId.HasValue)
            {
                steps = await _db.product_type_processes.AsNoTracking()
                    .Where(p => p.product_type_id == ptId.Value && (p.is_active ?? true))
                    .OrderBy(p => p.seq_num)
                    .Select(p => new ProductTypeProcessStepDto
                    {
                        process_id = p.process_id,
                        seq_num = p.seq_num,
                        process_name = p.process_name,
                        process_code = EF.Property<string?>(p, "process_code"),
                        machine = p.machine
                    })
                    .ToListAsync(ct);
            }

            steps = ResolveFixedRoute(
                steps.OrderBy(x => x.seq_num).ToList(),
                x => x.process_code,
                header.first_item?.production_process
            );

            var stages = new List<ProductionStageDto>();
            StageOutputRef? prevOutput = null;

            foreach (var s in steps)
            {
                var pcode = (s.process_code ?? "").Trim().ToUpperInvariant();

                var task = tasks.FirstOrDefault(t => t.process_id == s.process_id)
                           ?? tasks.FirstOrDefault(t => (t.seq_num ?? -1) == s.seq_num);

                var stageLogs = task == null
                    ? new List<TaskLogDto>()
                    : LogsByTaskId(logs, task.task_id);

                var qtyGood = stageLogs.Sum(x => x.qty_good);
                var qtyBad = 0;
                var denom = qtyGood + qtyBad;
                var wastePct = denom <= 0 ? 0m : Math.Round((qtyBad * 100m) / denom, 2);

                var io = BuildStageIO(
                    processCode: pcode,
                    processName: s.process_name ?? "",
                    detail: dto,
                    prevOutput: prevOutput,
                    sheetsRequired: sheetsRequired,
                    sheetsTotal: sheetsTotal,
                    nUp: nUp,
                    qtyGood: qtyGood,
                    numberOfPlates: numberOfPlates,
                    estInkWeightKg: estInkWeightKg
                );

                var stage = new ProductionStageDto
                {
                    process_id = s.process_id,
                    seq_num = s.seq_num,
                    process_name = s.process_name ?? "",
                    process_code = s.process_code,
                    machine = task?.machine ?? s.machine,
                    task_id = task?.task_id,
                    task_name = task?.name,
                    status = task?.status,
                    start_time = task?.start_time,
                    end_time = task?.end_time,
                    qty_good = qtyGood,
                    qty_bad = qtyBad,
                    waste_percent = wastePct,
                    last_scan_time = stageLogs.Count == 0 ? null : stageLogs.Max(x => x.log_time),
                    logs = stageLogs,
                    input_materials = io.inputs,
                    output_product = io.output,
                    planned_start_time = task?.planned_start_time,
                    planned_end_time = task?.planned_end_time
                };

                stages.Add(stage);
                prevOutput = io.nextOutput;
            }

            dto.stages = stages;
            return dto;
        }

        public async Task<ProductionWasteReportDto?> GetProductionWasteAsync(int prodId, CancellationToken ct = default)
        {
            var prod = await _db.productions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.prod_id == prodId, ct);

            if (prod == null) return null;

            var tasks = await _db.tasks.AsNoTracking()
                .Where(t => t.prod_id == prodId)
                .Select(t => new { t.task_id, t.seq_num, t.process_id, t.name })
                .ToListAsync(ct);

            var taskIds = tasks.Select(x => x.task_id).ToList();

            var logs = await _db.task_logs.AsNoTracking()
                .Where(l => l.task_id != null && taskIds.Contains(l.task_id.Value))
                .Select(l => new
                {
                    task_id = l.task_id!.Value,
                    qty_good = l.qty_good ?? 0,
                    log_time = l.log_time
                })
                .ToListAsync(ct);

            var ptId = prod.product_type_id;
            var stepMeta = new Dictionary<int, (string? code, string name, int seq)>();

            if (ptId.HasValue)
            {
                var steps = await _db.product_type_processes.AsNoTracking()
                    .Where(p => p.product_type_id == ptId.Value && (p.is_active ?? true))
                    .Select(p => new
                    {
                        p.process_id,
                        p.process_name,
                        p.seq_num,
                        process_code = (string?)EF.Property<string?>(p, "process_code")
                    })
                    .ToListAsync(ct);

                stepMeta = steps.ToDictionary(
                    x => x.process_id,
                    x => (x.process_code, x.process_name ?? "", x.seq_num)
                );
            }

            var stageRows = new List<StageWasteDto>();

            foreach (var t in tasks.OrderBy(x => x.seq_num ?? int.MaxValue))
            {
                var tlogs = logs.Where(x => x.task_id == t.task_id).ToList();
                var good = tlogs.Sum(x => x.qty_good);

                string pname = t.name;
                string? pcode = null;
                var seq = t.seq_num ?? 0;

                if (t.process_id.HasValue && stepMeta.TryGetValue(t.process_id.Value, out var meta))
                {
                    pname = meta.name;
                    pcode = meta.code;
                    seq = meta.seq;
                }

                stageRows.Add(new StageWasteDto
                {
                    task_id = t.task_id,
                    seq_num = seq,
                    process_name = pname,
                    process_code = pcode,
                    qty_good = good,
                    first_scan = tlogs.Count == 0 ? null : tlogs.Min(x => x.log_time),
                    last_scan = tlogs.Count == 0 ? null : tlogs.Max(x => x.log_time),
                });
            }

            var totalGood = stageRows.Sum(x => (decimal)x.qty_good);
            var totalBad = stageRows.Sum(x => (decimal)x.qty_bad);
            var totalDenom = totalGood + totalBad;
            var totalWastePct = totalDenom <= 0 ? 0m : Math.Round((totalBad * 100m) / totalDenom, 2);

            return new ProductionWasteReportDto
            {
                prod_id = prodId,
                total_good = totalGood,
                total_bad = totalBad,
                total_waste_percent = totalWastePct,
                stages = stageRows.OrderBy(x => x.seq_num).ToList()
            };
        }

        public async Task<bool> TryCloseProductionIfCompletedAsync(int prodId, DateTime now, CancellationToken ct = default)
        {
            var prod = await _db.productions.FirstOrDefaultAsync(p => p.prod_id == prodId, ct);
            if (prod == null) return false;

            var tasks = await _db.tasks
                .Where(t => t.prod_id == prodId)
                .Select(t => new { t.status, t.end_time, t.seq_num })
                .ToListAsync(ct);

            if (tasks.Count == 0) return false;

            bool allFinished = tasks.All(t =>
                string.Equals(t.status, "Finished", StringComparison.OrdinalIgnoreCase)
                || t.end_time != null);

            if (!allFinished) return false;

            var finishedAt = tasks.Where(t => t.end_time != null)
                .Select(t => t.end_time!.Value)
                .DefaultIfEmpty(now)
                .Max();

            prod.end_date = finishedAt;
            prod.status = "Finished";
            if (prod.actual_start_date == null)
                prod.actual_start_date = finishedAt;

            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<int?> StartProductionByOrderIdAndPromoteFirstTaskAsync(int orderId, DateTime now, CancellationToken ct = default)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                var prod = await _db.productions
                    .FirstOrDefaultAsync(p => p.order_id == orderId, ct);

                if (prod == null)
                    return (int?)null;

                prod.status = "InProcessing";
                prod.actual_start_date ??= now;

                if (prod.created_at == null)
                    prod.created_at = now;

                if (prod.planned_start_date == null)
                    prod.planned_start_date = now;

                if (prod.order_id.HasValue)
                {
                    var order = await _db.orders
                        .FirstOrDefaultAsync(o => o.order_id == prod.order_id.Value, ct);

                    if (order != null)
                        order.status = "InProcessing";
                }

                await _db.SaveChangesAsync(ct);

                await _taskRepo.PromoteInitialTasksAsync(prod.prod_id, now, ct);
                await _taskRepo.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
                return prod.prod_id;
            });
        }

        public async Task<bool> StartProductionByOrderIdAsync(int orderId, DateTime now, CancellationToken ct = default)
        {
            var prodId = await StartProductionByOrderIdAndPromoteFirstTaskAsync(orderId, now, ct);
            return prodId.HasValue;
        }

        public async Task<bool> SetProductionDeliveryByOrderIdAsync(int orderId, CancellationToken ct = default)
        {
            var prod = await _db.productions
                .FirstOrDefaultAsync(p => p.order_id == orderId, ct);

            if (prod == null)
                return false;

            var order = await _db.orders
                .FirstOrDefaultAsync(o => o.order_id == orderId, ct);

            if (order == null)
                return false;

            prod.status = "Delivery";
            order.status = "Delivery";

            await _db.SaveChangesAsync(ct);
            return true;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
            => _db.SaveChangesAsync(ct);

        private static string NormalizeProcessCode(string? code)
            => (code ?? "").Trim().ToUpperInvariant();

        private static HashSet<string> ParseSelectedProcessCodes(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => NormalizeProcessCode(x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static List<T> ResolveFixedRoute<T>(
    List<T> allSteps,
    Func<T, string?> processCodeSelector,
    string? selectedProcessesCsv)
        {
            if (allSteps == null || allSteps.Count == 0)
                return new List<T>();

            var selected = ParseSelectedProcessCodes(selectedProcessesCsv);
            if (selected.Count == 0)
                return allSteps;

            var filtered = allSteps
                .Where(x => selected.Contains(NormalizeProcessCode(processCodeSelector(x))))
                .ToList();

            return filtered.Count > 0 ? filtered : allSteps;
        }

        private static (List<StageMaterialDto> inputs, StageMaterialDto output, StageOutputRef nextOutput) BuildStageIO(
            string processCode,
            string processName,
            ProductionDetailDto detail,
            StageOutputRef? prevOutput,
            int sheetsRequired,
            int sheetsTotal,
            int nUp,
            int qtyGood,
            int? numberOfPlates,
            decimal estInkWeightKg)
        {
            var inputs = new List<StageMaterialDto>();
            var code = NormalizeProcessCode(processCode);

            string sizeSuffix =
                detail.length_mm.HasValue && detail.width_mm.HasValue && detail.height_mm.HasValue
                    ? $" ({detail.length_mm}×{detail.width_mm}×{detail.height_mm})mm"
                    : string.Empty;

            var baseSheets = sheetsTotal > 0 ? sheetsTotal : (sheetsRequired > 0 ? sheetsRequired : detail.quantity);
            if (baseSheets <= 0) baseSheets = 1;

            if (code == "RALO" || code == "RA_LO")
            {
                inputs.Add(new StageMaterialDto
                {
                    name = "Cuộn giấy ralo",
                    code = "RALO",
                    quantity = 1,
                    unit = "cuộn"
                });

                var outSheets = qtyGood > 0 ? qtyGood : baseSheets;

                var output = new StageMaterialDto
                {
                    name = $"Giấy đã ralo{sizeSuffix}",
                    code = processCode,
                    quantity = outSheets,
                    unit = "tờ"
                };

                var next = new StageOutputRef
                {
                    Name = output.name ?? "",
                    Code = output.code,
                    Unit = output.unit ?? "tờ",
                    Quantity = outSheets
                };

                return (inputs, output, next);
            }

            if (code == "DAN")
            {
                var danQty = qtyGood > 0 ? qtyGood : (detail.quantity > 0 ? detail.quantity : 1);

                inputs.Add(new StageMaterialDto
                {
                    name = "Phôi đã bế",
                    code = prevOutput?.Code ?? "BE",
                    quantity = detail.quantity > 0 ? detail.quantity : danQty,
                    unit = "sp"
                });

                var output = new StageMaterialDto
                {
                    name = $"Thành phẩm {processName}".Trim(),
                    code = processCode,
                    quantity = detail.quantity > 0 ? detail.quantity : danQty,
                    unit = "sp"
                };

                var next = new StageOutputRef
                {
                    Name = output.name ?? "",
                    Code = output.code,
                    Unit = output.unit ?? "sp",
                    Quantity = output.quantity
                };

                return (inputs, output, next);
            }

            var inputName = prevOutput?.Name ?? (detail.product_name ?? "Bán thành phẩm");
            var inputCode = prevOutput?.Code;
            var inputUnit = prevOutput?.Unit ?? "tờ";

            var inputQty = (qtyGood > 0)
                ? qtyGood
                : (prevOutput?.Quantity > 0 ? prevOutput.Quantity : baseSheets);

            if (inputQty <= 0) inputQty = baseSheets;

            inputs.Add(new StageMaterialDto
            {
                name = inputName,
                code = inputCode,
                quantity = inputQty,
                unit = inputUnit
            });

            if (code == "IN")
            {
                if (numberOfPlates.GetValueOrDefault() > 0)
                {
                    inputs.Add(new StageMaterialDto
                    {
                        name = "Kẽm in",
                        quantity = numberOfPlates.Value,
                        unit = "bản"
                    });
                }

                if (estInkWeightKg > 0)
                {
                    inputs.Add(new StageMaterialDto
                    {
                        name = "Mực các loại",
                        quantity = estInkWeightKg,
                        unit = "kg"
                    });
                }
            }

            var outQty = qtyGood > 0 ? qtyGood : inputQty;
            if (outQty <= 0) outQty = inputQty;

            var outputDefault = new StageMaterialDto
            {
                name = $"Thành phẩm {processName}".Trim(),
                code = processCode,
                quantity = outQty,
                unit = "tờ"
            };

            var nextOutput = new StageOutputRef
            {
                Name = outputDefault.name ?? "",
                Code = outputDefault.code,
                Unit = outputDefault.unit ?? "tờ",
                Quantity = outQty
            };

            return (inputs, outputDefault, nextOutput);
        }

        private static List<TaskLogDto> LogsByTaskId(List<TaskLogDto> all, int taskId)
        {
            return all
                .Where(x => x.task_id == taskId)
                .OrderBy(x => x.log_time)
                .ToList();
        }

        private static int? GetCurrentSeq(List<TaskRow> tasks)
        {
            var inProg = tasks.FirstOrDefault(x => x.StartTime != null && x.EndTime == null);
            if (inProg?.SeqNum != null) return inProg.SeqNum;

            var next = tasks.FirstOrDefault(x => x.EndTime == null);
            if (next?.SeqNum != null) return next.SeqNum;

            return null;
        }

        private static decimal ComputeProgressByStages(
            List<StepRow> steps,
            int? currentSeq,
            List<TaskRow> tasks)
        {
            var total = steps.Count;
            if (total <= 0) return 0m;

            if (tasks.Count > 0 && tasks.All(x => x.EndTime != null))
                return 100m;

            if (!currentSeq.HasValue) return 0m;

            var idx = steps.FindIndex(s => s.SeqNum == currentSeq.Value);
            if (idx < 0) idx = 0;

            var completedBefore = idx;
            var percent = completedBefore * 100m / total;
            return Math.Round(percent, 1);
        }

        private static void NormalizePaging(ref int page, ref int pageSize)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;
            if (pageSize > 200) pageSize = 200;
        }

        private static string ResolveTaskStageStatus(TaskRow? task)
        {
            if (task == null)
                return "Unassigned";

            if (!string.IsNullOrWhiteSpace(task.Status))
                return task.Status!;

            if (task.EndTime != null)
                return "Finished";

            if (task.StartTime != null)
                return "InProcessing";

            return "Unassigned";
        }

        public async Task<List<MachineScheduleBoardDto>> GetMachineScheduleBoardAsync(
    DateTime from,
    DateTime to,
    CancellationToken ct = default)
        {
            if (to <= from)
                to = from.AddDays(1);

            var machines = await _db.machines
                .AsNoTracking()
                .Where(x => x.is_active)
                .OrderBy(x => x.process_code)
                .ThenBy(x => x.machine_code)
                .Select(x => new
                {
                    x.machine_code,
                    x.process_code,
                    x.process_name,
                    x.quantity,
                    busy_quantity = x.busy_quantity ?? 0,
                    free_quantity = x.free_quantity ?? (x.quantity - (x.busy_quantity ?? 0))
                })
                .ToListAsync(ct);

            var rawRows = await _db.tasks
                .AsNoTracking()
                .Where(t => t.machine != null && t.machine != "")
                .Select(t => new
                {
                    TaskId = t.task_id,
                    ProdId = t.prod_id,
                    ProcessId = t.process_id,
                    SeqNum = t.seq_num,
                    Status = t.status,
                    MachineCode = t.machine,
                    PlannedStart = t.planned_start_time,
                    PlannedEnd = t.planned_end_time,
                    ActualStart = t.start_time,
                    ActualEnd = t.end_time,

                    OrderId = _db.productions
                        .Where(pr => pr.prod_id == t.prod_id)
                        .Select(pr => pr.order_id)
                        .FirstOrDefault(),

                    OrderCode = (
                        from pr in _db.productions
                        join o in _db.orders on pr.order_id equals o.order_id
                        where pr.prod_id == t.prod_id
                        select o.code
                    ).FirstOrDefault(),

                    ProcessCode = _db.product_type_processes
                        .Where(p => p.process_id == t.process_id)
                        .Select(p => p.process_code)
                        .FirstOrDefault(),

                    ProcessName = _db.product_type_processes
                        .Where(p => p.process_id == t.process_id)
                        .Select(p => p.process_name)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var rawSlots = rawRows
                .Select(x =>
                {
                    var start = x.PlannedStart ?? x.ActualStart;
                    var end = x.PlannedEnd
                              ?? x.ActualEnd
                              ?? (start.HasValue ? start.Value.AddHours(1) : (DateTime?)null);

                    return new
                    {
                        x.TaskId,
                        x.ProdId,
                        x.OrderId,
                        x.OrderCode,
                        x.ProcessId,
                        x.ProcessCode,
                        x.ProcessName,
                        x.SeqNum,
                        x.Status,
                        x.MachineCode,
                        Start = start,
                        End = end,
                        x.ActualStart,
                        x.ActualEnd
                    };
                })
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.MachineCode) &&
                    x.Start.HasValue &&
                    x.End.HasValue &&
                    x.Start.Value < to &&
                    x.End.Value > from)
                .Select(x => new MachineScheduleTaskDto
                {
                    task_id = x.TaskId,
                    prod_id = x.ProdId,
                    order_id = x.OrderId,
                    order_code = x.OrderCode,

                    process_id = x.ProcessId,
                    process_code = x.ProcessCode,
                    process_name = !string.IsNullOrWhiteSpace(x.ProcessName) ? x.ProcessName : null,

                    seq_num = x.SeqNum,
                    status = x.Status,

                    machine_code = x.MachineCode!,
                    lane_no = 0,

                    planned_start_time = x.Start,
                    planned_end_time = x.End,

                    actual_start_time = x.ActualStart,
                    actual_end_time = x.ActualEnd
                })
                .OrderBy(x => x.machine_code)
                .ThenBy(x => x.planned_start_time)
                .ThenBy(x => x.planned_end_time)
                .ThenBy(x => x.task_id)
                .ToList();

            var slotsByMachine = rawSlots
                .GroupBy(x => x.machine_code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var result = new List<MachineScheduleBoardDto>();

            foreach (var m in machines)
            {
                slotsByMachine.TryGetValue(m.machine_code, out var slots);
                slots ??= new List<MachineScheduleTaskDto>();

                AssignLaneNumbers(slots, Math.Max(1, m.quantity), from);

                result.Add(new MachineScheduleBoardDto
                {
                    machine_code = m.machine_code,
                    process_code = m.process_code,
                    process_name = m.process_name,
                    quantity = m.quantity,
                    busy_quantity = m.busy_quantity,
                    free_quantity = m.free_quantity,
                    from_time = from,
                    to_time = to,
                    slots = slots
                });
            }

            return result;
        }

        private static void AssignLaneNumbers(
            List<MachineScheduleTaskDto> slots,
            int laneCount,
            DateTime anchor)
        {
            if (slots == null || slots.Count == 0)
                return;

            var laneAvailableAt = Enumerable.Repeat(anchor, laneCount).ToArray();

            foreach (var s in slots
                         .OrderBy(x => x.planned_start_time)
                         .ThenBy(x => x.planned_end_time)
                         .ThenBy(x => x.task_id))
            {
                var start = s.planned_start_time ?? anchor;
                var end = s.planned_end_time ?? start.AddHours(1);

                var bestLane = 0;
                var bestAvailable = laneAvailableAt[0];

                for (var i = 0; i < laneAvailableAt.Length; i++)
                {
                    if (laneAvailableAt[i] <= start)
                    {
                        bestLane = i;
                        bestAvailable = laneAvailableAt[i];
                        break;
                    }

                    if (laneAvailableAt[i] < bestAvailable)
                    {
                        bestLane = i;
                        bestAvailable = laneAvailableAt[i];
                    }
                }

                var actualStart = bestAvailable > start ? bestAvailable : start;
                var actualEnd = end > actualStart ? end : actualStart;

                s.lane_no = bestLane + 1;
                laneAvailableAt[bestLane] = actualEnd;
            }
        }
    }
}