using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Interfaces
{
    public interface IAccessService
    {
        int? UserId { get; }
        int? RoleId { get; }
        bool IsAuthenticated { get; }
        bool IsConsultant { get; }
        Task<int?> GetConsultantScopeUserIdAsync(CancellationToken ct = default);
        Task EnsureCanAccessAssignedRequestAsync(int requestId, CancellationToken ct = default);
    }
}
