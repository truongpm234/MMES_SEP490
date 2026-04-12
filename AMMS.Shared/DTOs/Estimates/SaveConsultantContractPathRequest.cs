using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates
{
    public class SaveConsultantContractPathRequest
    {
        public int request_id { get; set; }
        public int estimate_id { get; set; }
        public string? consultant_contract_path { get; set; }
    }
}
