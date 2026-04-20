namespace AMMS.Shared.DTOs.Estimates
{
    public sealed class CompareContractResponse
    {
        public int request_id { get; set; }
        public int estimate_id { get; set; }

        public bool is_match { get; set; }

        public bool body_text_exact_match { get; set; }
        public decimal similarity_percent { get; set; }

        public bool signature_name_present { get; set; }
        public bool signature_mark_present { get; set; }
        public bool signature_inside_allowed_box { get; set; }

        public bool has_digital_signature { get; set; }
        public bool digital_signature_valid { get; set; }

        public bool used_ocr_fallback { get; set; }

        public int consultant_text_length { get; set; }
        public int customer_text_length { get; set; }

        public string verification_mode { get; set; } = string.Empty;
        public string? reject_reason { get; set; }

        public List<TextDifferenceItemDto> text_differences { get; set; } = new();

        public string? debug_signature_box_rect { get; set; }
        public string? debug_signature_name_rect { get; set; }
    }

    public sealed class TextDifferenceItemDto
    {
        public string type { get; set; } = "changed";

        public int clause_no { get; set; }
        public string clause_title { get; set; } = string.Empty;

        public decimal similarity_percent { get; set; }
        public string message { get; set; } = string.Empty;

        public string expected_text { get; set; } = string.Empty;
        public string actual_text { get; set; } = string.Empty;
    }
}