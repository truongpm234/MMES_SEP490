using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Machines
{
    public class MachineAvailabilitySnapshotDto
    {
        public DateTime generated_at { get; set; }
        public DateTime workshop_all_free_at { get; set; }
        public DateTime? ralo_cat_both_free_at { get; set; }
        public List<MachineAvailabilityLineDto> machines { get; set; } = new();
    }

}
