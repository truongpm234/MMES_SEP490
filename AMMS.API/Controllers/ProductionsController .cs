using AMMS.API.Jobs;
using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Exceptions;
using AMMS.Shared.DTOs.Orders;
using AMMS.Shared.DTOs.Productions;
using AMMS.Shared.Helpers;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AMMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    //[Authorize]
    public class ProductionsController : ControllerBase
    {
        private readonly IProductionService _service;
        private readonly IProductionSchedulingService _svc;
        private readonly IOrderMaterialService _orderMaterialService;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ILogger<ProductionsController> _logger;

        public ProductionsController(
    IProductionService service,
    IProductionSchedulingService svc,
    IOrderMaterialService orderMaterialService,
    IBackgroundJobClient backgroundJobClient,
    ILogger<ProductionsController> logger)
        {
            _service = service;
            _svc = svc;
            _orderMaterialService = orderMaterialService;
            _backgroundJobClient = backgroundJobClient;
            _logger = logger;
        }

        private int? GetRoleId()
        {
            var roleIdValue =
                User.FindFirst("roleid")?.Value ??     
                User.FindFirst("role_id")?.Value ??
                User.FindFirst(ClaimTypes.Role)?.Value;

            if (int.TryParse(roleIdValue, out var roleId))
                return roleId;

            return null;
        }

        [HttpPost("schedule")]
        public async Task<IActionResult> Schedule([FromBody] ScheduleRequest req)
        {
            var prodId = await _svc.ScheduleOrderAsync(
                orderId: req.order_id,
                productTypeId: req.product_type_id,
                productionProcessCsv: req.production_processes,
                managerId: req.manager_id
            );

            return Ok(new { prod_id = prodId });
        }

        [HttpGet("nearest-delivery")]
        public async Task<IActionResult> GetNearestDelivery()
        {
            var result = await _service.GetNearestDeliveryAsync();
            return Ok(result);
        }

        [HttpGet("get-all-process-type")]
        public async Task<ActionResult<List<string>>> GetAllProcessTypeAsync()
        {
            var data = await _service.GetAllProcessTypeAsync();
            return Ok(data);
        }

        [Authorize]
        [HttpGet("get-all-production")]
        public async Task<IActionResult> GetProducingOrders(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            CancellationToken ct = default)
        {
            var roleId = GetRoleId();

            var result = await _service.GetProducingOrdersAsync(page, pageSize, roleId, ct);
            return Ok(result);
        }

        [Authorize]
        [HttpGet("detail/{orderId:int}")]
        public async Task<IActionResult> GetProductionDetail(int orderId, CancellationToken ct)
        {
            var result = await _service.GetProductionDetailByOrderIdAsync(orderId, ct);
            if (result == null) return NotFound();

            return Ok(result);
        }

        [HttpGet("progress/{prodId:int}")]
        public async Task<ActionResult<ProductionProgressResponse>> Progress(int prodId)
        {
            return Ok(await _service.GetProgressAsync(prodId));
        }

        [HttpGet("waste/{prodId:int}")]
        public async Task<IActionResult> GetWaste(int prodId, CancellationToken ct)
        {
            var result = await _service.GetProductionWasteAsync(prodId, ct);
            if (result == null) return NotFound();
            return Ok(result);
        }

        [HttpGet("information/{orderId:int}")]
        [ProducesResponseType(typeof(OrderMaterialsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<OrderMaterialsResponse>> Get(int orderId, CancellationToken ct)
        {
            var res = await _orderMaterialService.GetMaterialsByOrderIdAsync(orderId, ct);
            if (res == null) return NotFound();
            return Ok(res);
        }

        [HttpGet("start-ready/{orderId:int}")]
        public async Task<IActionResult> GetProductionReady(int orderId, CancellationToken ct)
        {
            var result = await _service.GetProductionReadyAsync(orderId, ct);
            if (result == null)
                return NotFound(new { message = "Order not found" });

            return Ok(result);
        }

        [HttpPut("start-ready/{orderId:int}")]
        public async Task<IActionResult> SetProductionReady(int orderId, [FromBody] ConfirmProductionReadyRequest req, CancellationToken ct)
        {
            var ok = await _service.SetProductionReadyAsync(orderId, req.is_production_ready, ct);
            if (!ok)
                return NotFound(new { message = "Order not found" });

            return Ok(new
            {
                order_id = orderId,
                is_production_ready = req.is_production_ready,
                message = req.is_production_ready
                    ? "General manager confirmed production readiness."
                    : "Production readiness confirmation has been removed."
            });
        }

        [HttpPost("start/{orderId:int}")]
        public async Task<IActionResult> StartProduction(int orderId, CancellationToken ct)
        {
            try
            {
                var prodId = await _service.StartProductionAndPromoteFirstTaskAsync(orderId, ct);

                if (!prodId.HasValue)
                    return NotFound(new { message = "Production not found for this orderId" });

                return Ok(new
                {
                    message = "Production started successfully",
                    prod_id = prodId.Value,
                    first_task_status = "Unassigned",
                    start_mode = "ManualReadyByStaff"
                });
            }
            catch (BomValidationException ex)
            {
                return BadRequest(new
                {
                    message = "Không thể bắt đầu sản xuất vì BOM còn dòng chưa map với id cùa nvl.",
                    order_id = orderId,
                    missing_bom_lines = ex.Items
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message,
                    order_id = orderId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Start production failed",
                    detail = ex.Message,
                    order_id = orderId
                });
            }
        }

        [HttpPut("delivery/{orderId:int}")]
        public async Task<IActionResult> SetDelivery(int orderId, CancellationToken ct)
        {
            var ok = await _service.SetProductionDeliveryAsync(orderId, ct);

            if (!ok)
                return NotFound(new { message = "Production not found for this orderId" });

            try
            {
                _backgroundJobClient.Enqueue<DeliveryHandoverEmailJob>(
                    x => x.RunAsync(orderId, CancellationToken.None));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to enqueue DeliveryHandoverEmailJob. orderId={OrderId}",
                    orderId);
            }

            return NoContent();
        }

        [HttpPut("competed/{orderId:int}")]
        public async Task<IActionResult> SetCompleted(int orderId, CancellationToken ct)
        {
            var ok = await _service.SetCompletedAsync(orderId, ct);

            if (!ok)
                return NotFound(new { message = "Production not found for this orderId" });

            return NoContent();
        }

        [HttpGet("machine-schedule")]
        public async Task<IActionResult> GetMachineSchedule(
    [FromQuery] DateTime? from,
    [FromQuery] DateTime? to,
    CancellationToken ct = default)
        {
            var now = AppTime.NowVnUnspecified();

            var rangeFrom = from ?? now.Date.AddDays(-7);
            var rangeTo = to ?? now.Date.AddDays(7);

            if (rangeTo <= rangeFrom)
                rangeTo = rangeFrom.AddDays(14);

            var data = await _service.GetMachineScheduleBoardAsync(rangeFrom, rangeTo, ct);

            return Ok(new
            {
                from = rangeFrom,
                to = rangeTo,
                machines = data
            });
        }
    }
}