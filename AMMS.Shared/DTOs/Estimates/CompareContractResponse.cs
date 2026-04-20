using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates
{
    public class CompareContractResponse
    {
        public int request_id { get; set; }
        public int estimate_id { get; set; }

        // Kết quả tổng
        public bool is_match { get; set; }

        // Nội dung hợp đồng
        public bool body_text_exact_match { get; set; }
        public decimal similarity_percent { get; set; }

        // Vùng ký Bên A
        public bool signature_name_present { get; set; }
        public bool signature_mark_present { get; set; }

        // Chữ ký số / certificate signature
        public bool has_digital_signature { get; set; }
        public bool digital_signature_valid { get; set; }

        // OCR fallback
        public bool used_ocr_fallback { get; set; }

        public int consultant_text_length { get; set; }
        public int customer_text_length { get; set; }

        public string? verification_mode { get; set; }
        public string? reject_reason { get; set; }
    }
}
