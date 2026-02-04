using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class RequestApprovalUpdateDto
    {
        public int request_id { get; set; }
        public string? note { get; set; }
        public string status { get; set; } = null!;  // Processing | Verified | Declined
    }

}
