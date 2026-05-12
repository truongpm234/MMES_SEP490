using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class ScanTaskRequest
    {
        public string token { get; set; } = null!;
        public string? reason { get; set; }
        public string? report_image_url { get; set; }
        public bool use_manual_input { get; set; } = false;
        public List<TaskMaterialUsageInputDto>? materials { get; set; }
        public List<TaskReferenceUsageInputDto>? reference_inputs { get; set; }
        public List<TaskOutputReportDto>? outputs { get; set; }
    }
}
