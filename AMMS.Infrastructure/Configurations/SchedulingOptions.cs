using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Configurations
{
    public class SchedulingOptions
    {
        public int InitialLeadMinutes { get; set; } = 120;
        public int MinimumPlanningLeadMinutes { get; set; } = 60;

        public int LongRouteStageThreshold { get; set; } = 5;
        public int LongRouteExtraLeadMinutes { get; set; } = 30;

        public int LargeOrderQtyThreshold { get; set; } = 10000;
        public int LargeSheetsThreshold { get; set; } = 5000;
        public int LargeOrderExtraLeadMinutes { get; set; } = 45;

        public int HighPlateThreshold { get; set; } = 4;
        public int HighPlateExtraLeadMinutes { get; set; } = 20;

        public int DueDateSafetyHours { get; set; } = 4;
        public int DeliveryCutoffHour { get; set; } = 17;
        public int AnchorSearchStepMinutes { get; set; } = 15;
        public int UrgentLeadFloorMinutes { get; set; } = 0;
        public int shift_start_hour { get; set; } = 8;
        public int shift_hours_per_day { get; set; } = 12;
        public int order_gap_minutes { get; set; } = 45;
        public bool enforce_fifo_by_order_date { get; set; } = true;

        public List<string> holidays { get; set; } = new();
    }
}
