using System.Security.Claims;
using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Orders;
using AMMS.Shared.DTOs.Productions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        public ProductionsController(
            IProductionService service,
            IProductionSchedulingService svc,
            IOrderMaterialService orderMaterialService)
        {
            _service = service;
            _svc = svc;
            _orderMaterialService = orderMaterialService;
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

        [HttpGet("get-all-production")]
        public async Task<IActionResult> GetProducingOrders(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            CancellationToken ct = default)
        {
            var result = await _service.GetProducingOrdersAsync(page, pageSize, ct);
            return Ok(result);
        }

        [Authorize]
        [HttpGet("detail/{orderId:int}")]
        public async Task<IActionResult> GetProductionDetail(int orderId, CancellationToken ct)
        {
            var result = await _service.GetProductionDetailByOrderIdAsync(orderId, ct);
            if (result == null) return NotFound();

            var roleId = GetRoleId();

            result.stages = ProductionRoleMap.FilterStages(
                result.stages,
                roleId,
                x => x.process_code
            );

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

        [HttpPost("start/{orderId:int}")]
        public async Task<IActionResult> StartProduction(int orderId, CancellationToken ct)
        {
            var ok = await _service.StartProductionByOrderIdAsync(orderId, ct);

            if (!ok)
                return NotFound(new { message = "Production not found for this orderId" });

            return NoContent();
        }

        [HttpPut("delivery/{orderId:int}")]
        public async Task<IActionResult> SetDelivery(int orderId, CancellationToken ct)
        {
            var ok = await _service.SetProductionDeliveryAsync(orderId, ct);

            if (!ok)
                return NotFound(new { message = "Production not found for this orderId" });

            return NoContent();
        }
    }
}