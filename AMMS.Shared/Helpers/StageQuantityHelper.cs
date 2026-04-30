using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.Helpers
{
    public enum StageQtyMode
    {
        Plate,
        Sheet,
        Product
    }

    public sealed class StageQtyProfile
    {
        public StageQtyMode Mode { get; init; }
        public string QtyUnit { get; init; } = "sp";
        public int MinAllowed { get; init; } = 1;
        public int MaxAllowed { get; init; }
        public int SuggestedQty { get; init; }
    }

    public static class StageQuantityHelper
    {
        public static string Norm(string? code)
            => (code ?? "").Trim().ToUpperInvariant();

        public static bool IsRalo(string? code)
            => Norm(code) is "RALO" or "RA_LO";

        public static bool IsProductSplitStage(string? code)
    => Norm(code) is "BE" or "DUT" or "DAN";

        public static int FindProductSplitStageIndex(IReadOnlyList<string?> routeProcessCodes)
        {
            if (routeProcessCodes == null || routeProcessCodes.Count == 0)
                return -1;

            for (var i = 0; i < routeProcessCodes.Count; i++)
            {
                if (IsProductSplitStage(routeProcessCodes[i]))
                    return i;
            }

            return -1;
        }

        public static StageQtyMode ResolveStageQtyMode(
            string? currentCode,
            int currentStageIndex,
            IReadOnlyList<string?> routeProcessCodes)
        {
            if (IsRalo(currentCode))
                return StageQtyMode.Plate;

            var splitIndex = FindProductSplitStageIndex(routeProcessCodes);

            if (splitIndex >= 0 && currentStageIndex >= splitIndex)
                return StageQtyMode.Product;

            return StageQtyMode.Sheet;
        }

        public static int GetPlateCap(int numberOfPlates)
            => Math.Max(1, numberOfPlates);

        public static int GetSheetCap(int sheetsTotal)
            => Math.Max(1, sheetsTotal);

        public static int GetProductCap(int sheetsTotal, int nUp)
        {
            var safeSheets = Math.Max(1, sheetsTotal);
            var safeNUp = Math.Max(1, nUp);

            try
            {
                return checked(safeSheets * safeNUp);
            }
            catch
            {
                return int.MaxValue;
            }
        }

        public static StageQtyProfile BuildPolicy(
            string? currentCode,
            int currentStageIndex,
            IReadOnlyList<string?> routeProcessCodes,
            int sheetsTotal,
            int nUp,
            int numberOfPlates,
            int tokenQtyMax = int.MaxValue)
        {
            var mode = ResolveStageQtyMode(currentCode, currentStageIndex, routeProcessCodes);

            if (mode == StageQtyMode.Plate)
            {
                var plateQty = Math.Min(GetPlateCap(numberOfPlates), tokenQtyMax);

                return new StageQtyProfile
                {
                    Mode = mode,
                    QtyUnit = "bản",
                    MinAllowed = plateQty,
                    MaxAllowed = plateQty,
                    SuggestedQty = plateQty
                };
            }

            var cap = mode == StageQtyMode.Product
                ? GetProductCap(sheetsTotal, nUp)
                : GetSheetCap(sheetsTotal);

            cap = Math.Min(cap, tokenQtyMax);

            return new StageQtyProfile
            {
                Mode = mode,
                QtyUnit = mode == StageQtyMode.Product ? "sp" : "tờ",
                MinAllowed = 1,
                MaxAllowed = cap,
                SuggestedQty = cap
            };
        }
    }
}
