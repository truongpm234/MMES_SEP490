using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates;

public class CompareContractResponse
{
    public int request_id { get; set; }
    public int estimate_id { get; set; }
    public decimal similarity_percent { get; set; }
    public bool is_match_90 { get; set; }
    public int consultant_text_length { get; set; }
    public int customer_text_length { get; set; }
}
