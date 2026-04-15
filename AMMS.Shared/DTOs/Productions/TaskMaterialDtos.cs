using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class TaskConsumableMaterialDto
    {
        public int? material_id { get; set; }
        public string material_code { get; set; } = "";
        public string material_name { get; set; } = "";
        public string unit { get; set; } = "";
        public decimal estimated_input_qty { get; set; }
        public bool is_mapped { get; set; }
    }
}

