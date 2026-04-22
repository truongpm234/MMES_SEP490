using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates
{
    public class ContractTextDiffItemDto
    {
        public string type { get; set; } = "";
        public int expected_line { get; set; }
        public int actual_line { get; set; }
        public string expected_text { get; set; } = "";
        public string actual_text { get; set; } = "";
    }
}