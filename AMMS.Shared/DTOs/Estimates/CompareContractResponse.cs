using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;
using System.Collections.Generic;

namespace AMMS.Shared.DTOs.Estimates
{
    public class CompareContractResponse
    {
        public int request_id { get; set; }
        public int estimate_id { get; set; }

        public bool is_match { get; set; }

        // Body compare
        public bool body_text_exact_match { get; set; }
        public decimal similarity_percent { get; set; }

        // Signature detection
        public bool signature_name_present { get; set; }
        public bool signature_mark_present { get; set; }
        public bool signature_inside_allowed_box { get; set; }
        public bool signature_outside_allowed_box { get; set; }

        // Digital signature
        public bool has_digital_signature { get; set; }
        public bool digital_signature_valid { get; set; }

        // OCR/debug
        public bool used_ocr_fallback { get; set; }
        public string verification_mode { get; set; } = "";
        public string? reject_reason { get; set; }

        public int consultant_text_length { get; set; }
        public int customer_text_length { get; set; }

        public string? ocr_name_band_text { get; set; }
        public SignatureDebugInfoDto? signature_debug { get; set; }

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

    public class SignatureDebugInfoDto
    {
        public RectDto customer_panel { get; set; } = new();
        public RectDto allowed_signature_box { get; set; } = new();
        public RectDto observation_box { get; set; } = new();
        public RectDto name_band { get; set; } = new();
        public RectDto detected_signature_bounds { get; set; } = new();

        public double inside_ratio { get; set; }
        public double outside_ratio { get; set; }

        public string? debug_root { get; set; }
        public string? rendered_page_path { get; set; }
        public string? observation_crop_path { get; set; }
        public string? name_band_crop_path { get; set; }
    }

    public class RectDto
    {
        public int x { get; set; }
        public int y { get; set; }
        public int width { get; set; }
        public int height { get; set; }
    }
}