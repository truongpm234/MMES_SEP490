using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Machines
{
    public class MachineAvailabilityLineDto
    {
        public string machine_code { get; set; } = "";
        public string? process_code { get; set; }
        public string? process_name { get; set; }
        public int quantity { get; set; }
        public int busy_now { get; set; }
        public int free_now { get; set; }
        public DateTime generated_at { get; set; }
        public DateTime earliest_any_lane_free_at { get; set; }
        public DateTime all_lanes_free_at { get; set; }
        public List<DateTime> lane_free_times { get; set; } = new();
    }

}
