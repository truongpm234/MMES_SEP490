using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Planning
{
    public sealed class ExistingReservation
    {
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
    }
}
