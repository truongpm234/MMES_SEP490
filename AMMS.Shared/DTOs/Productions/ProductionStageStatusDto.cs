using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class ProductionStageStatusDto
    {
        public int? task_id { get; set; }
        public int? process_id { get; set; }
        public int? seq_num { get; set; }
        public string? process_code { get; set; }
        public string? process_name { get; set; }

        public string? status { get; set; }

        public DateTime? start_time { get; set; }
        public DateTime? end_time { get; set; }
        public DateTime? planned_start_time { get; set; }
        public DateTime? planned_end_time { get; set; }

        public bool is_current { get; set; }
    }
}
