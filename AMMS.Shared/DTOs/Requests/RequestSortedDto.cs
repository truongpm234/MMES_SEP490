using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class RequestSortedDto
    {
        public int request_id { get; set; }
        public string? customer_name { get; set; }
        public string? phone { get; set; }
        public string? email { get; set; }
        public DateTime? delivery_date { get; set; }
        public string? product_name { get; set; }
        public int? quantity { get; set; }
        public string? description { get; set; }
        public string? design_file_path { get; set; }
        public string? print_ready_file { get; set; }
        public DateTime? request_date { get; set; }
        public string? detail_address { get; set; }
        public string? status { get; set; }
        public string? product_type { get; set; }
        public int? number_of_plates { get; set; }
        public int? order_id { get; set; }
        public int? quote_id { get; set; }
        public int? product_length_mm { get; set; }
        public int? product_width_mm { get; set; }
        public int? product_height_mm { get; set; }
        public int? glue_tab_mm { get; set; }
        public int? bleed_mm { get; set; }
        public bool? is_one_side_box { get; set; }
        public int? print_width_mm { get; set; }
        public int? print_length_mm { get; set; }
        public bool? is_send_design { get; set; }
        public string? note { get; set; }
        public string? reason { get; set; }
        public int? accepted_estimate_id { get; set; }
        public string? consultant_note { get; set; }
        public DateTime? verified_at { get; set; }
        public DateTime? quote_expires_at { get; set; }
        public string? message_to_customer { get; set; }
        public decimal? preliminary_estimated_price { get; set; }
        public int? assigned_consultant { get; set; }
        public DateTime? assigned_at { get; set; }
        public string? delivery_note { get; set; }
        public int? actual_consultant_user_id { get; set; }
        public string? delivery_date_change_reason { get; set; }
        public DateTime? estimate_finish_date { get; set; }
        public string? assign_name { get; set; }

        // ===== cost_estimate =====
        public int? estimate_id { get; set; }
        public int? estimate_order_request_id { get; set; }
        public decimal? paper_cost { get; set; }
        public int? paper_sheets_used { get; set; }
        public decimal? paper_unit_price { get; set; }
        public decimal? ink_cost { get; set; }
        public decimal? ink_weight_kg { get; set; }
        public decimal? ink_rate_per_m2 { get; set; }
        public decimal? coating_glue_cost { get; set; }
        public decimal? coating_glue_weight_kg { get; set; }
        public decimal? coating_glue_rate_per_m2 { get; set; }
        public string? coating_type { get; set; }
        public decimal? mounting_glue_cost { get; set; }
        public decimal? mounting_glue_weight_kg { get; set; }
        public decimal? mounting_glue_rate_per_m2 { get; set; }
        public decimal? lamination_cost { get; set; }
        public decimal? lamination_weight_kg { get; set; }
        public decimal? lamination_rate_per_m2 { get; set; }
        public decimal? material_cost { get; set; }
        public decimal? base_cost { get; set; }
        public bool? is_rush { get; set; }
        public decimal? rush_percent { get; set; }
        public decimal? rush_amount { get; set; }
        public int? days_early { get; set; }
        public decimal? subtotal { get; set; }
        public decimal? discount_percent { get; set; }
        public decimal? discount_amount { get; set; }
        public decimal? final_total_cost { get; set; }
        public DateTime? estimated_finish_date { get; set; }
        public DateTime? desired_delivery_date { get; set; }
        public DateTime? estimate_created_at { get; set; }
        public int? sheets_required { get; set; }
        public int? sheets_waste { get; set; }
        public int? sheets_total { get; set; }
        public int? n_up { get; set; }
        public decimal? total_area_m2 { get; set; }
        public decimal? design_cost { get; set; }
        public string? cost_note { get; set; }
        public bool? is_active { get; set; }
        public string? paper_code { get; set; }
        public string? paper_name { get; set; }
        public string? wave_type { get; set; }
        public string? paper_alternative { get; set; }
        public string? wave_alternative { get; set; }
        public int? wave_sheets_used { get; set; }
        public string? production_processes { get; set; }
        public decimal? deposit_amount { get; set; }
        public int? previous_estimate_id { get; set; }
        public string? consultant_contract_path { get; set; }
        public string? customer_signed_contract_path { get; set; }
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
        public string? ink_type_names { get; set; }
        public string? alternative_material_reason { get; set; }
    }
}
