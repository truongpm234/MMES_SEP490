using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.Helpers
{
    public static class ProductionFlowHelper
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
    }
}
