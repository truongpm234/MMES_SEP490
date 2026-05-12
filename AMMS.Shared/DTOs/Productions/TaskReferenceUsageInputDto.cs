using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class TaskReferenceUsageInputDto
    {
        public string input_code { get; set; } = "";

        public string? input_name { get; set; }

        public string? unit { get; set; }

        public decimal quantity_used { get; set; }

        public decimal quantity_left { get; set; }
    }
}

