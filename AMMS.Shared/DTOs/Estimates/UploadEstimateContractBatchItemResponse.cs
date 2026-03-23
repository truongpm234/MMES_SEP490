using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates
{
    public class UploadEstimateContractBatchItemResponse
    {
        public int file_index { get; set; }
        public string original_file_name { get; set; } = "";
        public int estimate_id { get; set; }
        public string contract_file_path { get; set; } = "";
    }
}
