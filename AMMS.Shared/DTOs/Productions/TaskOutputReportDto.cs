using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class TaskOutputReportDto
    {
        public string output_code { get; set; } = "";

        public string? output_name { get; set; }

        public string? unit { get; set; }

        public decimal quantity_good { get; set; }

        public decimal quantity_bad { get; set; }
    }
}
