namespace AMMS.Shared.DTOs.Requests
{
    public class UpdateOrderRequest
    {
        public string? customer_name { get; set; }
        public string? customer_phone { get; set; }
        public string? customer_email { get; set; }
        public DateTime? delivery_date { get; set; }
        public string? delivery_date_change_reason { get; set; }
        public string? product_name { get; set; }
        public int? quantity { get; set; }
        public string? description { get; set; }
        public string? design_file_path { get; set; }
        public DateTime? order_request_date { get; set; }
        public string? province { get; set; }
        public string? district { get; set; }
        public string? detail_address { get; set; }
        public string? product_type { get; set; }
        public string? processing_status { get; set; }
        public int? number_of_plates { get; set; }
        public string? paper_code { get; set; }
        public string? paper_name { get; set; }
        public string? coating_type { get; set; }
        public string? wave_type { get; set; }
        public string? paper_alternative { get; set; }
        public string? wave_alternative { get; set; }
        public int? product_length_mm { get; set; }
        public int? product_width_mm { get; set; }
        public int? product_height_mm { get; set; }
        public int? glue_tab_mm { get; set; }
        public int? bleed_mm { get; set; }
        public bool? is_one_side_box { get; set; }
        public int? print_width_mm { get; set; }
        public int? print_length_mm { get; set; }
        public bool? is_send_design { get; set; }
        public string? production_processes { get; set; }
        public decimal? preliminary_estimated_price { get; set; }
        public string? reason { get; set; }
        public string? note { get; set; }
        public string? consultant_note { get; set; }
        public string? message_to_customer { get; set; }
        public string? delivery_note { get; set; }
        public string? print_ready_file { get; set; }
        public string? cost_note { get; set; }
        public string? ink_type_names { get; set; }
        public string? alternative_material_reason { get; set; }
    }
}