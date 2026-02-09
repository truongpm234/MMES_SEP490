using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Materials
{
    public class MaterialTypeGlueDto
    {
        public List<GlueTypeDto> GlueTypes { get; set; } = new();

        public string MostStockGlueNames { get; set; } = null!;
    }
}
