using System;
using System.Collections.Generic;
using System.Linq;

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

        public static bool IsCutStage(string? code)
            => Norm(code) is "CAT" or "CUT";

        public static bool IsProductSplitStage(string? code)
            => Norm(code) is "BE" or "DUT" or "DAN";

        public static int FindCutStageIndex(IReadOnlyList<string?> routeProcessCodes)
        {
            if (routeProcessCodes == null || routeProcessCodes.Count == 0)
                return -1;

            for (var i = 0; i < routeProcessCodes.Count; i++)
            {
                if (IsCutStage(routeProcessCodes[i]))
                    return i;
            }

            return -1;
        }

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

        public static bool IsAtOrAfterCutStage(
            string? currentCode,
            int currentStageIndex,
            IReadOnlyList<string?> routeProcessCodes)
        {
            if (IsCutStage(currentCode))
                return true;

            var cutIndex = FindCutStageIndex(routeProcessCodes);

            return cutIndex >= 0 && currentStageIndex >= cutIndex;
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

        private static int CapByTokenMax(int value, int tokenQtyMax)
        {
            var safeTokenMax = tokenQtyMax <= 0 ? int.MaxValue : tokenQtyMax;
            return Math.Max(1, Math.Min(value, safeTokenMax));
        }

        public static int GetProductionOutputCap(
    string? currentCode,
    int currentStageIndex,
    IReadOnlyList<string?> routeProcessCodes,
    int sheetsTotal,
    int nUp,
    int numberOfPlates,
    int tokenQtyMax = int.MaxValue)
        {
            var mode = ResolveStageQtyMode(
                currentCode,
                currentStageIndex,
                routeProcessCodes);

            if (mode == StageQtyMode.Plate)
            {
                return CapByTokenMax(GetPlateCap(numberOfPlates), tokenQtyMax);
            }

            var isAtOrAfterCut = IsAtOrAfterCutStage(
                currentCode,
                currentStageIndex,
                routeProcessCodes);

            var cap = isAtOrAfterCut || mode == StageQtyMode.Product
                ? GetProductCap(sheetsTotal, nUp)
                : GetSheetCap(sheetsTotal);

            return CapByTokenMax(cap, tokenQtyMax);
        }

        public static string ResolveQtyUnitLikeProduction(
    string? currentCode,
    int currentStageIndex,
    IReadOnlyList<string?> routeProcessCodes)
        {
            var mode = ResolveStageQtyMode(
                currentCode,
                currentStageIndex,
                routeProcessCodes);

            return mode switch
            {
                StageQtyMode.Plate => "bản",
                StageQtyMode.Product => "sp",
                _ => "tờ"
            };
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
            var mode = ResolveStageQtyMode(
                currentCode,
                currentStageIndex,
                routeProcessCodes);

            var maxQty = GetProductionOutputCap(
                currentCode,
                currentStageIndex,
                routeProcessCodes,
                sheetsTotal,
                nUp,
                numberOfPlates,
                tokenQtyMax);

            var unit = ResolveQtyUnitLikeProduction(
                currentCode,
                currentStageIndex,
                routeProcessCodes);

            if (mode == StageQtyMode.Plate)
            {
                return new StageQtyProfile
                {
                    Mode = mode,
                    QtyUnit = unit,
                    MinAllowed = maxQty,
                    MaxAllowed = maxQty,
                    SuggestedQty = maxQty
                };
            }

            return new StageQtyProfile
            {
                Mode = mode,
                QtyUnit = unit,
                MinAllowed = 1,
                MaxAllowed = maxQty,
                SuggestedQty = maxQty
            };
        }
    }
}