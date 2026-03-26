using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates
{
    public class CostSummaryByRequestDto
    {
        public int order_request_id { get; set; }
        public decimal final_total_cost { get; set; }
        public decimal deposit_amount { get; set; }
    }
}
