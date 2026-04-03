using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public sealed class StageOutputRef
    {
        public string Name { get; init; } = "";
        public string? Code { get; init; }
        public string Unit { get; init; } = "sp";

        public decimal EstimatedQuantity { get; init; }
        public decimal? ActualQuantity { get; init; }

        public decimal EffectiveQuantity => ActualQuantity ?? EstimatedQuantity;
    }

}
