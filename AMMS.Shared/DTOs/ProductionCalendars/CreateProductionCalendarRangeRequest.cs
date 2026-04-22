using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.ProductionCalendars
{
    public class CreateProductionCalendarRangeRequest
    {
        public DateTime from_date { get; set; }
        public DateTime to_date { get; set; }

        public string? holiday_name { get; set; }
        public string? holiday_type { get; set; } = "MANUAL";
        public bool is_non_working_day { get; set; } = true;
        public bool? is_manual_override { get; set; } = true;
        public string? note { get; set; }
    }
}