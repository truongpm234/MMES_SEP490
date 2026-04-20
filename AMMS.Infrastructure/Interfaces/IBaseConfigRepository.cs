using AMMS.Shared.DTOs.Estimates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Interfaces
{
    public interface IBaseConfigRepository
    {
        Task<EstimateBaseConfigDto> GetAsync(CancellationToken ct);
        Task<PaymentTermsConfig> GetPaymentTermsAsync(CancellationToken ct);
        Task UpdateAsync(UpdateEstimateBaseConfigRequest dto, CancellationToken ct);
    }
}
