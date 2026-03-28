using System;
using System.Collections.Generic;
using System.Linq;

namespace AMMS.Application.Helpers
{
    public static class ProductionRoleMap
    {
        private static readonly HashSet<int> FullAccessRoleIds = new()
        {
            6, 3, 4
        };

        // map role_id -> process_code
        private static readonly Dictionary<int, string> RoleIdToProcess = new()
        {
            [7] = "RALO", // staff-ralo
            [8] = "CAT",  // staff-cat
            [9] = "IN",   // staff-in
            [10] = "PHU",  // staff-phu
            [11] = "CAN",  // staff-can
            [12] = "BOI",  // staff-boi
            [13] = "BE",   // staff-be
            [14] = "DUT",  // staff-dut
            [15] = "DAN"   // staff-dan
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

        public static List<T> FilterStages<T>(
            IEnumerable<T> source,
            int? roleId,
            Func<T, string?> processCodeSelector)
        {
            if (HasFullAccess(roleId))
                return source.ToList();

            return source
                .Where(x => CanAccessProcess(roleId, processCodeSelector(x)))
                .ToList();
        }
    }
}