using AMMS.Shared.DTOs.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class CreateRequestResponse
    {
        public string message { get; set; } = "Create order successfully";
        public int order_request_id { get; set; }
        public int? assigned_consultant { get; set; }
        public DateTime? assigned_at { get; set; }
        public string? assign_name { get; set; }
        public AssignedConsultantSummaryDto? assigned_consultant_user { get; set; }
    }
}
