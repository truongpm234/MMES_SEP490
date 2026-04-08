using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Productions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly ITaskRepository _taskRepo;
    private readonly ITaskQrTokenService _tokenSvc;
    private readonly ITaskScanService _svc;

    public TasksController(ITaskRepository taskRepo, ITaskQrTokenService tokenSvc, ITaskScanService svc)
    {
        _taskRepo = taskRepo;
        _tokenSvc = tokenSvc;
        _svc = svc;
    }

    private int? GetCurrentUserId()
    {
        var raw =
            User.FindFirst("userid")?.Value ??
            User.FindFirst("user_id")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(raw, out var userId) ? userId : null;
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
            if (qtyGood <= 0)
                qtyGood = 1;
        }
        else
        {
            qtyGood = req.qty_good!.Value;

            if (qtyGood < policy.min_allowed || qtyGood > policy.max_allowed)
            {
                return BadRequest(new
                {
                    message = $"Số lượng báo cáo không hợp lệ. Công đoạn [{policy.process_code} - {policy.process_name}] chỉ cho phép trong khoảng 1 ---> {policy.max_allowed} {policy.qty_unit}.",
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

        var token = _tokenSvc.CreateToken(req.task_id, qtyGood, ttl);
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();

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
            process_name = policy.process_name
        };
    }

    [Authorize]
    [HttpPost("finish")]
    public async Task<ActionResult<ScanTaskResult>> Finish([FromBody] ScanTaskRequest req)
    {
        if (req == null || string.IsNullOrEmpty(req.token))
            return BadRequest("Invalid request or missing token.");

        var scannedByUserId = GetCurrentUserId();
        var res = await _svc.ScanFinishAsync(req, scannedByUserId);

        return Ok(res);
    }
}