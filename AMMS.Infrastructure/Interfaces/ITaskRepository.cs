using AMMS.Infrastructure.Entities;
using AMMS.Shared.DTOs.Productions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Interfaces
{
    public interface ITaskRepository
    {
        Task AddRangeAsync(IEnumerable<task> tasks);
        Task SaveChangesAsync();
        Task SaveChangesAsync(CancellationToken ct);
        Task<task?> GetByIdAsync(int taskId);
        Task<task?> GetNextTaskAsync(int prodId, int currentSeqNum);
        Task<task?> GetPrevTaskAsync(int prodId, int seqNum);
        Task<List<task>> GetTasksByProductionAsync(int prodId, CancellationToken ct = default);
        Task<task?> GetFirstTaskByProductionAsync(int prodId, CancellationToken ct = default);
        Task<bool> PromoteFirstTaskToReadyAsync(int prodId, DateTime now, CancellationToken ct = default);
        Task<bool> PromoteNextTaskToReadyAsync(int currentTaskId, DateTime now, CancellationToken ct = default);
        Task<List<TaskFlowDto>> GetTasksWithCodesByProductionAsync(int prodId, CancellationToken ct = default);
        Task<bool> PromoteInitialTasksAsync(int prodId, DateTime now, CancellationToken ct = default);
        Task<bool> PromoteAllTasksAfterRaloAsync(int prodId, DateTime now, CancellationToken ct = default);
        Task<TaskQtyPolicyDto?> GetQtyPolicyAsync(int taskId, CancellationToken ct = default);
        Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default);
        Task<int> SuggestQtyGoodAsync(int taskId, CancellationToken ct = default);
        Task<bool> SetTaskReadyAsync(int taskId, CancellationToken ct = default);
    }
}
