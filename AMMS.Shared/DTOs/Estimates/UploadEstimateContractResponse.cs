using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates
{
    public class UploadEstimateContractResponse
    {
        public int request_id { get; set; }
        public int estimate_id { get; set; }
        public string? contract_file_path { get; set; }
        public string? contract_public_id { get; set; }
        public string? original_file_name { get; set; }
        public string? preview_png_url { get; set; }
        public string? preview_jpg_url { get; set; }
        public string? view_url { get; set; }
    }
}
