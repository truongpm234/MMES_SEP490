using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Helpers
{
    public class SchedulingOptions
    {
        public int shift_start_hour { get; set; } = 8;
        public int shift_hours_per_day { get; set; } = 12;

        public List<string> holidays { get; set; } = new();
    }
}
