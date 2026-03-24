using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace AMMS.Shared.DTOs.Estimates;

public class UploadConsultantContractRequest
{
    public int request_id { get; set; }
    public int estimate_id { get; set; }
    public IFormFile file { get; set; } = null!;
}
