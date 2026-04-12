using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates
{
    public class GenerateConsultantContractResponse
    {
        public int request_id { get; set; }
        public int estimate_id { get; set; }
        public decimal vat_percent { get; set; }
        public decimal subtotal_before_vat { get; set; }
        public decimal vat_amount { get; set; }
        public decimal final_total_cost { get; set; }
        public decimal deposit_amount { get; set; }
        public decimal remaining_amount { get; set; }
        public string consultant_contract_path { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }
}
