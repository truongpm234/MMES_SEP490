using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class TaskMaterialUsageLogItemDto
    {
        public int material_id { get; set; }
        public string material_code { get; set; } = "";
        public string material_name { get; set; } = "";
        public string unit { get; set; } = "";

        public decimal estimated_input_qty { get; set; }
        public decimal quantity_used { get; set; }
        public decimal quantity_left { get; set; }
        public decimal quantity_waste { get; set; }

        public bool is_stock { get; set; }
    }
}