using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Planning;
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
            var d = date.Date;

            var row = await _repo.GetByDateAsync(d, ct);
            if (row != null)
                return !row.is_non_working_day;

            // cn nghỉ
            return d.DayOfWeek != DayOfWeek.Sunday;
        }

        public async Task<ProductionCalendarDto?> GetByDateAsync(DateTime date, CancellationToken ct = default)
        {
            var row = await _repo.GetByDateAsync(date.Date, ct);
            if (row == null) return null;

            return new ProductionCalendarDto
            {
                calendar_date = row.calendar_date,
                holiday_name = row.holiday_name,
                holiday_type = row.holiday_type,
                is_non_working_day = row.is_non_working_day,
                is_manual_override = row.is_manual_override,
                note = row.note
            };
        }

        public async Task<List<ProductionCalendarDto>> GetRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            var rows = await _repo.GetRangeAsync(from, to, ct);

            return rows.Select(row => new ProductionCalendarDto
            {
                calendar_date = row.calendar_date,
                holiday_name = row.holiday_name,
                holiday_type = row.holiday_type,
                is_non_working_day = row.is_non_working_day,
                is_manual_override = row.is_manual_override,
                note = row.note
            }).ToList();
        }

        public async Task UpsertAsync(ProductionCalendarDto dto, CancellationToken ct = default)
        {
            if (dto.calendar_date == default)
                throw new ArgumentException("calendar_date is required");

            var now = AppTime.NowVnUnspecified();

            var entity = new production_calendar
            {
                calendar_date = dto.calendar_date.Date,
                holiday_name = string.IsNullOrWhiteSpace(dto.holiday_name) ? null : dto.holiday_name.Trim(),
                holiday_type = string.IsNullOrWhiteSpace(dto.holiday_type) ? "MANUAL" : dto.holiday_type.Trim().ToUpperInvariant(),
                is_non_working_day = dto.is_non_working_day,
                is_manual_override = true, // CRUD admin => manual
                note = string.IsNullOrWhiteSpace(dto.note) ? null : dto.note.Trim(),
                created_at = now,
                updated_at = now
            };

            await _repo.UpsertAsync(entity, ct);
            await _repo.SaveChangesAsync(ct);
        }

        public async Task<bool> DeleteAsync(DateTime date, CancellationToken ct = default)
        {
            var ok = await _repo.DeleteByDateAsync(date.Date, ct);
            if (!ok) return false;

            await _repo.SaveChangesAsync(ct);
            return true;
        }
    }
}
