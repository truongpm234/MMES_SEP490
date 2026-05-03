using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class ImportReceiveSourceDto
    {
        public int prod_id { get; set; }
        public int order_id { get; set; }
        public string order_code { get; set; } = string.Empty;
        public List<ImportReceiveItemDto> items { get; set; } = new();
    }
}
