using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class GenerateImportReceiveResponse
    {
        public bool success { get; set; }
        public int prod_id { get; set; }
        public int order_id { get; set; }
        public string order_code { get; set; } = string.Empty;
        public string import_recieve_path { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }
}
