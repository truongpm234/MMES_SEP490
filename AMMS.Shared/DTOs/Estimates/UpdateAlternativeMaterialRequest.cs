using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates
{
    public class UpdateAlternativeMaterialRequest
    {
        public int request_id { get; set; }
        public int? estimate_id { get; set; }
        public string? paper_alternative { get; set; }
        public string? wave_alternative { get; set; }
        public string? alternative_material_reason { get; set; }
    }
}
