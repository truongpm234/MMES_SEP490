using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.Helpers
{
    public static class EstimateMaterialAlternativeHelper
    {
        public static string? ResolvePaperCode(string? paperAlternative, string? contractPaperCode)
            => !string.IsNullOrWhiteSpace(paperAlternative)
                ? paperAlternative.Trim()
                : contractPaperCode?.Trim();

        public static string? ResolveWaveType(string? waveAlternative, string? contractWaveType)
            => !string.IsNullOrWhiteSpace(waveAlternative)
                ? waveAlternative.Trim()
                : contractWaveType?.Trim();

        public static string ResolvePaperName(
            string? resolvedPaperCode,
            string? contractPaperName,
            IReadOnlyDictionary<string, string> materialNameByCode)
        {
            if (!string.IsNullOrWhiteSpace(resolvedPaperCode) &&
                materialNameByCode.TryGetValue(resolvedPaperCode.Trim(), out var materialName) &&
                !string.IsNullOrWhiteSpace(materialName))
            {
                return materialName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(contractPaperName))
                return contractPaperName.Trim();

            return resolvedPaperCode?.Trim() ?? "";
        }

        public static string? NormalizeNullable(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
