using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Products;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AMMS.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductReceiptsController : ControllerBase
    {
        private readonly IProductReceiptService _service;

        public ProductReceiptsController(IProductReceiptService service)
        {
            _service = service;
        }

        private int? GetCurrentUserId()
        {
            var raw =
                User.FindFirst("userid")?.Value ??
                User.FindFirst("user_id")?.Value ??
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return int.TryParse(raw, out var userId) ? userId : null;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateProductReceiptDto dto, CancellationToken ct)
        {
            try
            {
                var result = await _service.CreateAsync(dto, GetCurrentUserId(), ct);
                return StatusCode(StatusCodes.Status201Created, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
