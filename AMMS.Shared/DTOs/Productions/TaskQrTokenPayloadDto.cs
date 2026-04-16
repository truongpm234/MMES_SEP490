using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class TaskQrTokenPayloadDto
    {
        public int task_id { get; set; }
        public int qty_good { get; set; }
        public long exp_unix { get; set; }
        public List<TaskMaterialUsageInputDto> materials { get; set; } = new();
    }
}

