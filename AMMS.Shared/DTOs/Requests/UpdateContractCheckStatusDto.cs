using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class UpdateContractCheckStatusDto
    {
        public int request_id { get; set; }
        public bool? is_check_contract { get; set; }
        public string? note { get; set; }
    }
}