using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Application.Services
{
    public class TaskService : ITaskService
    {
        private readonly ITaskRepository _taskRepo;
        private readonly AppDbContext _db;

        public TaskService(ITaskRepository taskRepo, AppDbContext db)
        {
            _taskRepo = taskRepo;
            _db = db;
        }

        public async Task<bool> SetTaskReadyAsync(int taskId, CancellationToken ct = default)
        {
            var current = await _taskRepo.GetByIdWithProcessAsync(taskId, ct);
            if (current == null)
                return false;

            if (!current.prod_id.HasValue || !current.seq_num.HasValue)
                throw new InvalidOperationException("Task thiếu prod_id hoặc seq_num.");

            // validate production/order readiness
            var prod = await _db.productions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.prod_id == current.prod_id.Value, ct);

            if (prod == null)
                throw new InvalidOperationException("Không tìm thấy production của task.");

            if (!prod.order_id.HasValue)
                throw new InvalidOperationException("Production chưa gắn với order.");

            var order = await _db.orders
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.order_id == prod.order_id.Value, ct);

            if (order == null)
                throw new InvalidOperationException("Không tìm thấy order của production.");

            if (!order.layout_confirmed)
                throw new InvalidOperationException("Order chưa xác nhận layout, không thể bắt đầu công đoạn.");

            if (!order.is_production_ready)
                throw new InvalidOperationException("Order chưa được xác nhận sẵn sàng sản xuất, không thể bắt đầu công đoạn.");

            if (string.Equals(current.status, "Finished", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Task đã Finished, không thể chuyển lại Ready.");

            if (string.Equals(current.status, "Ready", StringComparison.OrdinalIgnoreCase))
                return true;

            var flowTasks = await _taskRepo.GetTasksByProductionWithProcessAsync(current.prod_id.Value, ct);
            if (flowTasks.Count == 0)
                throw new InvalidOperationException("Không tìm thấy flow task của production.");

            var currentFlow = flowTasks.FirstOrDefault(x => x.task_id == current.task_id);
            if (currentFlow == null)
                throw new InvalidOperationException("Task không thuộc flow hiện tại.");

            var currentCode = ProductionFlowHelper.Norm(currentFlow.process?.process_code);
            var isInitialParallel = ProductionFlowHelper.IsInitialParallel(currentCode);

            // Chỉ cho start từ trạng thái ban đầu
            if (!string.IsNullOrWhiteSpace(current.status) &&
                !string.Equals(current.status, "Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Chỉ task đang ở trạng thái Unassigned mới được bấm Ready. Hiện tại: {current.status}");
            }

            if (!isInitialParallel)
            {
                var currentSeq = currentFlow.seq_num ?? int.MaxValue;

                var previousUnfinished = flowTasks
                    .Where(x => x.task_id != currentFlow.task_id)
                    .Where(x => (x.seq_num ?? int.MaxValue) < currentSeq)
                    .Where(x => !string.Equals(x.status, "Finished", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.seq_num)
                    .ThenBy(x => x.task_id)
                    .FirstOrDefault();

                if (previousUnfinished != null)
                {
                    var prevName = previousUnfinished.name ?? previousUnfinished.process?.process_name ?? "previous task";
                    var prevCode = previousUnfinished.process?.process_code ?? "";
                    throw new InvalidOperationException(
                        $"Công đoạn trước [{prevCode} - {prevName}] chưa Finished nên chưa thể bắt đầu công đoạn này.");
                }
            }

            var now = AppTime.NowVnUnspecified();
            await _taskRepo.MarkTaskReadyAsync(taskId, now, ct);
            return true;
        }
    }
}