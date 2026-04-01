using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Machines
{
    public sealed class ReservationRow
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
    }

}
