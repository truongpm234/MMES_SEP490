using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public sealed class StageOutputRef
    {
        public string Name { get; set; } = "";
        public string? Code { get; set; }
        public string Unit { get; set; } = "tờ";
        public decimal Quantity { get; set; }
    }

}
