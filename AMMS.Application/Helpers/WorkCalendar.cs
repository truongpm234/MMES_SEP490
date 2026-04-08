using System;
using System.Collections.Generic;
using System.Linq;
using AMMS.Infrastructure.Configurations;
using Microsoft.Extensions.Options;

namespace AMMS.Application.Helpers
{
    public class WorkCalendar
    {
        private readonly SchedulingOptions _opt;
        private readonly HashSet<DateOnly> _holidaySet;
        private readonly int _shiftStartHour;
        private readonly int _shiftEndHour;
        private readonly bool _workOnSunday;

        public WorkCalendar(IOptions<SchedulingOptions> opt)
        {
            _opt = opt.Value ?? new SchedulingOptions();

            _shiftStartHour = _opt.shift_start_hour > 0
                ? _opt.shift_start_hour
                : 8;

            var fallbackEndHour = _shiftStartHour + (_opt.shift_hours_per_day > 0 ? _opt.shift_hours_per_day : 9);

            _shiftEndHour = _opt.shift_end_hour > _shiftStartHour
                ? _opt.shift_end_hour
                : fallbackEndHour;

            _workOnSunday = _opt.work_on_sunday;

            _holidaySet = (_opt.holidays ?? new List<string>())
                .Select(s => DateOnly.TryParse(s, out var d) ? d : default)
                .Where(d => d != default)
                .ToHashSet();
        }

        public bool IsHoliday(DateOnly d) => _holidaySet.Contains(d);

        public bool IsWorkingDay(DateOnly d)
        {
            if (!_workOnSunday && d.DayOfWeek == DayOfWeek.Sunday)
                return false;

            if (IsHoliday(d))
                return false;

            return true;
        }

        private DateTime GetShiftStart(DateOnly d)
            => new DateTime(d.Year, d.Month, d.Day, _shiftStartHour, 0, 0, DateTimeKind.Unspecified);

        private DateTime GetShiftEnd(DateOnly d)
            => new DateTime(d.Year, d.Month, d.Day, _shiftEndHour, 0, 0, DateTimeKind.Unspecified);

        private DateTime NextDayShiftStart(DateTime dt)
        {
            var nextDate = DateOnly.FromDateTime(dt.Date).AddDays(1);
            return GetShiftStart(nextDate);
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

                var shiftStart = GetShiftStart(d);
                var shiftEnd = GetShiftEnd(d);

                if (dt < shiftStart)
                    return shiftStart;

                if (dt >= shiftEnd)
                {
                    dt = NextDayShiftStart(dt);
                    continue;
                }

                return dt;
            }
        }

        public DateTime AddWorkingHours(DateTime start, double hours)
        {
            var cur = NormalizeStart(start);

            if (hours <= 0)
                return cur;

            var remaining = TimeSpan.FromHours(hours);

            while (remaining > TimeSpan.Zero)
            {
                var d = DateOnly.FromDateTime(cur);
                var shiftEnd = GetShiftEnd(d);

                var available = shiftEnd - cur;

                if (available <= TimeSpan.Zero)
                {
                    cur = NormalizeStart(NextDayShiftStart(cur));
                    continue;
                }

                if (remaining <= available)
                    return cur.Add(remaining);

                remaining -= available;

                cur = NormalizeStart(NextDayShiftStart(cur));
            }

            return cur;
        }
    }
}