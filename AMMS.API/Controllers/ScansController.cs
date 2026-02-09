using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AMMS.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScanController : ControllerBase
    {
        private readonly ScanService _scan;

        public ScanController(ScanService scan) => _scan = scan;

        [HttpPost("task")]
        public async Task<IActionResult> ScanTask([FromBody] ScanTaskDto dto, CancellationToken ct)
        {
            await _scan.ScanAsync(dto.task_id, dto.scanned_code, dto.action_type, dto.qty_good, ct);
            return Ok(new { ok = true });
        }
    }

    public record ScanTaskDto(int task_id, string scanned_code, string action_type, int qty_good);
}
