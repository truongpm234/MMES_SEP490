using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Email
{
    public class QuoteEmailPreviewResponse
    {
        public int order_request_id { get; set; }
        public int estimate_id { get; set; }
        public int quote_id { get; set; }
        public string? customer_name { get; set; }
        public string? customer_phone { get; set; }
        public string? customer_email { get; set; }
        public string? detail_address { get; set; }
        public DateTime? delivery_date { get; set; }
        public DateTime? order_request_date { get; set; }
        public string? product_name { get; set; }
        public int quantity { get; set; }
        public string paper_name { get; set; } = "N/A";
        public string coating_type { get; set; } = "N/A";
        public string wave_type { get; set; } = "N/A";
        public bool is_send_design { get; set; }
        public DateTime quote_created_at { get; set; }
        public DateTime quote_expired_at { get; set; }
        public decimal material_cost { get; set; }     
        public decimal labor_cost { get; set; }         
        public decimal other_fees { get; set; }      
        public decimal rush_amount { get; set; }   
        public decimal subtotal { get; set; }       
        public decimal final_total { get; set; }        
        public decimal discount_percent { get; set; }  
        public decimal discount_amount { get; set; }    
        public decimal deposit { get; set; }          
        public string design_type_text { get; set; } = "";
        public string production_process_text { get; set; } = "";
        public string delivery_text { get; set; } = "";
        public string request_date_text { get; set; } = "";
        public string quote_expired_at_text { get; set; } = "";
        public string? order_detail_url { get; set; }
        public bool is_customer_copy { get; set; }
        public string? email_html { get; set; }
        public string? consultant_contract_path { get; set; }
        public string? customer_signed_contract_path { get; set; }
    }

}
