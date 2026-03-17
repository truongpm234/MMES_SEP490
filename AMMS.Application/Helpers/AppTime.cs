using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Helpers
{
    public static class AppTime
    {
        private static readonly TimeZoneInfo VnTz =
            TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

        public static DateTime NowVn()
            => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTz);

        public static DateTime NowVnUnspecified()
            => DateTime.SpecifyKind(NowVn(), DateTimeKind.Unspecified);

        public static DateTime? NormalizeToVnDateOnlyUnspecified(DateTime? input)
        {
            if (!input.HasValue) return null;

            var dt = input.Value;
            DateTime vnTime;

            if (dt.Kind == DateTimeKind.Utc)
            {
                vnTime = TimeZoneInfo.ConvertTimeFromUtc(dt, VnTz);
            }
            else if (dt.Kind == DateTimeKind.Local)
            {
                vnTime = TimeZoneInfo.ConvertTime(dt, VnTz);
            }
            else
            {
                vnTime = dt;
            }

            return new DateTime(vnTime.Year, vnTime.Month, vnTime.Day, 0, 0, 0, DateTimeKind.Unspecified);
        }
    }
}
