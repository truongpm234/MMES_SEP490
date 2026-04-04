using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Materials;
using Microsoft.AspNetCore.Mvc;

namespace AMMS.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MaterialsController : ControllerBase
    {
        private readonly IMaterialService _materialService;

        public MaterialsController(IMaterialService materialService)
        {
            _materialService = materialService;
        }

        [HttpGet("get-material-by-{id}")]
        public async Task<IActionResult> GetMaterialById(int id)
        {
            var material = await _materialService.GetByIdAsync(id);
            if (material == null)
            {
                return NotFound(new { message = "Material not found" });
            }
            return Ok(material);
        }

        [HttpGet("get-all-materials")]
        public async Task<IActionResult> GetAllMaterials()
        {
            var materials = await _materialService.GetAllAsync();
            return Ok(materials);
        }

        [HttpGet("get-all-paper-type")]
        public async Task<ActionResult<List<string>>> GetAllPaperTypeAsync()
        {
            var data = await _materialService.GetAllPaperTypeAsync();
            return Ok(data);
        }

        [HttpGet("shortage-for-orders")]
        public async Task<IActionResult> GetShortageForAllOrders(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            CancellationToken ct = default)
        {
            var result = await _materialService.GetShortageForAllOrdersPagedAsync(page, pageSize, ct);
            return Ok(result);
        }

        [HttpGet("get-material-by-type-song")]
        public async Task<IActionResult> GetMaterialByTypeSong()
        {
            var materials = await _materialService.GetMaterialByTypeSongAsync();
            return Ok(materials);
        }

        [HttpGet("get-all-glue-type-dan")]
        public async Task<IActionResult> GetAllDanGlueTypeAsync()
        {
            var glue = await _materialService.GetAllDanGlueTypeAsync();
            return Ok(glue);
        }

        [HttpGet("get-all-glue-type-boi")]
        public async Task<IActionResult> GetAllBoiGlueTypeAsync()
        {
            var glue = await _materialService.GetAllBoiGlueTypeAsync();
            return Ok(glue);
        }

        [HttpGet("get-all-glue-type-phu")]
        public async Task<IActionResult> GetAllPhuGlueTypeAsync()
        {
            var glue = await _materialService.GetAllPhuGlueTypeAsync();
            return Ok(glue);
        }
        [HttpPost("{materialId}/increase-stock")]
        public async Task<IActionResult> IncreaseStock(int materialId, [FromBody] UpdateStockQtyDto dto)
        {
            try
            {
                var result = await _materialService.IncreaseStockAsync(materialId, dto.Quantity);
                if (!result)
                    return NotFound(new { message = "Không tìm thấy material." });

                return Ok(new { message = "Tăng stock_qty thành công." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{materialId}/decrease-stock")]
        public async Task<IActionResult> DecreaseStock(int materialId, [FromBody] UpdateStockQtyDto dto)
        {
            try
            {
                var result = await _materialService.DecreaseStockAsync(materialId, dto.Quantity);
                if (!result)
                    return NotFound(new { message = "Không tìm thấy material." });

                return Ok(new { message = "Giảm stock_qty thành công." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
