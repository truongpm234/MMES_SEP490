using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public sealed class TaskFlowDto
    {
        public int task_id { get; set; }
        public int prod_id { get; set; }
        public int? seq_num { get; set; }
        public string? status { get; set; }
        public string? machine { get; set; }
        public string? process_code { get; set; }
    }
}
