using AMMS.Infrastructure.DBContext;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Application.Helpers
{
    public class WorkCalendar
    {
        private readonly AppDbContext _db;
        private WorkCalendarConfig? _config;

        public WorkCalendar(AppDbContext db)
        {
            _db = db;
        }

        public DateTime NormalizeStart(DateTime input)
        {
            var cfg = GetConfig();
            var current = DateTime.SpecifyKind(input, DateTimeKind.Unspecified);

            while (true)
            {
                current = NormalizeToWorkingTime(current, cfg);

                if (IsWorkingDay(current.Date))
                    return current;

                current = current.Date.AddDays(1).Add(cfg.WorkStart);
            }
        }

        public DateTime AddWorkingHours(DateTime start, double hours)
        {
            if (hours <= 0)
                return NormalizeStart(start);

            var cfg = GetConfig();
            var current = NormalizeStart(start);
            var remainingMinutes = (int)Math.Ceiling(hours * 60d);

            while (remainingMinutes > 0)
            {
                current = NormalizeStart(current);

                var segmentEnd = GetSegmentEnd(current, cfg);
                var availableMinutes = (int)(segmentEnd - current).TotalMinutes;

                if (availableMinutes <= 0)
                {
                    current = MoveToNextSegment(current, cfg);
                    continue;
                }

                if (remainingMinutes <= availableMinutes)
                    return current.AddMinutes(remainingMinutes);

                remainingMinutes -= availableMinutes;
                current = MoveToNextSegment(segmentEnd, cfg);
            }

            return current;
        }

        private WorkCalendarConfig GetConfig()
        {
            if (_config != null)
                return _config;

            var rows = _db.estimate_config
                .AsNoTracking()
                .Where(x => x.config_group == "planning")
                .OrderByDescending(x => x.updated_at)
                .ToList();

            var cfg = new WorkCalendarConfig
            {
                WorkStart = ParseTime(
                    rows.FirstOrDefault(x => x.config_key == "work_start_time")?.value_text,
                    new TimeSpan(8, 0, 0)),

                BreakStart = ParseTime(
                    rows.FirstOrDefault(x => x.config_key == "break_start_time")?.value_text,
                    new TimeSpan(12, 0, 0)),

                BreakEnd = ParseTime(
                    rows.FirstOrDefault(x => x.config_key == "break_end_time")?.value_text,
                    new TimeSpan(13, 0, 0)),

                WorkEnd = ParseTime(
                    rows.FirstOrDefault(x => x.config_key == "work_end_time")?.value_text,
                    new TimeSpan(17, 0, 0))
            };

            // Tự sửa config lỗi để tránh vỡ scheduler
            if (cfg.WorkEnd <= cfg.WorkStart)
                cfg.WorkEnd = cfg.WorkStart.Add(TimeSpan.FromHours(8));

            if (cfg.BreakStart < cfg.WorkStart || cfg.BreakStart > cfg.WorkEnd)
                cfg.BreakStart = cfg.WorkEnd;

            if (cfg.BreakEnd < cfg.BreakStart || cfg.BreakEnd > cfg.WorkEnd)
                cfg.BreakEnd = cfg.BreakStart;

            _config = cfg;
            return _config;
        }

        private static DateTime NormalizeToWorkingTime(DateTime dt, WorkCalendarConfig cfg)
        {
            var t = dt.TimeOfDay;

            if (t < cfg.WorkStart)
                return dt.Date.Add(cfg.WorkStart);

            if (t >= cfg.WorkEnd)
                return dt.Date.AddDays(1).Add(cfg.WorkStart);

            var hasBreak = cfg.BreakEnd > cfg.BreakStart;
            if (hasBreak && t >= cfg.BreakStart && t < cfg.BreakEnd)
                return dt.Date.Add(cfg.BreakEnd);

            return dt;
        }

        private bool IsWorkingDay(DateTime date)
        {
            var d = date.Date;

            // Ưu tiên row override trong production_calendar
            var row = _db.production_calendars
                .AsNoTracking()
                .FirstOrDefault(x => x.calendar_date == d);

            if (row != null)
                return !row.is_non_working_day;

            // Rule mặc định
            return d.DayOfWeek != DayOfWeek.Sunday;
        }

        private static DateTime GetSegmentEnd(DateTime dt, WorkCalendarConfig cfg)
        {
            var t = dt.TimeOfDay;
            var hasBreak = cfg.BreakEnd > cfg.BreakStart;

            if (hasBreak && t < cfg.BreakStart)
                return dt.Date.Add(cfg.BreakStart);

            if ((!hasBreak || t >= cfg.BreakEnd) && t < cfg.WorkEnd)
                return dt.Date.Add(cfg.WorkEnd);

            return dt;
        }

        private static DateTime MoveToNextSegment(DateTime dt, WorkCalendarConfig cfg)
        {
            var t = dt.TimeOfDay;
            var hasBreak = cfg.BreakEnd > cfg.BreakStart;

            if (hasBreak && t < cfg.BreakStart)
                return dt.Date.Add(cfg.BreakEnd);

            return dt.Date.AddDays(1).Add(cfg.WorkStart);
        }

        private static TimeSpan ParseTime(string? raw, TimeSpan fallback)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return fallback;

            if (TimeSpan.TryParse(raw, out var ts))
                return ts;

            return fallback;
        }

        private sealed class WorkCalendarConfig
        {
            public TimeSpan WorkStart { get; set; }
            public TimeSpan BreakStart { get; set; }
            public TimeSpan BreakEnd { get; set; }
            public TimeSpan WorkEnd { get; set; }
        }
    }
}