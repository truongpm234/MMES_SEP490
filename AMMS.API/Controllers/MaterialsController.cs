using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Shared.DTOs.Materials;
using ExcelDataReader;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text;

namespace AMMS.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MaterialsController : ControllerBase
    {
        private readonly IMaterialService _materialService;
        private readonly AppDbContext _context;

        public MaterialsController(IMaterialService materialService, AppDbContext context)
        {
            _materialService = materialService;
            _context = context;
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

        [HttpGet("stock-alerts")]
        public async Task<IActionResult> GetMaterialStockAlerts([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] decimal nearMinThresholdPercent = 0.2m, CancellationToken ct = default)
        {
            var result = await _materialService.GetMaterialStockAlertsPagedAsync(
                page, pageSize, nearMinThresholdPercent, ct);

            return Ok(result);
        }
        [HttpPost("import-material-from-excel")]
        public async Task<IActionResult> ImportMaterial(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("File không hợp lệ");

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                int createdCount = 0;
                int updatedCount = 0;

                using var stream = file.OpenReadStream();

                IExcelDataReader reader;

                string extension = Path.GetExtension(file.FileName).ToLower();

                if (extension == ".csv")
                {
                    reader = ExcelReaderFactory.CreateCsvReader(stream);
                }
                else
                {
                    reader = ExcelReaderFactory.CreateReader(stream);
                }

                using (reader)
                {
                    var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                    {
                        ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                        {
                            UseHeaderRow = true
                        }
                    });

                    DataTable table = result.Tables[0];

                    foreach (DataRow row in table.Rows)
                    {
                        string code = row["code"]?.ToString()?.Trim() ?? "";

                        if (string.IsNullOrWhiteSpace(code))
                            continue;

                        var existingMaterial = await _context.materials
                            .FirstOrDefaultAsync(x => x.code == code);

                        // =========================
                        // CREATE NEW
                        // =========================
                        if (existingMaterial == null)
                        {
                            var newMaterial = new material
                            {
                                code = code,
                                name = row["name"]?.ToString()?.Trim() ?? "",
                                unit = row["unit"]?.ToString()?.Trim() ?? "",

                                stock_qty = ParseNullableDecimal(row["stock_qty"]?.ToString()),
                                min_stock = ParseNullableDecimal(row["min_stock"]?.ToString()),
                                cost_price = ParseNullableDecimal(row["cost_price"]?.ToString()),

                                description = row["description"]?.ToString()?.Trim(),

                                sheet_width_mm = ParseNullableInt(row["sheet_width_mm"]?.ToString()),
                                sheet_thick_mm = ParseNullableInt(row["sheet_thick_mm"]?.ToString()),
                                sheet_length_mm = ParseNullableInt(row["sheet_length_mm"]?.ToString()),

                                type = row["type"]?.ToString()?.Trim(),
                                material_class = row["material_class"]?.ToString()?.Trim()
                            };

                            await _context.materials.AddAsync(newMaterial);

                            createdCount++;
                        }
                        // =========================
                        // UPDATE EXISTING
                        // =========================
                        else
                        {
                            existingMaterial.name = row["name"]?.ToString()?.Trim() ?? "";
                            existingMaterial.unit = row["unit"]?.ToString()?.Trim() ?? "";

                            existingMaterial.stock_qty = ParseNullableDecimal(row["stock_qty"]?.ToString());
                            existingMaterial.min_stock = ParseNullableDecimal(row["min_stock"]?.ToString());
                            existingMaterial.cost_price = ParseNullableDecimal(row["cost_price"]?.ToString());

                            existingMaterial.description = row["description"]?.ToString()?.Trim();

                            existingMaterial.sheet_width_mm = ParseNullableInt(row["sheet_width_mm"]?.ToString());
                            existingMaterial.sheet_thick_mm = ParseNullableInt(row["sheet_thick_mm"]?.ToString());
                            existingMaterial.sheet_length_mm = ParseNullableInt(row["sheet_length_mm"]?.ToString());

                            existingMaterial.type = row["type"]?.ToString()?.Trim();
                            existingMaterial.material_class = row["material_class"]?.ToString()?.Trim();

                            updatedCount++;
                        }
                    }
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Import thành công",
                    created = createdCount,
                    updated = updatedCount
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = ex.Message
                });
            }
        }

        private decimal? ParseNullableDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (decimal.TryParse(value, out decimal result))
                return result;

            return null;
        }

        private int? ParseNullableInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (int.TryParse(value, out int result))
                return result;

            return null;
        }
    }
}
