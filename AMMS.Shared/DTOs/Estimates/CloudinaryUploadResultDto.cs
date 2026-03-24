using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates
{
    public class CloudinaryUploadResultDto
    {
        public string public_id { get; set; } = "";
        public string secure_url { get; set; } = "";
        public string resource_type { get; set; } = "";
        public string format { get; set; } = "";
        public string? preview_png_url { get; set; }
        public string? preview_jpg_url { get; set; }
        public string original_file_name { get; set; } = "";
    }
}
