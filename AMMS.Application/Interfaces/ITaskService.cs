using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Interfaces
{
    public interface ITaskService
    {
        Task<bool> SetTaskReadyAsync(int taskId, CancellationToken ct = default);
        Task<bool> FinishTaskFromStockAsync(int taskId, int? scannedByUserId = null, CancellationToken ct = default);
    }
}
