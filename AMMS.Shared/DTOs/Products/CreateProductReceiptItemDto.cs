using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Products
{
    public class CreateProductReceiptItemDto
    {
        public int product_id { get; set; }
        public int qty_received { get; set; }
        public string? note { get; set; }
    }
}
