using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class ImportReceiveItemDto
    {
        public int item_id { get; set; }
        public string product_name { get; set; } = string.Empty;
        public int quantity { get; set; }
        public string? packaging_standard { get; set; }
    }
}
