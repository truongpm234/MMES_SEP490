using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Materials
{
    public class GlueTypeDto
    {
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public decimal? StockQty { get; set; }
        public decimal? Price { get; set; } = null!;
    }
}
