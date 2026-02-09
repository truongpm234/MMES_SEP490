using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace AMMS.Application.Helpers
{
    public class WorkCalendar
    {
        private readonly SchedulingOptions _opt;
        private readonly HashSet<DateOnly> _holidaySet;

        public WorkCalendar(IOptions<SchedulingOptions> opt)
        {
            _opt = opt.Value;
            _holidaySet = _opt.holidays
                .Select(s => DateOnly.TryParse(s, out var d) ? d : default)
                .Where(d => d != default)
                .ToHashSet();
        }

        public bool IsHoliday(DateOnly d) => _holidaySet.Contains(d);

        public bool IsWorkingDay(DateOnly d)
        {
            if (d.DayOfWeek == DayOfWeek.Sunday) return false;
            if (IsHoliday(d)) return false;
            return true;
        }

        public DateTime NormalizeStart(DateTime dt)
        {
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

            while (true)
            {
                var d = DateOnly.FromDateTime(dt);
                if (!IsWorkingDay(d))
                {
                    dt = NextDayShiftStart(dt);
                    continue;
                }

                var shiftStart = new DateTime(dt.Year, dt.Month, dt.Day, _opt.shift_start_hour, 0, 0, DateTimeKind.Unspecified);
                var shiftEnd = shiftStart.AddHours(_opt.shift_hours_per_day);

                if (dt < shiftStart) return shiftStart;
                if (dt >= shiftEnd) { dt = NextDayShiftStart(dt); continue; }

                return dt;
            }
        }

        public DateTime AddWorkingHours(DateTime start, double hours)
        {
            if (hours <= 0) return start;

            var cur = NormalizeStart(start);
            var remaining = TimeSpan.FromHours(hours);

            while (remaining > TimeSpan.Zero)
            {
                var shiftStart = new DateTime(cur.Year, cur.Month, cur.Day, _opt.shift_start_hour, 0, 0, DateTimeKind.Unspecified);
                var shiftEnd = shiftStart.AddHours(_opt.shift_hours_per_day);

                // cur đã nằm trong ca
                var available = shiftEnd - cur;
                if (available <= TimeSpan.Zero)
                {
                    cur = NextDayShiftStart(cur);
                    cur = NormalizeStart(cur);
                    continue;
                }

                var take = remaining <= available ? remaining : available;
                cur = cur.Add(take);
                remaining -= take;

                if (remaining > TimeSpan.Zero)
                {
                    cur = NextDayShiftStart(cur);
                    cur = NormalizeStart(cur);
                }
            }

            return cur;
        }

        private DateTime NextDayShiftStart(DateTime dt)
        {
            var next = dt.Date.AddDays(1);
            return new DateTime(next.Year, next.Month, next.Day, _opt.shift_start_hour, 0, 0, DateTimeKind.Unspecified);
        }
    }
}

