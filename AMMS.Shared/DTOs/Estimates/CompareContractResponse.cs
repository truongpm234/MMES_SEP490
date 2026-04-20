namespace AMMS.Shared.DTOs.Estimates
{
    public class CompareContractResponse
    {
        public int request_id { get; set; }
        public int estimate_id { get; set; }

        public bool is_match { get; set; }

        public bool body_text_exact_match { get; set; }
        public decimal similarity_percent { get; set; }

        public bool signature_name_present { get; set; }
        public bool signature_mark_present { get; set; }

        public bool has_digital_signature { get; set; }
        public bool digital_signature_valid { get; set; }

        public bool used_ocr_fallback { get; set; }

        public int consultant_text_length { get; set; }
        public int customer_text_length { get; set; }

        public string? verification_mode { get; set; }
        public string? reject_reason { get; set; }

        public string? debug_signature_box_rect { get; set; }
        public string? debug_signature_name_rect { get; set; }

        public List<TextDifferenceItemDto> text_differences { get; set; } = new();
    }

    public class TextDifferenceItemDto
    {
        public string type { get; set; } = "";
        public int expected_line { get; set; }
        public int actual_line { get; set; }
        public string expected_text { get; set; } = "";
        public string actual_text { get; set; } = "";
    }
}