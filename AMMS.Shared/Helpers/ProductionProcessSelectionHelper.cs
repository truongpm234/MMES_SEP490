using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.Helpers
{
    public static class ProductionProcessSelectionHelper
    {
        public static string Norm(string? value)
            => (value ?? "").Trim().ToUpperInvariant();

        public static HashSet<string> ParseCsv(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(Norm)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public static List<T> ResolveFixedRoute<T>(
    IEnumerable<T> orderedSteps,
    Func<T, string?> codeSelector,
    string? rawCsv)
        {
            var steps = orderedSteps.ToList();
            if (steps.Count == 0)
                return steps;

            var selected = ParseCsv(rawCsv);
            if (selected.Count == 0)
                return steps;

            var filtered = steps
                .Where(s =>
                {
                    var code = Norm(codeSelector(s));
                    return !string.IsNullOrWhiteSpace(code) && selected.Contains(code);
                })
                .ToList();

            return filtered.Count > 0 ? filtered : steps;
        }

        public static string BuildCsv<T>(IEnumerable<T> orderedSteps, Func<T, string?> codeSelector)
        {
            return string.Join(",",
                orderedSteps
                    .Select(codeSelector)
                    .Select(Norm)
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }
}
