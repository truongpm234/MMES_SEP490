using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates;

public class UploadCustomerSignedContractResponse
{
    public int request_id { get; set; }
    public int estimate_id { get; set; }
    public string? customer_signed_contract_path { get; set; }
    public CompareContractResponse? compare_result { get; set; }
    public string? compare_warning { get; set; }
}
