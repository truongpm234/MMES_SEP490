using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace AMMS.Shared.Helpers
{
    public class EstimateHelper
    {
        public static string Trunc20(string? s)
        {
            s = (s ?? "").Trim();
            if (s.Length <= 20) return s;
            return s.Substring(0, 20);
        }
    }
}