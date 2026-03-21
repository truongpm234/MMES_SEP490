using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class MachineScheduleTaskDto
    {
        public int task_id { get; set; }
        public int? prod_id { get; set; }
        public int? order_id { get; set; }
        public string? order_code { get; set; }

        public int? process_id { get; set; }
        public string? process_code { get; set; }
        public string? process_name { get; set; }

        public int? seq_num { get; set; }
        public string? status { get; set; }

        public string machine_code { get; set; } = "";
        public int lane_no { get; set; }

        public DateTime? planned_start_time { get; set; }
        public DateTime? planned_end_time { get; set; }

        public DateTime? actual_start_time { get; set; }
        public DateTime? actual_end_time { get; set; }
    }
}
