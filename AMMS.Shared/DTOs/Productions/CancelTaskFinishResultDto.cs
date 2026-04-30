using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class CancelTaskFinishResultDto
    {
        public int task_id { get; set; }
        public int? prod_id { get; set; }
        public int deleted_log_count { get; set; }
        public int reversed_stock_move_count { get; set; }

        public string task_status { get; set; } = "Ready";
        public string? production_status { get; set; }
        public string? order_status { get; set; }
        public string? request_status { get; set; }

        public string message { get; set; } = "";
    }
}
