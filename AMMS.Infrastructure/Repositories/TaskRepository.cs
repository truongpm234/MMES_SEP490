using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
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

        public async Task<bool> PromoteFirstTaskToReadyAsync(int prodId, DateTime now, CancellationToken ct = default)
        {
            var tasks = await _db.tasks
                .Where(x => x.prod_id == prodId)
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .ToListAsync(ct);

            if (tasks.Count == 0)
                return false;

            if (tasks.Any(x => string.Equals(x.status, "Ready", StringComparison.OrdinalIgnoreCase)))
                return true;

            foreach (var t in tasks)
            {
                if (!string.Equals(t.status, "Finished", StringComparison.OrdinalIgnoreCase))
                {
                    t.status = "Unassigned";
                    t.start_time = null;
                }
            }

            var first = tasks.FirstOrDefault(x => !string.Equals(x.status, "Finished", StringComparison.OrdinalIgnoreCase));
            if (first == null)
                return false;

            first.status = "Ready";
            first.start_time ??= now;

            return true;
        }

        public async Task<bool> PromoteNextTaskToReadyAsync(int currentTaskId, DateTime now, CancellationToken ct = default)
        {
            var current = await _db.tasks
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
                return true;

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

            return true;
        }

        public async Task<int> SuggestQtyGoodAsync(int taskId, CancellationToken ct = default)
        {
            var row = await (
                from t in _db.tasks.AsNoTracking()
                join pr in _db.productions.AsNoTracking() on t.prod_id equals pr.prod_id
                join o in _db.orders.AsNoTracking() on pr.order_id equals o.order_id
                join q in _db.quotes.AsNoTracking() on o.quote_id equals q.quote_id into qj
                from q in qj.DefaultIfEmpty()
                join req in _db.order_requests.AsNoTracking() on q.order_request_id equals req.order_request_id into rj
                from req in rj.DefaultIfEmpty()
                join ce in _db.cost_estimates.AsNoTracking() on req.order_request_id equals ce.order_request_id into cej
                from ce in cej.DefaultIfEmpty()
                join ptp in _db.product_type_processes.AsNoTracking() on t.process_id equals ptp.process_id into ptpj
                from ptp in ptpj.DefaultIfEmpty()
                where t.task_id == taskId
                select new
                {
                    process_code = ptp.process_code,
                    order_qty = (int?)req.quantity,
                    sheets_total = (int?)ce.sheets_total,
                    sheets_required = (int?)ce.sheets_required,
                    n_up = (int?)ce.n_up,
                }
            ).FirstOrDefaultAsync(ct);

            if (row == null)
                return 1;

            var pcode = (row.process_code ?? "").Trim().ToUpperInvariant();

            var orderQty = row.order_qty ?? 0;
            var sheetsTotal = row.sheets_total ?? 0;
            var sheetsRequired = row.sheets_required ?? 0;

            if (pcode == "DAN")
                return SafeInt(orderQty, 1);

            return SafeInt(
                sheetsTotal,
                fallback: SafeInt(sheetsRequired, fallback: SafeInt(orderQty, 1))
            );
        }

        private static int SafeInt(int v, int fallback = 1) => v > 0 ? v : fallback;
    }
}