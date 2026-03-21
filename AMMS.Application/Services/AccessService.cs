using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Services
{
    public class AccessService : IAccessService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IRequestRepository _requestRepository;
        public AccessService(IHttpContextAccessor httpContextAccessor, IRequestRepository requestRepository)
        {
            _httpContextAccessor = httpContextAccessor;
            _requestRepository = requestRepository;
        }

        public int? UserId
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                if (user == null) return null;

                var raw =
                    user.FindFirstValue("user_id") ??
                    user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                    user.FindFirstValue("sub");

                return int.TryParse(raw, out var id) ? id : null;
            }
        }

        public int? RoleId
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                if (user == null) return null;

                var raw = user.FindFirstValue("roleid");
                return int.TryParse(raw, out var roleId) ? roleId : null;
            }
        }

        public bool IsAuthenticated =>
            _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

        public bool IsConsultant => RoleId == 2;

        public Task<int?> GetConsultantScopeUserIdAsync(CancellationToken ct = default)
        {
            if (!IsAuthenticated || !IsConsultant || !UserId.HasValue)
                return Task.FromResult<int?>(null);

            return Task.FromResult<int?>(UserId.Value);
        }

        public async Task EnsureCanAccessAssignedRequestAsync(int requestId, CancellationToken ct = default)
        {
            if (!IsAuthenticated || !IsConsultant || !UserId.HasValue)
                return;

            var allowed = await _requestRepository.CanConsultantAccessRequestAsync(
                requestId, UserId.Value, ct);

            if (!allowed)
                throw new UnauthorizedAccessException("Bạn không được phân công xử lý yêu cầu này.");
        }
    }
}
