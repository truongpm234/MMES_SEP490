using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Email
{
    public class QuoteEmailComparePreviewResponse
    {
        public int order_request_id { get; set; }
        public string? customer_name { get; set; }
        public string? customer_phone { get; set; }
        public string? customer_email { get; set; }
        public string? detail_address { get; set; }
        public DateTime? delivery_date { get; set; }
        public DateTime? order_request_date { get; set; }
        public string? product_name { get; set; }
        public int quantity { get; set; }
        public bool is_send_design { get; set; }
        public DateTime? estimate_finish_date { get; set; }
        public List<QuoteEmailPreviewResponse> quotes { get; set; } = new();
    }
}
