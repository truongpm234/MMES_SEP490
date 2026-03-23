using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace AMMS.Shared.DTOs.Estimates
{
    public class UploadEstimateContractsBatchRequest
    {
        public int request_id { get; set; }

        public List<IFormFile> files { get; set; } = new();
    }
}
