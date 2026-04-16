using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class TaskQrResponse
    {
        public int task_id { get; set; }
        public string token { get; set; } = "";
        public long expires_at_unix { get; set; }

        public int qty_good_used { get; set; }
        public bool is_auto_filled { get; set; }

        public int min_allowed { get; set; }
        public int max_allowed { get; set; }
        public int suggested_qty { get; set; }

        public string qty_unit { get; set; } = "sp";
        public string? process_code { get; set; }
        public string? process_name { get; set; }
        public int embedded_material_count { get; set; }
        public List<TaskConsumableMaterialDto> consumable_materials { get; set; } = new();
        public List<TaskReferenceInputDto> reference_inputs { get; set; } = new();
    }
}
