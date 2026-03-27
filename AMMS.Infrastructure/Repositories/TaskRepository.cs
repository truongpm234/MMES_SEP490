using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Productions;
using AMMS.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Infrastructure.Repositories
{
    public class TaskRepository : ITaskRepository
    {
        private readonly AppDbContext _db;

        public TaskRepository(AppDbContext db) => _db = db;

        public Task AddRangeAsync(IEnumerable<task> tasks)
        {
            _db.tasks.AddRange(tasks);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync() => _db.SaveChangesAsync();

        public Task SaveChangesAsync(CancellationToken ct)
            => _db.SaveChangesAsync(ct);

        public Task<task?> GetByIdAsync(int taskId)
            => _db.tasks.FirstOrDefaultAsync(x => x.task_id == taskId);

        public Task<task?> GetNextTaskAsync(int prodId, int currentSeqNum)
            => _db.tasks
                .Where(x => x.prod_id == prodId && x.seq_num > currentSeqNum)
                .OrderBy(x => x.seq_num)
                .FirstOrDefaultAsync();

        public Task<task?> GetPrevTaskAsync(int prodId, int seqNum)
        {
            return _db.tasks
                .Where(x => x.prod_id == prodId && x.seq_num < seqNum)
                .OrderByDescending(x => x.seq_num)
                .FirstOrDefaultAsync();
        }

        public async Task<List<task>> GetTasksByProductionAsync(int prodId, CancellationToken ct = default)
        {
            return await _db.tasks
                .Where(x => x.prod_id == prodId)
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .ToListAsync(ct);
        }

        public async Task<task?> GetFirstTaskByProductionAsync(int prodId, CancellationToken ct = default)
        {
            return await _db.tasks
                .Where(x => x.prod_id == prodId)
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<List<TaskFlowDto>> GetTasksWithCodesByProductionAsync(int prodId, CancellationToken ct = default)
        {
            return await _db.tasks
                .AsNoTracking()
                .Where(x => x.prod_id == prodId)
                .Select(x => new TaskFlowDto
                {
                    task_id = x.task_id,
                    prod_id = x.prod_id ?? 0,
                    seq_num = x.seq_num,
                    status = x.status,
                    machine = x.machine,
                    process_code = x.process != null ? x.process.process_code : null
                })
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .ToListAsync(ct);
        }

        public async Task<bool> PromoteInitialTasksAsync(int prodId, DateTime now, CancellationToken ct = default)
        {
            var tasks = await _db.tasks
                .Include(x => x.process)
                .Where(x => x.prod_id == prodId)
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .ToListAsync(ct);

            if (tasks.Count == 0)
                return false;

            var promoted = false;

            var initialTasks = tasks
                .Where(x => !string.Equals(x.status, "Finished", StringComparison.OrdinalIgnoreCase))
                .Where(x => ProductionFlowHelper.IsInitialParallel(x.process?.process_code))
                .ToList();

            if (initialTasks.Count == 0)
            {
                var first = tasks.FirstOrDefault(x => !string.Equals(x.status, "Finished", StringComparison.OrdinalIgnoreCase));
                if (first != null)
                    initialTasks.Add(first);
            }

            foreach (var t in initialTasks)
            {
                if (string.Equals(t.status, "Ready", StringComparison.OrdinalIgnoreCase))
                    continue;

                t.status = "Ready";
                t.start_time ??= now;
                await TryAllocateMachineWhenReadyAsync(t, ct);
                promoted = true;
            }

            return promoted;
        }

        public async Task<bool> PromoteFirstTaskToReadyAsync(int prodId, DateTime now, CancellationToken ct = default)
        {
            return await PromoteInitialTasksAsync(prodId, now, ct);
        }

        public async Task<bool> PromoteAllTasksAfterRaloAsync(int prodId, DateTime now, CancellationToken ct = default)
        {
            var tasks = await _db.tasks
                .Include(x => x.process)
                .Where(x => x.prod_id == prodId)
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .ToListAsync(ct);

            if (tasks.Count == 0)
                return false;

            var ralo = tasks.FirstOrDefault(x => ProductionFlowHelper.IsRalo(x.process?.process_code));
            if (ralo == null || !ralo.seq_num.HasValue)
                return false;

            var promoted = false;

            foreach (var t in tasks.Where(x =>
                         x.seq_num.HasValue &&
                         x.seq_num.Value > ralo.seq_num.Value &&
                         !string.Equals(x.status, "Finished", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.Equals(t.status, "Ready", StringComparison.OrdinalIgnoreCase))
                    continue;

                t.status = "Ready";
                t.start_time ??= now;
                await TryAllocateMachineWhenReadyAsync(t, ct);
                promoted = true;
            }

            return promoted;
        }

        public async Task<bool> PromoteNextTaskToReadyAsync(int currentTaskId, DateTime now, CancellationToken ct = default)
        {
            var current = await _db.tasks
                .Include(x => x.process)
                .FirstOrDefaultAsync(x => x.task_id == currentTaskId, ct);

            if (current == null || !current.prod_id.HasValue || !current.seq_num.HasValue)
                return false;

            var prodId = current.prod_id.Value;
            var currentSeq = current.seq_num.Value;

            var hasOtherReady = await _db.tasks
                .AnyAsync(x =>
                    x.prod_id == prodId &&
                    x.task_id != currentTaskId &&
                    x.status == "Ready", ct);

            if (hasOtherReady)
                return false;

            var next = await _db.tasks
                .Where(x =>
                    x.prod_id == prodId &&
                    x.seq_num > currentSeq &&
                    x.status != "Finished")
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .FirstOrDefaultAsync(ct);

            if (next == null)
                return false;

            next.status = "Ready";
            next.start_time ??= now;
            await TryAllocateMachineWhenReadyAsync(next, ct);

            return true;
        }

        public async Task<TaskQtyPolicyDto?> GetQtyPolicyAsync(int taskId, CancellationToken ct = default)
        {
            const int TokenQtyMax = 0xFFFF; // 65535

            var taskRow = await _db.tasks
                .AsNoTracking()
                .Where(x => x.task_id == taskId)
                .Select(x => new
                {
                    x.task_id,
                    x.prod_id,
                    x.process_id,
                    x.seq_num
                })
                .FirstOrDefaultAsync(ct);

            if (taskRow == null || !taskRow.prod_id.HasValue)
                return null;

            var prod = await _db.productions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.prod_id == taskRow.prod_id.Value, ct);

            if (prod == null)
                return null;

            order? ord = null;
            if (prod.order_id.HasValue)
            {
                ord = await _db.orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.order_id == prod.order_id.Value, ct);
            }

            order_request? req = null;

            if (ord?.quote_id is int quoteId && quoteId > 0)
            {
                req = await (
                    from q in _db.quotes.AsNoTracking()
                    join r in _db.order_requests.AsNoTracking()
                        on q.order_request_id equals r.order_request_id
                    where q.quote_id == quoteId
                    select r
                ).FirstOrDefaultAsync(ct);
            }

            req ??= prod.order_id.HasValue
                ? await _db.order_requests
                    .AsNoTracking()
                    .Where(x => x.order_id == prod.order_id.Value)
                    .OrderByDescending(x => x.order_request_id)
                    .FirstOrDefaultAsync(ct)
                : null;

            var currentStep = taskRow.process_id.HasValue
                ? await _db.product_type_processes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.process_id == taskRow.process_id.Value, ct)
                : null;

            int? productTypeId = prod.product_type_id ?? currentStep?.product_type_id;

            if (!productTypeId.HasValue && !string.IsNullOrWhiteSpace(req?.product_type))
            {
                productTypeId = await _db.product_types
                    .AsNoTracking()
                    .Where(x => x.code == req!.product_type)
                    .Select(x => (int?)x.product_type_id)
                    .FirstOrDefaultAsync(ct);
            }

            if (!productTypeId.HasValue)
                return null;

            cost_estimate? est = null;
            if (req != null)
            {
                var estQuery = _db.cost_estimates
                    .AsNoTracking()
                    .Where(x => x.order_request_id == req.order_request_id);

                if (req.accepted_estimate_id.HasValue && req.accepted_estimate_id.Value > 0)
                {
                    est = await estQuery
                        .FirstOrDefaultAsync(x => x.estimate_id == req.accepted_estimate_id.Value, ct);
                }

                est ??= await estQuery
                    .OrderByDescending(x => x.is_active)
                    .ThenByDescending(x => x.estimate_id)
                    .FirstOrDefaultAsync(ct);
            }

            var itemProcessCsv = prod.order_id.HasValue
                ? await _db.order_items
                    .AsNoTracking()
                    .Where(x => x.order_id == prod.order_id.Value)
                    .OrderBy(x => x.item_id)
                    .Select(x => x.production_process)
                    .FirstOrDefaultAsync(ct)
                : null;

            var allSteps = await _db.product_type_processes
                .AsNoTracking()
                .Where(x => x.product_type_id == productTypeId.Value && (x.is_active ?? true))
                .OrderBy(x => x.seq_num)
                .ToListAsync(ct);

            if (allSteps.Count == 0)
                return null;

            var route = ResolveFixedRoute(
                allSteps,
                x => x.process_code,
                !string.IsNullOrWhiteSpace(itemProcessCsv) ? itemProcessCsv : est?.production_processes);

            if (route.Count == 0)
                route = allSteps;

            var currentIndex = route.FindIndex(x =>
                (taskRow.process_id.HasValue && x.process_id == taskRow.process_id.Value) ||
                (taskRow.seq_num.HasValue && x.seq_num == taskRow.seq_num.Value));

            if (currentIndex < 0)
                currentIndex = 0;

            var stage = route[currentIndex];
            var pcode = Norm(stage.process_code);
            var pname = string.IsNullOrWhiteSpace(stage.process_name)
                ? pcode
                : stage.process_name!;

            var orderQty = SafePositive(req?.quantity ?? 0, 1);

            var sheetsRequired = Math.Max(est?.sheets_required ?? 0, 0);
            var sheetsWaste = Math.Max(est?.sheets_waste ?? 0, 0);
            var sheetsTotal = Math.Max(est?.sheets_total ?? 0, sheetsRequired + sheetsWaste);
            var nUp = SafePositive(est?.n_up ?? 0, 1);
            var numberOfPlates = SafePositive(req?.number_of_plates ?? 0, 1);

            if (sheetsRequired <= 0)
            {
                sheetsRequired = Math.Max(1, (int)Math.Ceiling(orderQty / (decimal)nUp));
            }

            if (sheetsTotal <= 0)
            {
                sheetsTotal = sheetsRequired + sheetsWaste;
            }

            if (sheetsTotal <= 0)
            {
                sheetsTotal = sheetsRequired;
            }

            var happyCaseQty = Math.Max(orderQty, SafeMul(sheetsRequired, nUp));
            var maxProductQty = Math.Max(happyCaseQty, SafeMul(Math.Max(sheetsTotal, 1), nUp));
            var clampedMaxProductQty = Math.Min(maxProductQty, TokenQtyMax);

            var maxSheetQty = Math.Min(Math.Max(sheetsTotal, Math.Max(sheetsRequired, 1)), TokenQtyMax);

            var finalSuggestedTarget = Math.Min(
                clampedMaxProductQty,
                Math.Max(orderQty, (int)Math.Ceiling(orderQty * 1.10m)));

            var routeCodes = route.Select(x => Norm(x.process_code)).ToList();
            var cutIndex = routeCodes.FindIndex(x => x == "CAT");

            if (IsRalo(pcode))
            {
                var minPlateQty = Math.Max(1, numberOfPlates);
                var maxPlateQty = minPlateQty + 1;

                minPlateQty = Math.Min(minPlateQty, TokenQtyMax);
                maxPlateQty = Math.Min(maxPlateQty, TokenQtyMax);

                return new TaskQtyPolicyDto
                {
                    task_id = taskId,
                    process_code = pcode,
                    process_name = pname,
                    qty_unit = "bản",

                    min_allowed = minPlateQty,
                    max_allowed = maxPlateQty,
                    suggested_qty = minPlateQty,

                    order_qty = orderQty,
                    sheets_required = sheetsRequired,
                    sheets_waste = sheetsWaste,
                    sheets_total = sheetsTotal,
                    n_up = nUp,
                    number_of_plates = numberOfPlates,

                    happy_case_qty = minPlateQty,
                    stage_index = currentIndex,
                    stage_count = route.Count
                };
            }

            var isSheetStage = cutIndex >= 0
    ? currentIndex <= cutIndex
    : IsSheetBasedStage(pcode);

            if (pcode == "CAT")
            {
                var minCutQty = Math.Max(1, sheetsRequired);
                var maxCutQty = Math.Max(minCutQty, sheetsTotal);

                minCutQty = Math.Min(minCutQty, TokenQtyMax);
                maxCutQty = Math.Min(maxCutQty, TokenQtyMax);

                return new TaskQtyPolicyDto
                {
                    task_id = taskId,
                    process_code = pcode,
                    process_name = pname,
                    qty_unit = "tờ",

                    min_allowed = minCutQty,
                    max_allowed = maxCutQty,
                    suggested_qty = maxCutQty,

                    order_qty = orderQty,
                    sheets_required = sheetsRequired,
                    sheets_waste = sheetsWaste,
                    sheets_total = sheetsTotal,
                    n_up = nUp,
                    number_of_plates = numberOfPlates,

                    happy_case_qty = Math.Max(sheetsRequired, 1),
                    stage_index = currentIndex,
                    stage_count = route.Count
                };
            }

            if (isSheetStage)
            {
                return new TaskQtyPolicyDto
                {
                    task_id = taskId,
                    process_code = pcode,
                    process_name = pname,
                    qty_unit = "tờ",
                    min_allowed = 1,
                    max_allowed = maxSheetQty,
                    suggested_qty = maxSheetQty,
                    order_qty = orderQty,
                    sheets_required = sheetsRequired,
                    sheets_waste = sheetsWaste,
                    sheets_total = sheetsTotal,
                    n_up = nUp,
                    number_of_plates = numberOfPlates,
                    happy_case_qty = Math.Min(Math.Max(sheetsRequired, 1), TokenQtyMax),
                    stage_index = currentIndex,
                    stage_count = route.Count
                };
            }

            var productStageIndexes = BuildProductStageIndexes(routeCodes, cutIndex);
            if (productStageIndexes.Count == 0)
            {
                productStageIndexes.Add(currentIndex);
            }

            var position = productStageIndexes.IndexOf(currentIndex);
            if (position < 0)
                position = Math.Max(0, productStageIndexes.Count - 1);

            var suggestedQty = ComputeProgressiveSuggestedQty(
                maxQty: clampedMaxProductQty,
                finalSuggestedQty: finalSuggestedTarget,
                position: position,
                count: productStageIndexes.Count);

            suggestedQty = Clamp(suggestedQty, 1, clampedMaxProductQty);

            return new TaskQtyPolicyDto
            {
                task_id = taskId,
                process_code = pcode,
                process_name = pname,
                qty_unit = "sp",
                min_allowed = 1,
                max_allowed = clampedMaxProductQty,
                suggested_qty = suggestedQty,
                order_qty = orderQty,
                sheets_required = sheetsRequired,
                sheets_waste = sheetsWaste,
                sheets_total = sheetsTotal,
                n_up = nUp,
                number_of_plates = numberOfPlates,
                happy_case_qty = happyCaseQty,
                stage_index = currentIndex,
                stage_count = route.Count
            };
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var result = await action(ct);
                    await tx.CommitAsync(ct);
                    return result;
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            });
        }

        public async Task<int> SuggestQtyGoodAsync(int taskId, CancellationToken ct = default)
        {
            var policy = await GetQtyPolicyAsync(taskId, ct);
            return policy?.suggested_qty ?? 1;
        }

        private async Task TryAllocateMachineWhenReadyAsync(task t, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(t.machine))
                return;

            var m = await _db.machines
                .FirstOrDefaultAsync(x => x.machine_code == t.machine && x.is_active, ct);

            if (m == null)
                return;

            m.busy_quantity ??= 0;
            m.free_quantity ??= (m.quantity - m.busy_quantity.Value);

            if (m.free_quantity <= 0)
                return;

            m.free_quantity -= 1;
            m.busy_quantity += 1;
        }

        private static int SafeInt(int v, int fallback = 1) => v > 0 ? v : fallback;

        private static string Norm(string? code)
    => (code ?? "").Trim().ToUpperInvariant();

        private static bool IsRalo(string? code)
            => Norm(code) is "RALO" or "RA_LO";

        private static bool IsSheetBasedStage(string? code)
            => Norm(code) is "IN" or "PHU" or "CAN" or "BOI";

        private static int SafePositive(int value, int fallback = 1)
            => value > 0 ? value : fallback;

        private static int SafeMul(int a, int b)
        {
            try
            {
                return checked(a * b);
            }
            catch
            {
                return int.MaxValue;
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int ComputeProgressiveSuggestedQty(
            int maxQty,
            int finalSuggestedQty,
            int position,
            int count)
        {
            if (count <= 1)
                return finalSuggestedQty;

            if (position <= 0)
                return maxQty;

            if (position >= count - 1)
                return finalSuggestedQty;

            var extra = maxQty - finalSuggestedQty;
            if (extra <= 0)
                return finalSuggestedQty;

            var reduction = (int)Math.Ceiling(extra * (position / (decimal)(count - 1)));
            var value = maxQty - reduction;

            return value < finalSuggestedQty ? finalSuggestedQty : value;
        }

        private static List<int> BuildProductStageIndexes(IReadOnlyList<string> routeCodes, int cutIndex)
        {
            if (cutIndex >= 0 && cutIndex + 1 < routeCodes.Count)
            {
                return Enumerable.Range(cutIndex + 1, routeCodes.Count - (cutIndex + 1)).ToList();
            }

            return routeCodes
                .Select((code, index) => new { code, index })
                .Where(x => x.code is "BE" or "DUT" or "DAN")
                .Select(x => x.index)
                .ToList();
        }

        private static HashSet<string> ParseSelectedProcessCodes(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => Norm(x))
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
                .Where(x => selected.Contains(Norm(processCodeSelector(x))))
                .ToList();

            return filtered.Count > 0 ? filtered : allSteps;
        }
    }
}