using AMMS.Application.Helpers;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Shared.DTOs.Socket;
using AMMS.Shared.Helpers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AMMS.API
{
    public class ScanService
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<RealtimeHub> _hub;

        public ScanService(AppDbContext db, IHubContext<RealtimeHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        public async Task ScanAsync(int taskId, string scannedCode, string actionType, int qtyGood, CancellationToken ct)
        {
            var task = await _db.tasks.AsTracking()
                .FirstOrDefaultAsync(x => x.task_id == taskId, ct)
                ?? throw new InvalidOperationException("Task not found");

            if (task.prod_id == null)
                throw new InvalidOperationException("Task missing prod_id");

            var prodId = task.prod_id.Value;

            var orderId = await _db.productions.AsNoTracking()
                .Where(p => p.prod_id == prodId)
                .Select(p => p.order_id)
                .FirstOrDefaultAsync(ct);

            var now = AppTime.NowVnUnspecified();

            _db.task_logs.Add(new task_log
            {
                task_id = taskId,
                scanned_code = scannedCode,
                action_type = actionType,
                qty_good = qtyGood,
                log_time = now
            });

            switch ((actionType ?? "").Trim())
            {
                case "Unassigned":
                    task.status = "Unassigned";
                    task.start_time ??= now;
                    break;

                case "Ready":
                    task.status = "Ready";
                    task.start_time ??= now;
                    break;

                case "Finished":
                    task.status = "Finished";
                    task.end_time = now;
                    break;
            }

            await _db.SaveChangesAsync(ct);

            var totalTasks = await _db.tasks.AsNoTracking()
                .CountAsync(t => t.prod_id == prodId, ct);

            var finishedTasks = await _db.tasks.AsNoTracking()
                .CountAsync(t => t.prod_id == prodId && t.status == "Finished", ct);

            var percent = totalTasks == 0 ? 0 : (int)Math.Round(finishedTasks * 100.0 / totalTasks);

            await _hub.Clients.Group($"prod:{prodId}")
                .SendAsync("tasklog.created", new TaskLogCreatedEvent(
                    task_id: taskId,
                    prod_id: prodId,
                    action_type: actionType,
                    qty_good: qtyGood,
                    log_time: now
                ), ct);

            await _hub.Clients.Group($"prod:{prodId}")
                .SendAsync("task.updated", new TaskUpdatedEvent(
                    task_id: taskId,
                    prod_id: prodId,
                    status: task.status,
                    start_time: task.start_time,
                    end_time: task.end_time
                ), ct);

            await _hub.Clients.Group($"prod:{prodId}")
                .SendAsync("production.progress", new ProductionProgressEvent(
                    prod_id: prodId,
                    order_id: (int)orderId,
                    total_tasks: totalTasks,
                    finished_tasks: finishedTasks,
                    percent: percent
                ), ct);

            if (orderId > 0)
            {
                await _hub.Clients.Group($"order:{orderId}")
                    .SendAsync("production.progress", new ProductionProgressEvent(
                        prod_id: prodId,
                        order_id: (int)orderId,
                        total_tasks: totalTasks,
                        finished_tasks: finishedTasks,
                        percent: percent
                    ), ct);

                await _hub.Clients.Group($"order:{orderId}")
                    .SendAsync("task.updated", new TaskUpdatedEvent(
                        task_id: taskId,
                        prod_id: prodId,
                        status: task.status,
                        start_time: task.start_time,
                        end_time: task.end_time
                    ), ct);
            }
        }
    }
}
