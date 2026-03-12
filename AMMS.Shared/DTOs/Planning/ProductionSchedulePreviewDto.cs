using System;
using System.Collections.Generic;

namespace AMMS.Shared.DTOs.Planning
{
    public class ProductionSchedulePreviewDto
    {
        public int order_request_id { get; set; }
        public DateTime? desired_delivery_date { get; set; }
        public DateTime estimated_finish_date { get; set; }
        public List<ProductionStagePlanPreviewDto> stages { get; set; } = new();
    }

    public class ProductionStagePlanPreviewDto
    {
        public int process_id { get; set; }
        public int seq_num { get; set; }
        public string process_name { get; set; } = "";
        public string process_code { get; set; } = "";
        public string machine_code { get; set; } = "";
        public string unit { get; set; } = "";
        public decimal required_units { get; set; }
        public decimal effective_capacity_per_hour { get; set; }
        public int setup_minutes { get; set; }
        public int handoff_minutes { get; set; }
        public DateTime planned_start_time { get; set; }
        public DateTime planned_end_time { get; set; }
    }
}