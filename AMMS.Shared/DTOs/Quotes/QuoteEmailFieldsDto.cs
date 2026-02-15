namespace AMMS.Shared.DTOs.Quotes
{
    public class QuoteEmailFieldsDto
    {
        // IDs
        public int order_request_id { get; set; }
        public int estimate_id { get; set; }
        public int quote_id { get; set; }

        // Order/request info
        public string? detail_address { get; set; }
        public string delivery { get; set; } = "N/A";              
        public string request_date_text { get; set; } = "N/A";  
        public string customer_name { get; set; } = "";
        public string? customer_phone { get; set; }
        public string? customer_email { get; set; }

        // Product info
        public string product_name { get; set; } = "";
        public int quantity { get; set; }
        public string paper_name { get; set; } = "N/A";
        public string coating_type { get; set; } = "N/A";
        public string wave_type { get; set; } = "N/A";
        public string design_type { get; set; } = "";
        public string production_process_text { get; set; } = "Không áp dụng";

        // Costs (RAW numbers)
        public decimal material_cost { get; set; }
        public decimal labor_cost { get; set; }
        public decimal other_fees { get; set; }
        public decimal rush_amount { get; set; }
        public decimal subtotal { get; set; }
        public decimal final_total { get; set; }
        public decimal discount_percent { get; set; }
        public decimal discount_amount { get; set; }
        public decimal deposit { get; set; }

        // Expiry
        public DateTime expired_at { get; set; }
        public string expired_at_text { get; set; } = "";           
        public string? order_detail_url { get; set; }

        // Formatted money
        public string material_cost_text { get; set; } = "0 đ";
        public string labor_cost_text { get; set; } = "0 đ";
        public string other_fees_text { get; set; } = "0 đ";
        public string rush_amount_text { get; set; } = "0 đ";
        public string subtotal_text { get; set; } = "0 đ";
        public string final_total_text { get; set; } = "0 đ";
        public string discount_amount_text { get; set; } = "0 đ";
        public string deposit_text { get; set; } = "0 đ";
    }
}
