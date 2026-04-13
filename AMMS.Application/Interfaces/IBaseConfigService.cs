using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AMMS.Shared.DTOs.Estimates;

namespace AMMS.Application.Interfaces
{
    public interface IBaseConfigService
    {
        Task<EstimateBaseConfigDto> GetAsync(CancellationToken ct = default);
        Task<PaymentTermsConfig> GetPaymentTermsAsync(CancellationToken ct);
    }
}
