using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMMS.Application.Services
{
    public class ProductionSchedulingService : IProductionSchedulingService
    {
        private readonly AppDbContext _db;
        private readonly IProductionRepository _prodRepo;
        private readonly IProductTypeProcessRepository _ptpRepo;
        private readonly IMachineRepository _machineRepo;
        private readonly ITaskRepository _taskRepo;
        private readonly WorkCalendar _cal;

        public ProductionSchedulingService(AppDbContext db, IProductionRepository prodRepo, IProductTypeProcessRepository ptpRepo, IMachineRepository machineRepo, ITaskRepository taskRepo, WorkCalendar cal)
        {
            _db = db;
            _prodRepo = prodRepo;
            _ptpRepo = ptpRepo;
            _machineRepo = machineRepo;
            _taskRepo = taskRepo;
            _cal = cal;
        }

        public async Task<int> ScheduleOrderAsync(int orderId, int productTypeId, string? productionProcessCsv, int? managerId = 3)
        {
            var selected = ParseProcessCsv(productionProcessCsv);

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                var now = AppTime.NowVnUnspecified();

                var order = await _db.orders
                    .AsTracking()
                    .FirstOrDefaultAsync(o => o.order_id == orderId);

                if (order == null)
                    throw new Exception($"Order {orderId} not found");

                production? prod = null;

                if (order.production_id.HasValue)
                {
                    prod = await _db.productions
                        .AsTracking()
                        .FirstOrDefaultAsync(p =>
                            p.prod_id == order.production_id.Value &&
                            p.end_date == null);
                }

                if (prod == null)
                {
                    prod = await _db.productions
                        .AsTracking()
                        .Where(p => p.order_id == orderId && p.end_date == null)
                        .OrderByDescending(p => p.prod_id)
                        .FirstOrDefaultAsync();
                }

                if (prod == null)
                {
                    prod = new production
                    {
                        code = "TMP-PROD",
                        order_id = orderId,
                        manager_id = managerId,
                        status = "Scheduled",
                        product_type_id = productTypeId,
                        start_date = now
                    };

                    await _db.productions.AddAsync(prod);
                    await _db.SaveChangesAsync();

                    prod.code = $"PROD-{prod.prod_id:00000}";
                    await _db.SaveChangesAsync();
                }

                if (order.production_id != prod.prod_id)
                {
                    order.production_id = prod.prod_id;
                    await _db.SaveChangesAsync();
                }

                var hasTask = await _db.tasks
                    .AsNoTracking()
                    .AnyAsync(t => t.prod_id == prod.prod_id);

                if (hasTask)
                {
                    await tx.CommitAsync();
                    return prod.prod_id;
                }

                // Load routing
                var steps = await _ptpRepo.GetActiveByProductTypeIdAsync(productTypeId);
                if (steps == null || steps.Count == 0)
                    throw new Exception("No routing found");

                // ✅ lọc theo CSV nếu có
                if (selected.Count > 0)
                {
                    steps = steps
                        .Where(s =>
                        {
                            var code = (s.process_code ?? "").Trim().ToUpperInvariant();
                            return !string.IsNullOrWhiteSpace(code) && selected.Contains(code);
                        })
                        .ToList();

                    if (steps.Count == 0)
                        throw new Exception("No routing after applying productionProcessCsv filter");
                }

                var firstSeq = steps.Min(x => x.seq_num);
                var tasks = new List<task>();

                var (orderQty, sheetsTotal, sheetsRequired, nUp) = await LoadPlanningNumbersByOrderIdAsync(orderId, CancellationToken.None);
                var plannedQtySheets = GetPlannedQtySheets_AllInSheets(sheetsTotal, sheetsRequired, orderQty);
                DateTime? prevPlannedEnd = null;

                foreach (var s in steps.OrderBy(x => x.seq_num))
                {
                    var pcode = (s.process_code ?? "").Trim().ToUpperInvariant();
                    string? machineCode = null;

                    if (!string.IsNullOrWhiteSpace(pcode))
                    {
                        var best = await _machineRepo.FindBestMachineByProcessCodeAsync(pcode, CancellationToken.None);
                        machineCode = best?.machine_code;
                    }

                    var isFirst = s.seq_num == firstSeq;

                    DateTime? plannedStart = null;
                    DateTime? plannedEnd = null;

                    if (!string.IsNullOrWhiteSpace(machineCode))
                    {
                        var machineFreeAt = await GetMachineAvailableAtAsync(machineCode, now, CancellationToken.None);

                        var startCandidate = now;
                        if (prevPlannedEnd.HasValue && prevPlannedEnd.Value > startCandidate) startCandidate = prevPlannedEnd.Value;
                        if (machineFreeAt > startCandidate) startCandidate = machineFreeAt;

                        plannedStart = _cal.NormalizeStart(startCandidate);

                        var hours = await EstimateDurationHoursByMachineAsync(machineCode, plannedQtySheets, CancellationToken.None);
                        plannedEnd = _cal.AddWorkingHours(plannedStart.Value, hours);

                        prevPlannedEnd = plannedEnd;
                    }
                    else
                    {
                        var startCandidate = prevPlannedEnd.HasValue && prevPlannedEnd.Value > now ? prevPlannedEnd.Value : now;
                        plannedStart = _cal.NormalizeStart(startCandidate);
                        plannedEnd = _cal.AddWorkingHours(plannedStart.Value, 1.0);
                        prevPlannedEnd = plannedEnd;
                    }

                    tasks.Add(new task
                    {
                        prod_id = prod.prod_id,
                        process_id = s.process_id,
                        seq_num = s.seq_num,
                        name = s.process_name,
                        status = isFirst ? "Ready" : "Unassigned",
                        start_time = isFirst ? now : null,
                        machine = machineCode,
                        planned_start_time = plannedStart,
                        planned_end_time = plannedEnd
                    });
                }

                await _taskRepo.AddRangeAsync(tasks);
                await _taskRepo.SaveChangesAsync();

                var firstTask = tasks.FirstOrDefault(t => t.seq_num == firstSeq);
                if (firstTask != null && !string.IsNullOrWhiteSpace(firstTask.machine))
                {
                    await _machineRepo.AllocateAsync(firstTask.machine!, need: 1);
                }

                await tx.CommitAsync();
                return prod.prod_id;
            });
        }
        private async Task<DateTime> GetMachineAvailableAtAsync(string machineCode, DateTime now, CancellationToken ct)
        {
            var lastPlannedEnd = await _db.tasks.AsNoTracking()
                .Where(t => t.machine == machineCode && t.end_time == null && t.planned_end_time != null)
                .MaxAsync(t => (DateTime?)t.planned_end_time, ct);

            if (lastPlannedEnd == null) return now;
            return lastPlannedEnd.Value > now ? lastPlannedEnd.Value : now;
        }

        private async Task<double> EstimateDurationHoursByMachineAsync(string machineCode, int plannedQtySheets, CancellationToken ct)
        {
            var m = await _db.machines.AsNoTracking()
                .Where(x => x.machine_code == machineCode)
                .Select(x => new
                {
                    x.capacity_per_hour,
                    x.efficiency_percent
                })
                .FirstOrDefaultAsync(ct);

            if (m == null) return 1.0; // fallback

            var eff = m.efficiency_percent <= 0 ? 100m : m.efficiency_percent;
            var capPerHour = (decimal)m.capacity_per_hour * (eff / 100m);

            if (capPerHour <= 0) return 1.0;

            var hours = (decimal)plannedQtySheets / capPerHour;
            if (hours <= 0) hours = 0.1m;

            return (double)Math.Round(hours, 4);
        }

        private static int GetPlannedQtySheets_AllInSheets(int sheetsTotal, int sheetsRequired, int orderQty)
        {
            return SafeInt(sheetsTotal, fallback: SafeInt(sheetsRequired, fallback: SafeInt(orderQty, 1)));
        }

        private async Task<(int orderQty, int sheetsTotal, int sheetsRequired, int nUp)> LoadPlanningNumbersByOrderIdAsync(int orderId, CancellationToken ct)
        {
            var row = await (
                from o in _db.orders.AsNoTracking()
                join q in _db.quotes.AsNoTracking() on o.quote_id equals q.quote_id into qj
                from q in qj.DefaultIfEmpty()
                join req in _db.order_requests.AsNoTracking() on q.order_request_id equals req.order_request_id into rj
                from req in rj.DefaultIfEmpty()
                join ce in _db.cost_estimates.AsNoTracking() on req.order_request_id equals ce.order_request_id into cej
                from ce in cej.DefaultIfEmpty()
                where o.order_id == orderId
                select new
                {
                    order_qty = (int?)req.quantity,
                    sheets_total = (int?)ce.sheets_total,
                    sheets_required = (int?)ce.sheets_required,
                    n_up = (int?)ce.n_up
                }
            ).FirstOrDefaultAsync(ct);

            if (row == null) return (0, 0, 0, 1);

            return (
                row.order_qty ?? 0,
                row.sheets_total ?? 0,
                row.sheets_required ?? 0,
                row.n_up.HasValue && row.n_up.Value > 0 ? row.n_up.Value : 1
            );
        }

        private static int SafeInt(int v, int fallback = 1) => v > 0 ? v : fallback;

        private static HashSet<string> ParseProcessCsv(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToUpperInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
