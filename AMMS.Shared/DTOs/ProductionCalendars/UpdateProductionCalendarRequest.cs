using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.ProductionCalendars
{
    public class UpdateProductionCalendarRequest
    {
        public string? holiday_name { get; set; }
        public string? holiday_type { get; set; }
        public string? note { get; set; }
    }
}
