using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class TaskQrMaterialBundleDto
    {
        public List<TaskConsumableMaterialDto> consumable_materials { get; set; } = new();
        public List<TaskReferenceInputDto> reference_inputs { get; set; } = new();
    }
}