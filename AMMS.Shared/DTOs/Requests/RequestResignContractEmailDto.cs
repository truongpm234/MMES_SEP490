using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class RequestResignContractEmailDto
    {
        public int request_id { get; set; }
        public string? custom_message { get; set; }
    }
}
