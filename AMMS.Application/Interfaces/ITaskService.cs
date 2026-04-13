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
    }
}
