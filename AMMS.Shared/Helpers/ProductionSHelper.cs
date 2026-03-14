using AMMS.Shared.DTOs.Productions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.Helpers
{
    public class ProductionSHelper
    {
        private static readonly HashSet<int> FullAccessRoleIds = new()
{
    4,6 // production_manager and warehouse_manager
};

        public static readonly Dictionary<int, string> RoleIdToProcess = new()
        {
            [7] = "RALO",
            [8] = "CAT",
            [9] = "IN",
            [10] = "PHU",
            [11] = "CAN",
            [12] = "BOI",
            [13] = "BE",
            [14] = "DUT",
            [15] = "DAN"
        };

        public static bool HasFullAccess(int? roleId)
            => roleId.HasValue && FullAccessRoleIds.Contains(roleId.Value);

        public static bool CanAccessProcess(int? roleId, string? processCode)
        {
            if (!roleId.HasValue || string.IsNullOrWhiteSpace(processCode))
                return false;

            if (HasFullAccess(roleId))
                return true;

            return RoleIdToProcess.TryGetValue(roleId.Value, out var allowedCode)
                   && string.Equals(
                       allowedCode,
                       processCode.Trim(),
                       StringComparison.OrdinalIgnoreCase
                   );
        }

        public static List<StepRow> FilterStepsByRole(List<StepRow> steps, int? roleId)
        {
            if (HasFullAccess(roleId))
                return steps;

            return steps
                .Where(x => CanAccessProcess(roleId, x.ProcessCode))
                .ToList();
        }
    }
}
