using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Planning;
using AMMS.Shared.DTOs.ProductionCalendars;
using AMMS.Shared.Helpers;

namespace AMMS.Application.Services
{
    public class ProductionCalendarService : IProductionCalendarService
    {
        private readonly IProductionCalendarRepository _repo;

        public ProductionCalendarService(IProductionCalendarRepository repo)
        {
            _repo = repo;
        }

        public async Task<bool> IsWorkingDayAsync(DateTime date, CancellationToken ct = default)
        {
            var d = ToVnDate(date);

            var row = await _repo.GetByDateAsync(d, ct);
            if (row != null)
                return !row.is_non_working_day;

            return d.DayOfWeek != DayOfWeek.Sunday;
        }

        public async Task<ProductionCalendarDto?> GetByDateAsync(DateTime date, CancellationToken ct = default)
        {
            var row = await _repo.GetByDateAsync(ToVnDate(date), ct);
            if (row == null) return null;

            return new ProductionCalendarDto
            {
                calendar_date = row.calendar_date,
                holiday_name = row.holiday_name,
                holiday_type = row.holiday_type,
                is_non_working_day = row.is_non_working_day,
                is_manual_override = row.is_manual_override,
                note = row.note,
                created_at = row.created_at,
                updated_at = row.updated_at
            };
        }

        public async Task<List<ProductionCalendarDto>> GetRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            var rows = await _repo.GetRangeAsync(ToVnDate(from), ToVnDate(to), ct);

            return rows.Select(row => new ProductionCalendarDto
            {
                calendar_date = row.calendar_date,
                holiday_name = row.holiday_name,
                holiday_type = row.holiday_type,
                is_non_working_day = row.is_non_working_day,
                is_manual_override = row.is_manual_override,
                note = row.note,
                created_at = row.created_at,
                updated_at = row.updated_at
            }).ToList();
        }

        public async Task CreateAsync(CreateProductionCalendarRequest dto, CancellationToken ct = default)
        {
            if (dto == null)
                throw new ArgumentException("Payload is required");

            if (dto.calendar_date == default)
                throw new ArgumentException("calendar_date is required");

            var calendarDate = ToVnDate(dto.calendar_date);

            var existing = await _repo.GetByDateAsync(calendarDate, ct);
            if (existing != null)
                throw new ArgumentException($"calendar_date {calendarDate:yyyy-MM-dd} already exists");

            var now = AppTime.NowVnUnspecified();

            var entity = new production_calendar
            {
                calendar_date = calendarDate,
                holiday_name = NormalizeNullable(dto.holiday_name, 255, "holiday_name"),
                holiday_type = NormalizeRequiredOrDefault(dto.holiday_type, 50, "MANUAL"),
                is_non_working_day = dto.is_non_working_day,
                is_manual_override = dto.is_manual_override ?? true,
                note = NormalizeNullable(dto.note, null, "note"),
                created_at = now,
                updated_at = now
            };

            await _repo.AddAsync(entity, ct);
            await _repo.SaveChangesAsync(ct);
        }

