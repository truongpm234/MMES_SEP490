using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Productions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Application.Services
{
    public class TaskScanService : ITaskScanService
    {
        private readonly AppDbContext _db;
        private readonly ITaskQrTokenService _tokenSvc;
        private readonly ITaskRepository _taskRepo;
        private readonly ITaskLogRepository _logRepo;
        private readonly IProductionRepository _prodRepo;
        private readonly IMachineRepository _machineRepo;
        private readonly IProductionSchedulingService _scheduling;
        private readonly IHubContext<RealtimeHub> _hub;

        public TaskScanService(
            AppDbContext db,
            ITaskQrTokenService tokenSvc,
            ITaskRepository taskRepo,
            ITaskLogRepository logRepo,
            IProductionRepository productionRepo,
            IMachineRepository machineRepo,
            IProductionSchedulingService scheduling,
            IHubContext<RealtimeHub> hub)
        {
            _db = db;
            _tokenSvc = tokenSvc;
            _taskRepo = taskRepo;
            _logRepo = logRepo;
            _prodRepo = productionRepo;
            _machineRepo = machineRepo;
            _scheduling = scheduling;
            _hub = hub;
        }

        public async Task<ScanTaskResult> ScanFinishAsync(ScanTaskRequest req)
        {
            if (!_tokenSvc.TryValidate(req.token, out var taskId, out var qtyGood, out var reason))
                throw new ArgumentException(reason);

            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                try
                {
                    var t = await _taskRepo.GetByIdAsync(taskId)
                        ?? throw new Exception("Task not found");

                    if (!t.prod_id.HasValue || !t.seq_num.HasValue)
                        throw new Exception("Task missing prod_id/seq_num");

                    var flowTasks = await _taskRepo.GetTasksWithCodesByProductionAsync(t.prod_id.Value);
                    var currentFlow = flowTasks.FirstOrDefault(x => x.task_id == t.task_id)
                        ?? throw new Exception("Task flow info not found");

                    var currentCode = ProductionFlowHelper.Norm(currentFlow.process_code);
                    var hasRalo = flowTasks.Any(x => ProductionFlowHelper.IsRalo(x.process_code));
                    var raloFinished = !hasRalo || flowTasks.Any(x =>
                        ProductionFlowHelper.IsRalo(x.process_code) &&
                        string.Equals(x.status, "Finished", StringComparison.OrdinalIgnoreCase));

                    if (!hasRalo)
                    {
                        var prev = await _taskRepo.GetPrevTaskAsync(t.prod_id.Value, t.seq_num.Value);
                        if (prev != null && !string.Equals(prev.status, "Finished", StringComparison.OrdinalIgnoreCase))
                            throw new Exception($"Previous step (task_id={prev.task_id}, seq={prev.seq_num}) is not Finished");
                    }
                    else
                    {
                        if (ProductionFlowHelper.NeedsRaloGate(currentCode) && !raloFinished)
                            throw new Exception("RALO must be Finished before this task can be scanned");
                    }

                    if (!string.Equals(t.status, "Ready", StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"Task status '{t.status}' is not scannable. Only Ready can be finished.");

                    var now = AppTime.NowVnUnspecified();

                    if (!string.IsNullOrWhiteSpace(t.machine))
                        await _machineRepo.ReleaseAsync(t.machine!, release: 1);

                    t.status = "Finished";
                    t.start_time ??= now;
                    t.end_time = now;

                    var log = new task_log
                    {
                        task_id = t.task_id,
                        scanned_code = req.token,
                        action_type = "Finished",
                        qty_good = qtyGood,
                        log_time = now
                    };

                    await _logRepo.AddAsync(log);

                    await _taskRepo.SaveChangesAsync();
                    await _logRepo.SaveChangesAsync();

                    bool promotedNext = false;

                    if (hasRalo)
                    {
                        if (ProductionFlowHelper.IsRalo(currentCode))
                        {
                            promotedNext = await _taskRepo.PromoteAllTasksAfterRaloAsync(t.prod_id.Value, now);
                            await _taskRepo.SaveChangesAsync();
                        }
                    }
                    else
                    {
                        promotedNext = await _taskRepo.PromoteNextTaskToReadyAsync(t.task_id, now);
                        await _taskRepo.SaveChangesAsync();
                    }

                    if (t.prod_id.HasValue)
                    {
                        await _prodRepo.TryCloseProductionIfCompletedAsync(
                            t.prod_id.Value, now, CancellationToken.None);
                    }

                    var prod = await _db.productions
                        .AsTracking()
                        .FirstOrDefaultAsync(p => p.prod_id == t.prod_id.Value);

                    if (prod != null
                        && string.Equals(prod.status, "Finished", StringComparison.OrdinalIgnoreCase)
                        && prod.order_id.HasValue)
                    {
                        var order = await _db.orders
                            .AsTracking()
                            .FirstOrDefaultAsync(o => o.order_id == prod.order_id.Value);

                        if (order != null &&
                            !string.Equals(order.status, "Finished", StringComparison.OrdinalIgnoreCase))
                        {
                            order.status = "Finished";
                            await _db.SaveChangesAsync();
                        }
                    }

                    await tx.CommitAsync();

                    try { await _scheduling.DispatchDueTasksAsync(); } catch { }

                    await _hub.Clients
                        .Group($"prod-{t.prod_id}")
                        .SendAsync("ProdUpdated", new
                        {
                            prodId = t.prod_id,
                            taskId = t.task_id,
                            status = "Finished",
                            promoted_next = promotedNext,
                            flow_gate = hasRalo ? "RALO" : "SEQUENTIAL"
                        });

                    if (prod?.order_id != null)
                    {
                        await _hub.Clients.All.SendAsync("OrderUpdated", new
                        {
                            orderId = prod.order_id,
                            status = prod.status
                        });
                    }

                    return new ScanTaskResult
                    {
                        task_id = t.task_id,
                        prod_id = t.prod_id,
                        message = promotedNext
                            ? $"Finished & logged qty_good={qtyGood}. Flow released."
                            : $"Finished & logged qty_good={qtyGood}."
                    };
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            });
        }

        public async Task<int> SuggestQtyGoodAsync(int taskId, CancellationToken ct = default)
        {
            return await _taskRepo.SuggestQtyGoodAsync(taskId, ct);
        }
    }
}