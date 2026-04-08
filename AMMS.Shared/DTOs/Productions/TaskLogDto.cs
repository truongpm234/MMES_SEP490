using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public sealed class TaskLogDto
    {
        public int log_id { get; set; }
        public int? task_id { get; set; }
        public string? action_type { get; set; }
        public int qty_good { get; set; }
        public int qty_bad { get; set; }
        public int? operator_id { get; set; }
        public DateTime? log_time { get; set; }
        public string? scanner_id { get; set; }
        public string? scanned_code { get; set; }
        public int? scanned_by_user_id { get; set; }
    }
}
