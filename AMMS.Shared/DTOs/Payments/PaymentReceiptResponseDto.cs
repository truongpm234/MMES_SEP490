using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Payments
{
    public class PaymentReceiptCompanyDto
    {
        public string company_name { get; set; } = "";
        public string address { get; set; } = "";
        public string phone { get; set; } = "";
        public string email { get; set; } = "";
        public string tax_code { get; set; } = "";
        public string bank_account { get; set; } = "";
        public string bank_name { get; set; } = "";
    }

    public class PaymentReceiptResponseDto
    {
        public PaymentReceiptCompanyDto company_info { get; set; } = new();

        public string receipt_no { get; set; } = "";
        public DateTime receipt_date { get; set; }

        public int payment_id { get; set; }
        public string provider { get; set; } = "";
        public string payment_type { get; set; } = "";
        public string payment_type_display { get; set; } = "";
        public string payment_status { get; set; } = "";
        public string currency { get; set; } = "VND";

        public long payos_order_code { get; set; }
        public string? payos_payment_link_id { get; set; }
        public string? payos_transaction_id { get; set; }

        public int order_request_id { get; set; }
        public int? order_id { get; set; }
        public string? business_order_code { get; set; }
        public int? quote_id { get; set; }
        public int? estimate_id { get; set; }

        public string? customer_name { get; set; }
        public string? customer_phone { get; set; }
        public string? customer_email { get; set; }
        public string? customer_address { get; set; }

        public string? product_name { get; set; }
        public int? quantity { get; set; }

        public string? receipt_content { get; set; }
        public string? collected_by { get; set; }
        public string? note { get; set; }

        public decimal total_order_value { get; set; }
        public decimal deposit_required { get; set; }
        public decimal amount_received { get; set; }
        public string amount_received_in_words { get; set; } = "";
        public decimal paid_before_this_receipt { get; set; }
        public decimal cumulative_paid { get; set; }
        public decimal remaining_after_this_receipt { get; set; }
    }
}