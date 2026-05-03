using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public sealed class PreviousProcessContext
    {
        public string? previous_process_code { get; init; }
        public int previous_stage_index { get; init; }
        public List<string?> route_process_codes { get; init; } = new();
    }
}
