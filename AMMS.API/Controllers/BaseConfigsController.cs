using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AMMS.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BaseConfigsController : ControllerBase
    {
        private readonly IBaseConfigService _service;

        public BaseConfigsController(IBaseConfigService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var data = await _service.GetAsync(ct);
            return Ok(data);
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] UpdateEstimateBaseConfigRequest dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Request body is required" });

            try
            {
                await _service.UpdateAsync(dto, ct);

                return Ok(new
                {
                    message = "Base configuration updated successfully",
                    note = "config_group and config_key are fixed"
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
