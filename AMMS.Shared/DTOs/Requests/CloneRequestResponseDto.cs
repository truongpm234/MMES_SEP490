using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class CloneRequestResponseDto
    {
        public int source_request_id { get; set; }
        public int cloned_request_id { get; set; }
        public List<int> cloned_estimate_ids { get; set; } = new();
        public string message { get; set; } = "Cloned request successfully";
    }
}
