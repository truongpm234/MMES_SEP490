using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public sealed class StageWasteDto
    {
        public int? task_id { get; set; }
        public int seq_num { get; set; }
        public string process_name { get; set; } = "";
        public string? process_code { get; set; }

        public int qty_good { get; set; }
        public decimal waste_percent { get; set; }

        public DateTime? first_scan { get; set; }
        public DateTime? last_scan { get; set; }
    }
}
