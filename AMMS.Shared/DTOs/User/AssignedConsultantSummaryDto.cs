using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.User
{
    public class AssignedConsultantSummaryDto
    {
        public int user_id { get; set; }
        public string username { get; set; } = "";
        public string? full_name { get; set; }
        public string? email { get; set; }
        public string? phone_number { get; set; }
        public int? role_id { get; set; }
        public bool? is_active { get; set; }
        public DateTime? created_at { get; set; }
    }
}
