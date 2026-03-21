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
    }
}