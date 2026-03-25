using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class TaskQtyPolicyDto
    {
        public int task_id { get; set; }

        public string process_code { get; set; } = "";
        public string process_name { get; set; } = "";

        public string qty_unit { get; set; } = "sp";

        public int min_allowed { get; set; } = 1;
        public int max_allowed { get; set; } = 1;
        public int suggested_qty { get; set; } = 1;

        public int order_qty { get; set; }
        public int sheets_required { get; set; }
        public int sheets_waste { get; set; }
        public int sheets_total { get; set; }
        public int n_up { get; set; }
        public int number_of_plates { get; set; }

        public int happy_case_qty { get; set; }
        public int stage_index { get; set; }
        public int stage_count { get; set; }
    }
}
