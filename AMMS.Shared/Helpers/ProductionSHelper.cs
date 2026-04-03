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
        public static readonly HashSet<int> FullAccessRoleIds = new()
        {
            4,6, 3
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

        public static StageMaterialDto BuildStageMaterial(
    string name,
    string? code,
    decimal estimatedQty,
    decimal? actualQty,
    string unit)
        {
            estimatedQty = Math.Max(0m, estimatedQty);
            if (actualQty.HasValue)
                actualQty = Math.Max(0m, actualQty.Value);

            return new StageMaterialDto
            {
                name = name,
                code = code,
                unit = unit,

                estimated_quantity = estimatedQty,
                actual_quantity = actualQty,

                quantity = actualQty ?? estimatedQty,
                quantity_source = actualQty.HasValue ? "Actual" : "Estimated"
            };
        }

        public static decimal? ToActualQty(int qtyGood)
            => qtyGood > 0 ? qtyGood : null;

        public static decimal CapEstimatedByPreviousOutput(StageOutputRef? prevOutput, decimal proposed)
        {
            proposed = Math.Max(0m, proposed);

            if (prevOutput?.ActualQuantity is decimal prevActual && prevActual > 0m)
                return Math.Min(proposed, prevActual);

            if (prevOutput != null && prevOutput.EstimatedQuantity > 0m)
                return Math.Min(proposed, prevOutput.EstimatedQuantity);

            return proposed;
        }

        public static decimal? CapActualByPreviousOutput(StageOutputRef? prevOutput, decimal? proposed)
        {
            if (!proposed.HasValue)
                return null;

            if (prevOutput?.ActualQuantity is decimal prevActual && prevActual > 0m)
                return Math.Min(proposed.Value, prevActual);

            return proposed.Value;
        }

        public static decimal ResolveEstimatedOutputQty(
            string processCode,
            ProductionDetailDto detail,
            decimal defaultEstimatedQty)
        {
            var code = (processCode ?? "").Trim().ToUpperInvariant();

            if (code == "DAN" && detail.quantity > 0)
                return detail.quantity;

            return defaultEstimatedQty;
        }


    }
}
