using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Application.Services
{
    public class BaseConfigService : IBaseConfigService
    {
        private readonly IBaseConfigRepository _repo;

        public BaseConfigService(IBaseConfigRepository repo)
        {
            _repo = repo;
        }

        public async Task<EstimateBaseConfigDto> GetAsync(CancellationToken ct)
        {
            return await _repo.GetAsync(ct);
        }

        public async Task<PaymentTermsConfig> GetPaymentTermsAsync(CancellationToken ct)
        {
            return await _repo.GetPaymentTermsAsync(ct);
        }

        public async Task UpdateAsync(UpdateEstimateBaseConfigRequest dto, CancellationToken ct)
        {
            await _repo.UpdateAsync(dto, ct);
        }
    }
}