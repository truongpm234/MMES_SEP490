using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Requests
{
    public class UploadPrintReadyFileRequest
    {
        public int? estimate_id { get; set; }
        public IFormFile File { get; set; } = default!;
    }
}
