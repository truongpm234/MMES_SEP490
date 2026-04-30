using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.Helpers
{
    public class ProductionFlowHelper
    {
        public const string Ralo = "RALO";
        public const string Cat = "CAT";

        public static string Norm(string? code)
            => (code ?? "").Trim().ToUpperInvariant();

        public static bool IsRalo(string? code)
            => Norm(code) == Ralo;

        public static bool IsCat(string? code)
            => Norm(code) == Cat;

        public static bool IsInitialParallel(string? code)
        {
            var c = Norm(code);
            return c == Ralo || c == Cat;
        }

        public static bool NeedsRaloGate(string? code)
            => !IsInitialParallel(code);

        public static string ResolveCoatingDisplayName(string? coatingType)
        {
            var code = (coatingType ?? "").Trim().ToUpperInvariant();

            return code switch
            {
                "KEO_NUOC" => "Keo nước",
                "KEO_DAU" => "Keo dầu",
                _ => string.IsNullOrWhiteSpace(coatingType) ? "Keo phủ" : coatingType!.Trim()
            };
        }

        public static (int requiredPlates, int wastePlates, int totalPlates) ResolveRaloPlateBreakdown(int? storedTotalPlates)
        {
            var total = Math.Max(1, storedTotalPlates ?? 1);

            /*
                Quy ước:
                - Nếu số bản cần <= 5: hao phí +1
                - Nếu số bản cần > 5: hao phí +2
            */

            int waste;
            int required;

            if (total <= 6)
            {
                waste = 1;
                required = total - waste;
            }
            else
            {
                waste = 2;
                required = total - waste;
            }

            if (required < 1)
            {
                required = total;
                waste = 0;
            }

            return (required, waste, total);
        }

        public static decimal MultiplyByNUp(decimal qty, int nUp)
        {
            return qty * Math.Max(1, nUp);
        }

        public static decimal? MultiplyByNUp(decimal? qty, int nUp)
        {
            if (!qty.HasValue)
                return null;

            return qty.Value * Math.Max(1, nUp);
        }
    }
}
