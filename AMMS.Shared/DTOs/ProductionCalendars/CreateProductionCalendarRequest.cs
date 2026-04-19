using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.ProductionCalendars
{
    public class CreateProductionCalendarRequest
    {
        public DateTime calendar_date { get; set; }
        public string? holiday_name { get; set; }
        public string? holiday_type { get; set; }
        public bool is_non_working_day { get; set; }
        public bool? is_manual_override { get; set; }
        public string? note { get; set; }
    }
}
