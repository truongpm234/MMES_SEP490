using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class RequestWithCostDto
    {
        public int order_request_id { get; set; }
        public string? customer_name { get; set; }
        public string? customer_phone { get; set; }
        public string? customer_email { get; set; }
        public DateTime? delivery_date { get; set; }
        public string? delivery_date_change_reason { get; set; }
        public bool? is_check_contract { get; set; }
        public string? product_name { get; set; }
        public int? quantity { get; set; }
        public string? description { get; set; }
        public string? design_file_path { get; set; }
        public DateTime? order_request_date { get; set; }
        public string? detail_address { get; set; }
        public string? process_status { get; set; }
        public string? product_type { get; set; }
        public int? number_of_plates { get; set; }
        public int? actual_consultant_user_id { get; set; }
        public string? production_processes { get; set; }
        public string? alternative_material_reason { get; set; }
        public string? coating_type { get; set; }
        public string? paper_code { get; set; }
        public string? paper_name { get; set; }
        public string? wave_type { get; set; }
        public string? paper_alternative { get; set; }
        public string? wave_alternative { get; set; }
        public string? ink_type_names { get; set; }
        public DateTime? verified_at { get; set; }
        public DateTime? quote_expires_at { get; set; }
        public int? order_id { get; set; }
        public int? quote_id { get; set; }
        public long order_code { get; set; }
        public int? product_length_mm { get; set; }
        public int? product_width_mm { get; set; }
        public int? product_height_mm { get; set; }
        public int? glue_tab_mm { get; set; }
        public int? bleed_mm { get; set; }
        public bool? is_one_side_box { get; set; }
        public int? print_width_mm { get; set; }
        public int? print_length_mm { get; set; }
        public bool? is_send_design { get; set; }
        public string? reason { get; set; }
        public string? message_to_customer { get; set; }
        public decimal? final_total_cost { get; set; }
        public decimal? deposit_amount { get; set; }
        public decimal? preliminary_estimated_price { get; set; }
        public string? consultant_contract_path { get; set; }
        public string? customer_signed_contract_path { get; set; }
        public DateTime? estimate_finish_date { get; set; }
        public string? contract_check_note { get; set; }
        public string? assign_name { get; set; }
        public string? deposit_receipt_path { get; set; }
        public string? remaining_receipt_path { get; set; }
        public int? lamination_material_id { get; set; }
        public string? lamination_material_code { get; set; }
        public string? lamination_material_name { get; set; }
    }
}
