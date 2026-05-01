using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Products
{
    public class CreateProductReceiptDto
    {
        public string? note { get; set; }
        public List<CreateProductReceiptItemDto> items { get; set; } = new();
    }
}
