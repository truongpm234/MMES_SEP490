using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Planning;
using AMMS.Shared.DTOs.ProductionCalendars;
using Microsoft.AspNetCore.Mvc;

namespace AMMS.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductionCalendarsController : ControllerBase
    {
        private readonly IProductionCalendarService _service;

        public ProductionCalendarsController(IProductionCalendarService service)
        {
            _service = service;
        }

        [HttpGet("by-date")]
        public async Task<IActionResult> GetByDate([FromQuery] DateTime date, CancellationToken ct)
        {
            if (date == default)
                return BadRequest(new { message = "date is required" });

            var row = await _service.GetByDateAsync(date, ct);

            if (row == null)
            {
                return Ok(new
                {
                    calendar_date = date.Date,
                    source = date.Date.DayOfWeek == DayOfWeek.Sunday ? "SUNDAY_RULE" : "DEFAULT_WORKING_DAY",
                    is_non_working_day = date.Date.DayOfWeek == DayOfWeek.Sunday
                });
            }

            return Ok(row);
        }
        [HttpGet("all-date")]
        public async Task<IActionResult> GetAllDate()
        {
            return Ok(await _service.GetAllDate());
        }

        [HttpGet("range")]
        public async Task<IActionResult> GetRange([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        {
            if (from == default || to == default)
                return BadRequest(new { message = "from and to are required" });

            var rows = await _service.GetRangeAsync(from, to, ct);
            return Ok(rows);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateProductionCalendarRequest dto, CancellationToken ct)
        {
            if (dto == null || dto.calendar_date == default)
                return BadRequest(new { message = "calendar_date is required" });

            try
            {
                await _service.CreateAsync(dto, ct);

                return StatusCode(StatusCodes.Status201Created, new
                {
                    message = "Production calendar created successfully",
                    calendar_date = dto.calendar_date.Date
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut]
        public async Task<IActionResult> Upsert([FromBody] ProductionCalendarDto dto, CancellationToken ct)
        {
            if (dto == null || dto.calendar_date == default)
                return BadRequest(new { message = "calendar_date is required" });

            await _service.UpsertAsync(dto, ct);

            return Ok(new
            {
                message = "Production calendar updated successfully",
                calendar_date = dto.calendar_date.Date
            });
        }

        [HttpDelete]
        public async Task<IActionResult> Delete([FromQuery] DateTime date, CancellationToken ct)
        {
            if (date == default)
                return BadRequest(new { message = "date is required" });

            var ok = await _service.DeleteAsync(date, ct);
            if (!ok)
                return NotFound(new { message = "Calendar date not found", calendar_date = date.Date });

            return Ok(new
            {
                message = "Production calendar deleted successfully",
                calendar_date = date.Date
            });
        }

        [HttpPost("create-range")]
        public async Task<IActionResult> CreateRange([FromBody] CreateProductionCalendarRangeRequest dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload is required" });

            if (dto.from_date == default || dto.to_date == default)
                return BadRequest(new { message = "from_date and to_date are required" });

            try
            {
                var totalDays = await _service.CreateRangeAsync(dto, ct);

                return StatusCode(StatusCodes.Status201Created, new
                {
                    message = "Production calendar range created successfully",
                    from_date = dto.from_date.Date,
                    to_date = dto.to_date.Date,
                    total_days = totalDays,
                    holiday_name = dto.holiday_name,
                    holiday_type = string.IsNullOrWhiteSpace(dto.holiday_type)
                        ? "MANUAL"
                        : dto.holiday_type.Trim().ToUpperInvariant(),
                    is_non_working_day = dto.is_non_working_day
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}