using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Productions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly ITaskRepository _taskRepo;
    private readonly ITaskQrTokenService _tokenSvc;
    private readonly ITaskScanService _scanSvc;
    private readonly ITaskService _taskService;
    private readonly AppDbContext _db;

    public TasksController(
        AppDbContext db,
        ITaskRepository taskRepo,
        ITaskQrTokenService tokenSvc,
        ITaskScanService scanSvc,
        ITaskService taskService)
    {
        _db = db;
        _taskRepo = taskRepo;
        _tokenSvc = tokenSvc;
        _scanSvc = scanSvc;
        _taskService = taskService;
    }

    private int? GetCurrentUserId()
    {
        var raw =
            User.FindFirst("userid")?.Value ??
            User.FindFirst("user_id")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(raw, out var userId) ? userId : null;
    }

    [HttpGet("qr-prepare/{taskId:int}")]
    public async Task<IActionResult> GetQrPrepare(int taskId, CancellationToken ct)
    {
        var t = await _taskRepo.GetByIdAsync(taskId);
        if (t == null) return NotFound(new { message = "Task not found", task_id = taskId });

        var policy = await _taskRepo.GetQtyPolicyAsync(taskId, ct);
        var bundle = await _scanSvc.GetTaskQrMaterialBundleAsync(taskId, ct);

        return Ok(new
        {
            task_id = taskId,
            process_code = policy?.process_code,
            process_name = policy?.process_name,
            qty_unit = policy?.qty_unit,
            min_allowed = policy?.min_allowed,
            max_allowed = policy?.max_allowed,
            suggested_qty = policy?.suggested_qty,
            consumable_materials = bundle.consumable_materials,
            reference_inputs = bundle.reference_inputs
        });
    }

    [HttpPost("qr")]
    public async Task<ActionResult<TaskQrResponse>> CreateQr([FromBody] CreateTaskQrRequest req, CancellationToken ct)
    {
        var t = await _taskRepo.GetByIdAsync(req.task_id);
        if (t == null) return NotFound();

        var policy = await _taskRepo.GetQtyPolicyAsync(req.task_id, ct);
        if (policy == null)
        {
            return BadRequest(new
            {
                message = "Không xác định được ngưỡng số lượng hợp lệ cho công đoạn này.",
                task_id = req.task_id
            });
        }

        var ttl = TimeSpan.FromMinutes(Math.Max(1, req.ttl_minutes));
        var isAuto = !req.qty_good.HasValue || req.qty_good.Value <= 0;

        int qtyGood;
        if (isAuto)
        {
            qtyGood = policy.suggested_qty;
            if (qtyGood <= 0) qtyGood = 1;
        }
        else
        {
            qtyGood = req.qty_good!.Value;

            if (qtyGood < policy.min_allowed || qtyGood > policy.max_allowed)
            {
                return BadRequest(new
                {
                    message = $"Số lượng báo cáo không hợp lệ. Công đoạn [{policy.process_code} - {policy.process_name}] chỉ cho phép trong khoảng {policy.min_allowed} ---> {policy.max_allowed} {policy.qty_unit}.",
                    task_id = req.task_id,
                    process_code = policy.process_code,
                    process_name = policy.process_name,
                    qty_unit = policy.qty_unit,
                    min_allowed = policy.min_allowed,
                    max_allowed = policy.max_allowed,
                    suggested_qty = policy.suggested_qty,
                    order_qty = policy.order_qty,
                    sheets_required = policy.sheets_required,
                    sheets_waste = policy.sheets_waste,
                    sheets_total = policy.sheets_total,
                    n_up = policy.n_up,
                    number_of_plates = policy.number_of_plates
                });
            }
        }

        var inputMaterials = await _scanSvc.BuildMaterialUsageForQrAsync(req.task_id,
    req.materials,
    ct);

        var token = _tokenSvc.CreateToken(req.task_id, qtyGood, inputMaterials, ttl);
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();

        var qrMaterialBundle = await _scanSvc.GetTaskQrMaterialBundleAsync(req.task_id, ct);

        return new TaskQrResponse
        {
            task_id = req.task_id,
            token = token,
            expires_at_unix = expiresAt,
            qty_good_used = qtyGood,
            is_auto_filled = isAuto,
            min_allowed = policy.min_allowed,
            max_allowed = policy.max_allowed,
            suggested_qty = policy.suggested_qty,
            qty_unit = policy.qty_unit,
            process_code = policy.process_code,
            process_name = policy.process_name,
            embedded_material_count = inputMaterials.Count,
            consumable_materials = qrMaterialBundle.consumable_materials,
            reference_inputs = qrMaterialBundle.reference_inputs
        };
    }

    [HttpPost("finish")]
    public async Task<ActionResult<ScanTaskResult>> Finish([FromBody] ScanTaskRequest req)
    {
        if (req == null || string.IsNullOrEmpty(req.token))
            return BadRequest("Invalid request or missing token.");

        var scannedByUserId = GetCurrentUserId();
        var res = await _scanSvc.ScanFinishAsync(req, scannedByUserId);

        return Ok(res);
    }

    [HttpPut("ready")]
    public async Task<IActionResult> SetTaskReady([FromBody] SetTaskReadyRequest req, CancellationToken ct)
    {
        try
        {
            var ok = await _taskService.SetTaskReadyAsync(req.task_id, ct);

            if (!ok)
                return NotFound(new { message = "Task not found", task_id = req.task_id });

            return Ok(new
            {
                message = "Task status updated to Ready",
                task_id = req.task_id,
                status = "Ready"
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message,
                task_id = req.task_id
            });
        }
    }

    [HttpPost("cancel-finish/{taskId:int}")]
    public async Task<IActionResult> CancelFinish(
    int taskId,
    [FromBody] CancelTaskFinishRequest? req,
    CancellationToken ct)
    {
        try
        {
            var cancelledByUserId = GetCurrentUserId();

            var result = await _scanSvc.CancelTaskFinishAsync(
                taskId,
                req,
                cancelledByUserId,
                ct);

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                message = ex.Message,
                task_id = taskId
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message,
                task_id = taskId
            });
        }
    }

    private static List<TaskMaterialUsageInputDto> NormalizeQrMaterials(List<TaskMaterialUsageInputDto>? materials)
    {
        return (materials ?? new List<TaskMaterialUsageInputDto>())
            .Select(x => new TaskMaterialUsageInputDto
            {
                material_id = x.material_id,
                quantity_used = Math.Round(x.quantity_used, 4),
                quantity_left = Math.Round(x.quantity_left, 4),
                is_stock = x.is_stock
            })
            .ToList();
    }

    [HttpPost("finish-from-stock")]
    public async Task<IActionResult> FinishFromStock([FromBody] FinishTaskFromStockRequest req, CancellationToken ct)
    {
        if (req == null || req.task_id <= 0)
            return BadRequest(new { message = "task_id is required" });

        try
        {
            var ok = await _taskService.FinishTaskFromStockAsync(req.task_id, GetCurrentUserId(), ct);
            if (!ok)
                return NotFound(new { message = "Task not found", task_id = req.task_id });

            return Ok(new
            {
                message = "Task status updated to Finished",
                task_id = req.task_id,
                status = "Finished",
                reason = "Bán thành phẩm đã có sẵn trong kho"
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message,
                task_id = req.task_id
            });
        }
    }
    }