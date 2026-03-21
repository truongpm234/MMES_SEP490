using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class MachineScheduleBoardDto
    {
        public string machine_code { get; set; } = "";
        public string? process_code { get; set; }
        public string process_name { get; set; } = "";

        public int quantity { get; set; }
        public int busy_quantity { get; set; }
        public int free_quantity { get; set; }

        public DateTime from_time { get; set; }
        public DateTime to_time { get; set; }

        public List<MachineScheduleTaskDto> slots { get; set; } = new();
    }
}