        public async Task<bool> UpdateAsync(DateTime date, UpdateProductionCalendarRequest dto, CancellationToken ct = default)
        {
            if (dto == null)
                throw new ArgumentException("Payload is required");

            var row = await _repo.GetByDateTrackingAsync(ToVnDate(date), ct);
            if (row == null) return false;

            if (dto.holiday_name != null)
                row.holiday_name = NormalizeNullable(dto.holiday_name, 255, "holiday_name");

            if (dto.holiday_type != null)
                row.holiday_type = NormalizeRequired(dto.holiday_type, 50, "holiday_type");

            if (dto.note != null)
                row.note = NormalizeNullable(dto.note, null, "note");

            row.updated_at = AppTime.NowVnUnspecified();

            await _repo.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> UpdateNonWorkingDayAsync(DateTime date, bool isNonWorkingDay, CancellationToken ct = default)
        {
            var row = await _repo.GetByDateTrackingAsync(ToVnDate(date), ct);
            if (row == null) return false;

            row.is_non_working_day = isNonWorkingDay;
            row.updated_at = AppTime.NowVnUnspecified();

            await _repo.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> UpdateManualOverrideAsync(DateTime date, bool isManualOverride, CancellationToken ct = default)
        {
            var row = await _repo.GetByDateTrackingAsync(ToVnDate(date), ct);
            if (row == null) return false;

            row.is_manual_override = isManualOverride;
            row.updated_at = AppTime.NowVnUnspecified();

            await _repo.SaveChangesAsync(ct);
            return true;
        }

        public async Task UpsertAsync(ProductionCalendarDto dto, CancellationToken ct = default)
        {
            if (dto.calendar_date == default)
                throw new ArgumentException("calendar_date is required");

            var now = AppTime.NowVnUnspecified();

            var entity = new production_calendar
            {
                calendar_date = ToVnDate(dto.calendar_date),
                holiday_name = string.IsNullOrWhiteSpace(dto.holiday_name) ? null : dto.holiday_name.Trim(),
                holiday_type = string.IsNullOrWhiteSpace(dto.holiday_type) ? "MANUAL" : NormalizeRequired(dto.holiday_type, 50, "holiday_type"),
                is_non_working_day = dto.is_non_working_day,
                is_manual_override = true,
                note = string.IsNullOrWhiteSpace(dto.note) ? null : dto.note.Trim(),
                created_at = now,
                updated_at = now
            };

            await _repo.UpsertAsync(entity, ct);
            await _repo.SaveChangesAsync(ct);
        }

        public async Task<bool> DeleteAsync(DateTime date, CancellationToken ct = default)
        {
            var ok = await _repo.DeleteByDateAsync(ToVnDate(date), ct);
            if (!ok) return false;

            await _repo.SaveChangesAsync(ct);
            return true;
        }

        private static DateTime ToVnDate(DateTime value)
            => DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

        private static string? NormalizeNullable(string? value, int? maxLength, string fieldName)
        {
            if (value == null) return null;

            var trimmed = value.Trim();
            if (trimmed.Length == 0) return null;

            if (maxLength.HasValue && trimmed.Length > maxLength.Value)
                throw new ArgumentException($"{fieldName} length must be <= {maxLength.Value}");

            return trimmed;
        }

        private static string NormalizeRequired(string value, int maxLength, string fieldName)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                throw new ArgumentException($"{fieldName} is required");

            if (trimmed.Length > maxLength)
                throw new ArgumentException($"{fieldName} length must be <= {maxLength}");

            return trimmed.ToUpperInvariant();
        }

        private static string NormalizeRequiredOrDefault(string? value, int maxLength, string defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            var trimmed = value.Trim();
            if (trimmed.Length > maxLength)
                throw new ArgumentException($"holiday_type length must be <= {maxLength}");

            return trimmed.ToUpperInvariant();
        }

        public Task<List<production_calendar>> GetAllDate()
        {
            return _repo.GetAllDate();
        }

        public async Task<int> CreateRangeAsync(CreateProductionCalendarRangeRequest dto, CancellationToken ct = default)
        {
            if (dto == null)
                throw new ArgumentException("Payload is required");

            if (dto.from_date == default)
                throw new ArgumentException("from_date is required");

            if (dto.to_date == default)
                throw new ArgumentException("to_date is required");

            var fromDate = ToVnDate(dto.from_date);
            var toDate = ToVnDate(dto.to_date);

            if (toDate < fromDate)
                throw new ArgumentException("to_date must be greater than or equal to from_date");

            var holidayName = NormalizeNullable(dto.holiday_name, 255, "holiday_name");
            var holidayType = NormalizeRequiredOrDefault(dto.holiday_type, 50, "MANUAL");
            var note = NormalizeNullable(dto.note, null, "note");
            var isManualOverride = dto.is_manual_override ?? true;

            var now = AppTime.NowVnUnspecified();

            var totalDays = await _repo.UpsertRangeAsync(
                from: fromDate,
                to: toDate,
                holidayName: holidayName,
                holidayType: holidayType,
                isNonWorkingDay: dto.is_non_working_day,
                isManualOverride: isManualOverride,
                note: note,
                createdAt: now,
                updatedAt: now,
                ct: ct);

            await _repo.SaveChangesAsync(ct);
            return totalDays;
        }
    }
}