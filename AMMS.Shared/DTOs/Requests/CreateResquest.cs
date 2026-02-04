namespace AMMS.Shared.DTOs.Requests
{
    public class CreateResquest
    {
        public string? customer_name { get; set; }

        public string? customer_phone { get; set; }

        public string? customer_email { get; set; }

        public DateTime? delivery_date { get; set; }

        public string? product_name { get; set; } = null!;

        public int? quantity { get; set; }

        public string? description { get; set; }

        public string? design_file_path { get; set; }

        public DateTime? order_request_date { get; set; }

        public string? detail_address { get; set; }

        public bool is_send_design { get; set; }

        public int? product_length_mm { get; set; }

        public int? product_width_mm { get; set; }

        public int? product_height_mm { get; set; }

        public string? paper_name { get; set; }
    }
}
