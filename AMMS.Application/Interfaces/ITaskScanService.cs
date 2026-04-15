using AMMS.Shared.DTOs.Productions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Interfaces
{
    public interface ITaskScanService
    {
        Task<ScanTaskResult> ScanFinishAsync(ScanTaskRequest req, int? scannedByUserId, CancellationToken ct = default);
        Task<int> SuggestQtyGoodAsync(int taskId, CancellationToken ct = default);
        Task<List<TaskConsumableMaterialDto>> GetConsumableMaterialsForTaskPublicAsync(int taskId, CancellationToken ct = default);
        Task<TaskQrMaterialBundleDto> GetTaskQrMaterialBundleAsync(int taskId, CancellationToken ct = default);
    }
}
