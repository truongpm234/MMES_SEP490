using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Infrastructure.Repositories
{
    public class ProductionCalendarRepository : IProductionCalendarRepository
    {
        private readonly AppDbContext _db;

        public ProductionCalendarRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<production_calendar?> GetByDateAsync(DateTime date, CancellationToken ct = default)
        {
            var d = ToUnspecifiedDate(date);

            return await _db.production_calendars
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.calendar_date == d, ct);
        }

        public async Task<List<production_calendar>> GetAllDate()
        {
            return await _db.production_calendars.ToListAsync();
        }

        public async Task<production_calendar?> GetByDateTrackingAsync(DateTime date, CancellationToken ct = default)
        {
            var d = ToUnspecifiedDate(date);

            return await _db.production_calendars
                .FirstOrDefaultAsync(x => x.calendar_date == d, ct);
        }

        public async Task<List<production_calendar>> GetRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            var fromDate = ToUnspecifiedDate(from);
            var toDate = ToUnspecifiedDate(to);

            if (toDate < fromDate)
                (fromDate, toDate) = (toDate, fromDate);

            return await _db.production_calendars
                .AsNoTracking()
                .Where(x => x.calendar_date >= fromDate && x.calendar_date <= toDate)
                .OrderBy(x => x.calendar_date)
                .ToListAsync(ct);
        }

        public async Task AddAsync(production_calendar entity, CancellationToken ct = default)
        {
            entity.calendar_date = ToUnspecifiedDate(entity.calendar_date);
            entity.created_at = ToUnspecified(entity.created_at);
            entity.updated_at = ToUnspecified(entity.updated_at);

            await _db.production_calendars.AddAsync(entity, ct);
        }

        public async Task UpsertAsync(production_calendar entity, CancellationToken ct = default)
        {
            var d = ToUnspecifiedDate(entity.calendar_date);

            var existing = await _db.production_calendars
                .FirstOrDefaultAsync(x => x.calendar_date == d, ct);

            if (existing == null)
            {
                entity.calendar_date = d;
                entity.created_at = ToUnspecified(entity.created_at);
                entity.updated_at = ToUnspecified(entity.updated_at);
                await _db.production_calendars.AddAsync(entity, ct);
                return;
            }

            existing.holiday_name = entity.holiday_name;
            existing.holiday_type = entity.holiday_type;
            existing.is_non_working_day = entity.is_non_working_day;
            existing.is_manual_override = entity.is_manual_override;
            existing.note = entity.note;
            existing.updated_at = ToUnspecified(entity.updated_at);
        }

        public async Task<bool> DeleteByDateAsync(DateTime date, CancellationToken ct = default)
        {
            var d = ToUnspecifiedDate(date);

            var existing = await _db.production_calendars
                .FirstOrDefaultAsync(x => x.calendar_date == d, ct);

            if (existing == null)
                return false;

            _db.production_calendars.Remove(existing);
            return true;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
            => _db.SaveChangesAsync(ct);

        private static DateTime ToUnspecifiedDate(DateTime value)
            => DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

        private static DateTime ToUnspecified(DateTime value)
            => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

        public async Task<int> UpsertRangeAsync(
    DateTime from,
    DateTime to,
    string? holidayName,
    string holidayType,
    bool isNonWorkingDay,
    bool isManualOverride,
    string? note,
    DateTime createdAt,
    DateTime updatedAt,
    CancellationToken ct = default)
        {
            var fromDate = ToUnspecifiedDate(from);
            var toDate = ToUnspecifiedDate(to);

            if (toDate < fromDate)
                (fromDate, toDate) = (toDate, fromDate);

            var existingRows = await _db.production_calendars
                .Where(x => x.calendar_date >= fromDate && x.calendar_date <= toDate)
                .ToListAsync(ct);

            var existingMap = existingRows.ToDictionary(x => x.calendar_date.Date);

            var count = 0;

            for (var date = fromDate; date <= toDate; date = date.AddDays(1))
            {
                var key = date.Date;

                if (existingMap.TryGetValue(key, out var existing))
                {
                    existing.holiday_name = holidayName;
                    existing.holiday_type = holidayType;
                    existing.is_non_working_day = isNonWorkingDay;
                    existing.is_manual_override = isManualOverride;
                    existing.note = note;
                    existing.updated_at = ToUnspecified(updatedAt);
                }
                else
                {
                    var entity = new production_calendar
                    {
                        calendar_date = ToUnspecifiedDate(date),
                        holiday_name = holidayName,
                        holiday_type = holidayType,
                        is_non_working_day = isNonWorkingDay,
                        is_manual_override = isManualOverride,
                        note = note,
                        created_at = ToUnspecified(createdAt),
                        updated_at = ToUnspecified(updatedAt)
                    };

                    await _db.production_calendars.AddAsync(entity, ct);
                }

                count++;
            }

            return count;
        }
    }
}