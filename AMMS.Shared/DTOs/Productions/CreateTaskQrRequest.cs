using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class CreateTaskQrRequest
    {
        public int task_id { get; set; }
        public int ttl_minutes { get; set; } = 60;
        public int? qty_good { get; set; }
        public bool use_manual_input { get; set; } = false;
        public List<TaskMaterialUsageInputDto> materials { get; set; } = new();
    }

}
