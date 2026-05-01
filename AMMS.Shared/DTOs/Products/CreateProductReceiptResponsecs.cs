using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Products
{
    public class CreateProductReceiptResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public int receipt_id { get; set; }
        public string code { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
    }
}
