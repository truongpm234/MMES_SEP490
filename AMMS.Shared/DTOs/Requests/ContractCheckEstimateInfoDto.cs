using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class ContractCheckEstimateInfoDto
    {
        public int estimate_id { get; set; }
        public string? consultant_contract_path { get; set; }
        public string? customer_signed_contract_path { get; set; }
    }
}
