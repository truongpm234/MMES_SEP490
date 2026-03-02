using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class SubmitForApprovalRequestDto
    {
        public int request_id { get; set; }
        public string? consultant_note { get; set; }
    }
}
