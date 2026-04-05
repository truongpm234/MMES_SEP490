using AMMS.Shared.DTOs.Estimates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class RequestWithTwoEstimatesDto
    {
        public int order_request_id { get; set; }
        public string customer_name { get; set; } = "";
        public string customer_phone { get; set; } = "";
        public string? customer_email { get; set; }
        public DateTime? delivery_date { get; set; }
        public string? delivery_date_change_reason { get; set; }
        public string? consultant_note { get; set; }
        public string? message_to_customer { get; set; }
        public bool? is_check_contract { get; set; }
        public string product_name { get; set; } = "";
        public int quantity { get; set; }
        public string? description { get; set; }
        public string? detail_address { get; set; }
        public string? product_type { get; set; }
        public int? number_of_plates { get; set; }
        public string? print_ready_file { get; set; }
        public int? product_length_mm { get; set; }
        public int? product_width_mm { get; set; }
        public int? product_height_mm { get; set; }
        public int? glue_tab_mm { get; set; }
        public int? bleed_mm { get; set; }
        public bool? is_one_side_box { get; set; }
        public int? print_width_mm { get; set; }
        public int? print_length_mm { get; set; }
        public bool? is_send_design { get; set; }

        public List<CostEstimateCompareDto> estimates { get; set; } = new();
    }

    public class CostEstimateCompareDto
    {
        public int estimate_id { get; set; }
        public int? previous_estimate_id { get; set; }
        public bool is_active { get; set; }
        public decimal paper_cost { get; set; }
        public decimal ink_cost { get; set; }
        public string? ink_type_names { get; set; }
        public string? alternative_material_reason { get; set; }
        public decimal coating_glue_cost { get; set; }
        public decimal mounting_glue_cost { get; set; }
        public decimal lamination_cost { get; set; }
        public decimal material_cost { get; set; }
        public decimal base_cost { get; set; }
        public bool is_rush { get; set; }
        public decimal rush_percent { get; set; }
        public decimal rush_amount { get; set; }
        public decimal subtotal { get; set; }
        public decimal discount_percent { get; set; }
        public decimal discount_amount { get; set; }
        public decimal final_total_cost { get; set; }
        public decimal deposit_amount { get; set; }
        public DateTime created_at { get; set; }
        public DateTime estimated_finish_date { get; set; }
        public DateTime desired_delivery_date { get; set; }
        public int sheets_required { get; set; }
        public int sheets_waste { get; set; }
        public int sheets_total { get; set; }
        public int n_up { get; set; }
        public decimal total_area_m2 { get; set; }
        public string? production_processes { get; set; }
        public decimal design_cost { get; set; }
        public string? paper_code { get; set; }
        public string? paper_name { get; set; }
        public string? coating_type { get; set; }
        public string? wave_type { get; set; }
        public string? paper_alternative { get; set; }
        public string? wave_alternative { get; set; }
        public int? wave_sheet_used { get; set; }
        public string? cost_note { get; set; }
        public string? contract_file_path { get; set; }
        public DateTime? contract_uploaded_at { get; set; }
        public int? waste_gluing_boxes { get; set; }
        public decimal? sheet_area_m2 { get; set; }
        public int? print_sheets_used { get; set; }
        public decimal? total_coating_area_m2 { get; set; }
        public decimal? total_lamination_area_m2 { get; set; }
        public int? coating_sheets_used { get; set; }
        public int? lamination_sheets_used { get; set; }
        public decimal? wave_sheet_area_m2 { get; set; }
        public int? wave_n_up { get; set; }
        public int? wave_sheets_required { get; set; }
        public decimal? total_mounting_area_m2 { get; set; }
        public decimal? wave_unit_price { get; set; }
        public decimal? wave_cost { get; set; }
        public decimal? total_process_cost { get; set; }
        public List<EstimateProcessCostDto> process_costs { get; set; } = new();
    }
}