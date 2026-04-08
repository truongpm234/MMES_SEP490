using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Productions;
using AMMS.Shared.DTOs.Socket;
using AMMS.Shared.Helpers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Application.Services
{
    public class TaskScanService : ITaskScanService
    {
        private readonly NotificationService _noti;
        private readonly ITaskQrTokenService _tokenSvc;
        private readonly ITaskRepository _taskRepo;
        private readonly ITaskLogRepository _logRepo;
        private readonly IProductionRepository _prodRepo;
        private readonly IMachineRepository _machineRepo;
        private readonly IProductionSchedulingService _scheduling;
        private readonly IHubContext<RealtimeHub> _hub;
        private readonly IRequestRepository _orderRequestRepo;
        private readonly IOrderRepository _orderRepo;
        private readonly AppDbContext _db;

        public TaskScanService(
            NotificationService noti,
            AppDbContext db,
            ITaskQrTokenService tokenSvc,
            ITaskRepository taskRepo,
            ITaskLogRepository logRepo,
            IProductionRepository productionRepo,
            IMachineRepository machineRepo,
            IProductionSchedulingService scheduling,
            IHubContext<RealtimeHub> hub,
            IRequestRepository orderRequestRepo,
            IOrderRepository orderRepo)
        {
            _noti = noti;
            _tokenSvc = tokenSvc;
            _taskRepo = taskRepo;
            _logRepo = logRepo;
            _prodRepo = productionRepo;
            _machineRepo = machineRepo;
            _scheduling = scheduling;
            _hub = hub;
            _orderRequestRepo = orderRequestRepo;
            _orderRepo = orderRepo;
        }

        public async Task<ScanTaskResult> ScanFinishAsync(ScanTaskRequest req, CancellationToken ct = default)
        {
            if (!_tokenSvc.TryValidate(req.token, out var taskId, out var qtyGood, out var reason))
                throw new ArgumentException(reason);

            var policy = await _taskRepo.GetQtyPolicyAsync(taskId, ct);
            if (policy == null)
                throw new InvalidOperationException("Không xác định được policy số lượng cho task.");

            if (qtyGood < policy.min_allowed || qtyGood > policy.max_allowed)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(req.token),
                    $"qty_good={qtyGood} vượt ngoài khoảng cho phép {policy.min_allowed}..{policy.max_allowed} {policy.qty_unit} của công đoạn {policy.process_code}.");
            }

            var result = await _taskRepo.ExecuteInTransactionAsync(async innerCt =>
            {
                var t = await _taskRepo.GetByIdAsync(taskId)
                    ?? throw new Exception("Task not found");

                if (!t.prod_id.HasValue || !t.seq_num.HasValue)
                    throw new Exception("Task missing prod_id/seq_num");

                var flowTasks = await _taskRepo.GetTasksWithCodesByProductionAsync(t.prod_id.Value, innerCt);
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
                await _taskRepo.SaveChangesAsync(innerCt);

                bool promotedNext = await _taskRepo.PromoteNextTaskToReadyAsync(t.task_id, now, innerCt);
                task? nextTask = null;

                if (promotedNext)
                {
                    nextTask = await _db.tasks
                        .Include(x => x.process)
                        .Where(x => x.prod_id == t.prod_id
                            && x.status == "Ready"
                            && x.task_id != t.task_id)
                        .OrderBy(x => x.seq_num)
                        .ThenBy(x => x.task_id)
                        .FirstOrDefaultAsync(innerCt);
                    if (nextTask != null)
                    {
                        await _hub.Clients.Group(RealtimeGroups.ByRole(nextTask.name)).SendAsync("nextTask", new { message = $"Công đoạn {nextTask.name} được bắt đầu" });
                    }

                }

                await _taskRepo.SaveChangesAsync(innerCt);

                if (t.prod_id.HasValue)
                {
                    await _prodRepo.TryCloseProductionIfCompletedAsync(t.prod_id.Value, now, innerCt);
                }

                production? prod = null;
                order? ord = null;

                if (t.prod_id.HasValue)
                    prod = await _prodRepo.GetByIdForUpdateAsync(t.prod_id.Value, innerCt);

                if (prod != null
                    && string.Equals(prod.status, "Finished", StringComparison.OrdinalIgnoreCase)
                    && prod.order_id.HasValue)
                {
                    ord = await _orderRepo.GetByIdForUpdateAsync(prod.order_id.Value, innerCt);

                    if (ord != null &&
                        !string.Equals(ord.status, "Finished", StringComparison.OrdinalIgnoreCase))
                    {
                        ord.status = "Finished";
                    }

                    if (ord != null)
                    {
                        await _orderRequestRepo.MarkProcessStatusFinishedByOrderAsync(
                            ord.order_id,
                            ord.quote_id,
                            innerCt);
                    }

                    await _taskRepo.SaveChangesAsync(innerCt);
                }
                await _hub.Clients
                .Group(RealtimeGroups.ByRole("production manager")).SendAsync("finishedTask", new { message = $"Hoàn thành công đoạn" });
                await _hub.Clients.All.SendAsync("update-ui", new { message = "update UI" });
                return new ScanTaskResult
                {
                    task_id = t.task_id,
                    prod_id = t.prod_id,
                    message = promotedNext
                        ? $"Finished & logged qty_good={qtyGood}. Flow released."
                        : $"Finished & logged qty_good={qtyGood}."
                };
            }, ct);

            try
            {
                await _scheduling.DispatchDueTasksAsync();
            }
            catch
            {
            }



            if (result.prod_id.HasValue)
            {
                var prod = await _prodRepo.GetByIdForUpdateAsync(result.prod_id.Value, ct);
                if (prod?.order_id != null)
                {
                    await _hub.Clients.All.SendAsync("finishedProduction", new { message = $"Đơn hàng {prod.order_id} đã được sản xuất xong" });
                }
            }

            return result;
        }

        public async Task<int> SuggestQtyGoodAsync(int taskId, CancellationToken ct = default)
        {
            var policy = await _taskRepo.GetQtyPolicyAsync(taskId, ct);
            return policy?.suggested_qty ?? 1;
        }
    }
}