using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.SubProduct
{
    public class UpdateSubProductResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public int id { get; set; }
        public DateTime? updated_at { get; set; }
    }

}
