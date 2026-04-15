using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Productions;
using AMMS.Shared.DTOs.Socket;
using AMMS.Shared.Helpers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AMMS.Application.Services
{
    public class TaskScanService : ITaskScanService
    {
        private readonly NotificationService _noti;
        private readonly ITaskQrTokenService _tokenSvc;
        private readonly ITaskRepository _taskRepo;
        private readonly ITaskLogRepository _logRepo;
        private readonly IProductionRepository _prodRepo;
        private readonly IMachineRepository _machineRepo;
        private readonly IHubContext<RealtimeHub> _hub;
        private readonly IRequestRepository _orderRequestRepo;
        private readonly IOrderRepository _orderRepo;
        private readonly AppDbContext _db;

        public TaskScanService(
            NotificationService noti,
            AppDbContext db,
            ITaskQrTokenService tokenSvc,
            ITaskRepository taskRepo,
            ITaskLogRepository logRepo,
            IProductionRepository productionRepo,
            IMachineRepository machineRepo,
            IHubContext<RealtimeHub> hub,
            IRequestRepository orderRequestRepo,
            IOrderRepository orderRepo)
        {
            _noti = noti;
            _tokenSvc = tokenSvc;
            _taskRepo = taskRepo;
            _logRepo = logRepo;
            _prodRepo = productionRepo;
            _machineRepo = machineRepo;
            _hub = hub;
            _orderRequestRepo = orderRequestRepo;
            _orderRepo = orderRepo;
            _db = db;
        }

        public async Task<ScanTaskResult> ScanFinishAsync(ScanTaskRequest req, int? scannedByUserId, CancellationToken ct = default)
        {
            if (!_tokenSvc.TryValidate(req.token, out var taskId, out var qtyGood, out var reason))
                throw new ArgumentException(reason);

            var expectedMaterials = await GetConsumableMaterialsForTaskAsync(taskId, ct);
            ValidateMaterialUsageInput(expectedMaterials, req.materials ?? new List<TaskMaterialUsageInputDto>());

            var materialUsageSnapshot = BuildMaterialUsageSnapshot(
                expectedMaterials,
                req.materials ?? new List<TaskMaterialUsageInputDto>());

            var materialUsageJson = materialUsageSnapshot.Count == 0
                ? null
                : JsonSerializer.Serialize(materialUsageSnapshot, _jsonOptions);

            var policy = await _taskRepo.GetQtyPolicyAsync(taskId, ct);
            if (policy == null)
                throw new InvalidOperationException("Không xác định được policy số lượng cho task.");

            if (qtyGood < policy.min_allowed || qtyGood > policy.max_allowed)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(req.token),
                    $"qty_good={qtyGood} vượt ngoài khoảng cho phép {policy.min_allowed}..{policy.max_allowed} {policy.qty_unit} của công đoạn {policy.process_code}.");
            }

            var result = await _taskRepo.ExecuteInTransactionAsync(async innerCt =>
            {
                var t = await _taskRepo.GetByIdAsync(taskId)
                    ?? throw new Exception("Task not found");

                if (!t.prod_id.HasValue || !t.seq_num.HasValue)
                    throw new Exception("Task missing prod_id/seq_num");

                var flowTasks = await _taskRepo.GetTasksByProductionWithProcessAsync(t.prod_id.Value, innerCt);
                var currentFlow = flowTasks.FirstOrDefault(x => x.task_id == t.task_id)
                    ?? throw new Exception("Task flow info not found");

                var currentCode = ProductionFlowHelper.Norm(currentFlow.process?.process_code);
                var hasRalo = flowTasks.Any(x => ProductionFlowHelper.IsRalo(x.process?.process_code));
                var raloFinished = !hasRalo || flowTasks.Any(x =>
                    ProductionFlowHelper.IsRalo(x.process?.process_code) &&
                    string.Equals(x.status, "Finished", StringComparison.OrdinalIgnoreCase));

                if (!hasRalo)
                {
                    var prev = await _taskRepo.GetPrevTaskAsync(t.prod_id.Value, t.seq_num.Value);
                    if (prev != null && !string.Equals(prev.status, "Finished", StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"Previous step (task_id={prev.task_id}, seq={prev.seq_num}) is not Finished");
                }
                else
                {
                    if (ProductionFlowHelper.NeedsRaloGate(currentCode) && !raloFinished)
                        throw new Exception("RALO must be Finished before this task can be scanned");
                }

                if (!string.Equals(t.status, "Ready", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Task status '{t.status}' is not scannable. Only Ready can be finished.");

                var now = AppTime.NowVnUnspecified();

                if (!string.IsNullOrWhiteSpace(t.machine))
                    await _machineRepo.ReleaseAsync(t.machine!, release: 1);

                t.status = "Finished";
                t.start_time ??= now;
                t.end_time = now;

                var finishLog = new task_log
                {
                    task_id = t.task_id,
                    scanned_code = req.token,
                    action_type = "Finished",
                    qty_good = qtyGood,
                    log_time = now,
                    scanned_by_user_id = scannedByUserId,
                    material_usage_json = materialUsageJson
                };
                await _logRepo.AddAsync(finishLog);

                // log từng NVL
                foreach (var item in req.materials ?? new List<TaskMaterialUsageInputDto>())
                {
                    if (item.quantity_left > 0 && item.is_stock)
                    {
                        var mat = await _db.materials
                            .FirstOrDefaultAsync(x => x.material_id == item.material_id, innerCt);

                        if (mat == null)
                            throw new InvalidOperationException($"Material not found. material_id={item.material_id}");

                        mat.stock_qty = (mat.stock_qty ?? 0m) + item.quantity_left;

                        await _db.stock_moves.AddAsync(new stock_move
                        {
                            material_id = item.material_id,
                            type = "IN",
                            qty = item.quantity_left,
                            ref_doc = $"TASK-LEFTOVER-{t.task_id}",
                            user_id = scannedByUserId,
                            move_date = now,
                            note = $"Return leftover from task_id={t.task_id}, prod_id={t.prod_id}",
                            purchase_id = null
                        }, innerCt);
                    }
                }

                await _taskRepo.SaveChangesAsync(innerCt);

                if (t.prod_id.HasValue)
                    await _prodRepo.TryCloseProductionIfCompletedAsync(t.prod_id.Value, now, innerCt);

                production? prod = null;
                order? ord = null;

                if (t.prod_id.HasValue)
                    prod = await _prodRepo.GetByIdForUpdateAsync(t.prod_id.Value, innerCt);

                if (prod != null
                    && string.Equals(prod.status, "Importing", StringComparison.OrdinalIgnoreCase)
                    && prod.order_id.HasValue)
                {
                    ord = await _orderRepo.GetByIdForUpdateAsync(prod.order_id.Value, innerCt);

                    if (ord != null &&
                        !string.Equals(ord.status, "Importing", StringComparison.OrdinalIgnoreCase))
                    {
                        ord.status = "Importing";
                        var request = await _db.order_requests.FirstOrDefaultAsync(o => o.order_id == ord.order_id);
                        await _hub.Clients.All.SendAsync("finishedProduction",
                        new { message = $"Đơn hàng {prod.order_id} đã được sản xuất xong" });
                        if (request != null)
                        {
                            await _hub.Clients.Group(RealtimeGroups.ByRole("warehouse manager")).SendAsync("Importing", new { message = $"Đơn hàng {ord.order_id} đã được sản xuất xong, chờ nhập kho" });
                            await _noti.CreateNotfi(4, $"Đơn hàng {ord.order_id} đã được sản xuất xong, chờ nhập kho", null, request.order_request_id, "Importing");
                        }
                    }

                    if (ord != null)
                    {
                        await _orderRequestRepo.MarkProcessStatusImportingByOrderAsync(
                            ord.order_id,
                            ord.quote_id,
                            innerCt);

                        await _orderRequestRepo.MarkProcessStatusImportingByOrderAsync(
                            ord.order_id,
                            null,
                            innerCt);
                    }
                }

                await _hub.Clients.All.SendAsync("update-ui", new { message = "update UI" });

                return new ScanTaskResult
                {
                    task_id = t.task_id,
                    prod_id = t.prod_id,
                    message = $"Finished & logged qty_good={qtyGood}. Đã lưu log NVL sử dụng/hoàn kho."
                };
            }, ct);

            if (result.prod_id.HasValue)
            {
                var prod = await _prodRepo.GetByIdForUpdateAsync(result.prod_id.Value, ct);
                if (prod?.order_id != null)
                {
                    await _hub.Clients.All.SendAsync("finishedProduction",
                        new { message = $"Đơn hàng {prod.order_id} đã được sản xuất xong" });
                }
            }

            return result;
        }

        private static void ValidateMaterialUsageInput(
    List<TaskConsumableMaterialDto> expectedMaterials,
    List<TaskMaterialUsageInputDto> inputMaterials)
        {
            if (expectedMaterials.Any(x => !x.is_mapped))
            {
                var unmapped = string.Join(", ",
                    expectedMaterials
                        .Where(x => !x.is_mapped)
                        .Select(x => x.material_code));

                throw new InvalidOperationException(
                    $"Có NVL chưa map sang materials: {unmapped}. Không thể finish task.");
            }

            if (expectedMaterials.Count == 0)
            {
                if (inputMaterials != null && inputMaterials.Count > 0)
                    throw new InvalidOperationException("Task này không yêu cầu nhập NVL.");
                return;
            }

            if (inputMaterials == null || inputMaterials.Count == 0)
                throw new InvalidOperationException("Bắt buộc nhập danh sách NVL đã sử dụng.");

            if (inputMaterials.Count != expectedMaterials.Count)
                throw new InvalidOperationException("Số lượng NVL nhập vào không khớp với số NVL ước tính.");

            var duplicated = inputMaterials
                .GroupBy(x => x.material_id)
                .Any(g => g.Count() > 1);

            if (duplicated)
                throw new InvalidOperationException("Danh sách NVL bị trùng material_id.");

            foreach (var expected in expectedMaterials)
            {
                if (!expected.material_id.HasValue || expected.material_id.Value <= 0)
                    throw new InvalidOperationException($"NVL {expected.material_code} chưa có material_id hợp lệ.");

                var input = inputMaterials.FirstOrDefault(x => x.material_id == expected.material_id.Value);
                if (input == null)
                    throw new InvalidOperationException($"Thiếu dữ liệu NVL material_id={expected.material_id.Value}.");

                if (input.quantity_used < 0)
                    throw new InvalidOperationException(
                        $"quantity_used của {expected.material_code} không được nhỏ hơn 0.");

                if (input.quantity_left < 0)
                    throw new InvalidOperationException(
                        $"quantity_left của {expected.material_code} không được nhỏ hơn 0.");

                if (input.quantity_used + input.quantity_left > expected.estimated_input_qty)
                    throw new InvalidOperationException(
                        $"NVL {expected.material_code} vượt số lượng input ước tính. " +
                        $"Used + Left = {input.quantity_used + input.quantity_left}, " +
                        $"Estimated = {expected.estimated_input_qty}");

                if (input.quantity_left > 0 && !input.is_stock)
                    throw new InvalidOperationException(
                        $"NVL {expected.material_code} có dư thì is_stock phải = true.");
            }
        }

        public async Task<int> SuggestQtyGoodAsync(int taskId, CancellationToken ct = default)
        {
            var policy = await _taskRepo.GetQtyPolicyAsync(taskId, ct);
            return policy?.suggested_qty ?? 1;
        }

        public async Task<TaskQrMaterialBundleDto> GetTaskQrMaterialBundleAsync(int taskId, CancellationToken ct = default)
        {
            var ctx = await GetTaskEstimateContextAsync(taskId, ct);
            if (ctx == null)
                return new TaskQrMaterialBundleDto();

            return new TaskQrMaterialBundleDto
            {
                consumable_materials = await BuildConsumableMaterialsAsync(ctx, ct),
                reference_inputs = await BuildReferenceInputsAsync(ctx, ct)
            };
        }

        private static string RemoveDiacritics(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var ch in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string NormalizeMaterialCode(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            var s = RemoveDiacritics(raw)
                .Trim()
                .ToUpperInvariant();

            s = s.Replace("Đ", "D");
            s = Regex.Replace(s, @"[^A-Z0-9]+", "_");
            s = Regex.Replace(s, @"_+", "_").Trim('_');

            return s switch
            {
                // Kẽm
                "KEM_THO" => "PLATE",
                "BAN_KEM_THO" => "PLATE",
                "BAN_KEM" => "PLATE",
                "BAN_KEM_IN" => "PLATE",
                "PLATE_INPUT" => "PLATE",

                // Mực
                "MUC" => "INK",
                "MUC_IN" => "INK",
                "MUC_TONG_HOP" => "INK",
                "INK_TYPES" => "INK",

                // Keo phủ
                "KEO_NUOC" => "KEO_PHU_NUOC",
                "KEO_PHU_NUOC" => "KEO_PHU_NUOC",
                "KEO_DAU" => "KEO_PHU_DAU",
                "KEO_PHU_DAU" => "KEO_PHU_DAU",
                "UV" => "KEO_PHU_UV",
                "KEO_UV" => "KEO_PHU_UV",
                "PHU_UV" => "KEO_PHU_UV",
                "KEO_PHU_UV" => "KEO_PHU_UV",

                // Màng
                "CAN_MANG" => "MANG_12MIC",
                "MANG_CAN" => "MANG_12MIC",
                "MANG" => "MANG_12MIC",
                "LAMINATION" => "MANG_12MIC",
                "MANG_12_MIC" => "MANG_12MIC",

                // Keo bồi
                "MOUNTING_GLUE" => "KEO_BOI",
                "KEO_BOI" => "KEO_BOI",

                _ => s
            };
        }

        private async Task<List<TaskConsumableMaterialDto>> BuildConsumableMaterialsAsync(
    TaskEstimateContext ctx,
    CancellationToken ct = default)
        {
            var t = ctx.Task;
            var req = ctx.Request;
            var est = ctx.Estimate;

            var processCode = (t.process?.process_code ?? "").Trim().ToUpperInvariant();
            var result = new List<TaskConsumableMaterialDto>();

            async Task AddMaterialAsync(
    decimal estimatedQty,
    string fallbackCode,
    string fallbackName,
    string unit,
    material? resolvedMaterial)
            {
                if (estimatedQty <= 0)
                    return;

                var normalizedFallbackCode = NormalizeMaterialCode(
                    !string.IsNullOrWhiteSpace(fallbackCode) ? fallbackCode : fallbackName);

                result.Add(new TaskConsumableMaterialDto
                {
                    material_id = resolvedMaterial?.material_id,
                    material_code = resolvedMaterial?.code ?? normalizedFallbackCode,
                    material_name = resolvedMaterial?.name ?? fallbackName,
                    unit = resolvedMaterial?.unit ?? unit,
                    estimated_input_qty = Math.Round(estimatedQty, 4),
                    is_mapped = resolvedMaterial != null
                });
            }

            switch (processCode)
            {
                case "RALO":
                    {
                        var plateMat = await ResolveMaterialByCodesOrNamesAsync(
                            ct,
                            new[] { "PLATE", "PLATE_INPUT" },
                            new[] { "Kẽm thô", "Bản kẽm thô" });

                        await AddMaterialAsync(
                            estimatedQty: Math.Max(req.number_of_plates ?? 0, 1),
                            fallbackCode: "PLATE",
                            fallbackName: "Kẽm thô",
                            unit: "bản",
                            resolvedMaterial: plateMat);
                        break;
                    }

                case "CAT":
                    {
                        var paperMat = await ResolvePaperMaterialAsync(est, ct);

                        await AddMaterialAsync(
                            estimatedQty: est.sheets_total > 0 ? est.sheets_total : est.sheets_required,
                            fallbackCode: est.paper_code ?? "PAPER",
                            fallbackName: est.paper_name ?? "Giấy thô",
                            unit: "tờ",
                            resolvedMaterial: paperMat);
                        break;
                    }

                case "IN":
                    {
                        var inkMat = await ResolveMaterialByCodesOrNamesAsync(
                            ct,
                            new[] { "INK" },
                            new[] { "Mực tổng hợp" });

                        await AddMaterialAsync(
                            estimatedQty: est.ink_weight_kg,
                            fallbackCode: "INK",
                            fallbackName: "Mực tổng hợp",
                            unit: "kg",
                            resolvedMaterial: inkMat);
                        break;
                    }

                case "PHU":
                    {
                        var coatingMat = await ResolveCoatingMaterialAsync(est, ct);

                        await AddMaterialAsync(
                            estimatedQty: est.coating_glue_weight_kg,
                            fallbackCode: NormalizeMaterialCode(est.coating_type),
                            fallbackName: ProductionFlowHelper.ResolveCoatingDisplayName(est.coating_type),
                            unit: "kg",
                            resolvedMaterial: coatingMat);
                        break;
                    }

                case "CAN":
                case "CAN_MANG":
                    {
                        var filmMat = await ResolveMaterialByCodesOrNamesAsync(
                            ct,
                            new[] { "MANG_12MIC", "MANG_CAN", "LAMINATION" },
                            new[] { "Màng cán 12 mic", "Màng cán" });

                        await AddMaterialAsync(
                            estimatedQty: est.lamination_weight_kg,
                            fallbackCode: "MANG_12MIC",
                            fallbackName: "Màng cán 12 mic",
                            unit: "kg",
                            resolvedMaterial: filmMat);
                        break;
                    }

                case "BOI":
                    {
                        var waveMat = await ResolveWaveMaterialAsync(est, ct);
                        var resolvedWaveType = EstimateMaterialAlternativeHelper.ResolveWaveType(
                            est.wave_alternative,
                            est.wave_type);

                        await AddMaterialAsync(
                            estimatedQty: est.wave_sheets_used ?? est.wave_sheets_required ?? 0,
                            fallbackCode: waveMat?.code ?? resolvedWaveType ?? "WAVE",
                            fallbackName: waveMat?.name ?? (string.IsNullOrWhiteSpace(resolvedWaveType) ? "Sóng carton" : $"Sóng {resolvedWaveType}"),
                            unit: waveMat?.unit ?? "tờ",
                            resolvedMaterial: waveMat);

                        var glueMat = await ResolveMaterialByCodesOrNamesAsync(
                            ct,
                            new[] { "KEO_BOI", "MOUNTING_GLUE" },
                            new[] { "Keo bồi" });

                        await AddMaterialAsync(
                            estimatedQty: est.mounting_glue_weight_kg,
                            fallbackCode: "KEO_BOI",
                            fallbackName: "Keo bồi",
                            unit: "kg",
                            resolvedMaterial: glueMat);
                        break;
                    }

                // BE / DUT / DAN không phải NVL kho trực tiếp
                case "BE":
                case "DUT":
                case "DAN":
                default:
                    break;
            }

            return result;
        }

        private async Task<List<TaskReferenceInputDto>> BuildReferenceInputsAsync(
    TaskEstimateContext ctx,
    CancellationToken ct = default)
        {
            var t = ctx.Task;
            var currentCode = ProductionFlowHelper.Norm(t.process?.process_code);

            var previousProcessCode = await GetPreviousProcessCodeAsync(t, ct);
            if (string.IsNullOrWhiteSpace(previousProcessCode))
                return new List<TaskReferenceInputDto>();

            var (unit, qty) = ResolveReferenceInputShape(previousProcessCode, ctx);

            var result = new List<TaskReferenceInputDto>();

            switch (currentCode)
            {
                case "IN":
                case "BE":
                case "DUT":
                case "DAN":
                    result.Add(new TaskReferenceInputDto
                    {
                        input_code = previousProcessCode,
                        input_name = $"Bán thành phẩm từ công đoạn {previousProcessCode}",
                        unit = unit,
                        estimated_qty = Math.Round(qty, 4)
                    });
                    break;
            }

            return result;
        }

        private async Task<material?> ResolveCoatingMaterialAsync(cost_estimate est, CancellationToken ct)
        {
            var codes = new List<string>
    {
        NormalizeMaterialCode(est.coating_type)
    };

            return await ResolveMaterialByCodesOrNamesAsync(ct, codes);
        }

        private async Task<material?> ResolveWaveMaterialAsync(cost_estimate est, CancellationToken ct)
        {
            var resolvedWaveType = EstimateMaterialAlternativeHelper.ResolveWaveType(
                est.wave_alternative,
                est.wave_type);

            var codes = new List<string>();

            codes.AddRange(BuildWaveCodeCandidates(resolvedWaveType));
            codes.AddRange(BuildWaveCodeCandidates(est.wave_type));
            codes.AddRange(BuildWaveCodeCandidates(est.wave_alternative));

            return await ResolveMaterialByCodesOrNamesAsync(ct, codes);
        }

        private static List<string> BuildWaveCodeCandidates(string? waveType)
        {
            var raw = (waveType ?? "").Trim().ToUpperInvariant()
                .Replace(" ", "_");

            var result = new List<string>();

            switch (raw)
            {
                case "SONG_B_NAU":
                    result.Add("SONG_B_NAU");
                    break;

                case "B":
                case "SONG_B":
                case "B_NAU":
                    result.Add("SONG_B_NAU");
                    break;

                case "SONG_E_MOC":
                case "E_MOC":
                    result.Add("SONG_E_MOC");
                    break;

                case "SONG_E_NAU":
                case "E_NAU":
                    result.Add("SONG_E_NAU");
                    break;

                case "E":
                case "SONG_E":
                    // nếu estimate chỉ lưu E thì đây là mapping gần đúng
                    result.Add("SONG_E_NAU");
                    result.Add("SONG_E_MOC");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(raw))
                result.Add(raw);

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<material?> ResolvePaperMaterialAsync(cost_estimate est, CancellationToken ct)
        {
            var resolvedPaperCode = EstimateMaterialAlternativeHelper.ResolvePaperCode(
                est.paper_alternative,
                est.paper_code);

            return await ResolveMaterialByCodesOrNamesAsync(
                ct,
                new[]
                {
            resolvedPaperCode,
            est.paper_code,
            est.paper_alternative
                },
                new[]
                {
            est.paper_name,
            est.paper_alternative
                });
        }

        private async Task<material?> ResolveMaterialByCodesOrNamesAsync(
    CancellationToken ct,
    IEnumerable<string?> codeCandidates,
    IEnumerable<string?>? nameCandidates = null)
        {
            var aliases = new List<string>();

            aliases.AddRange(
                codeCandidates
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeMaterialCode));

            if (nameCandidates != null)
            {
                aliases.AddRange(
                    nameCandidates
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(NormalizeMaterialCode));
            }

            aliases = aliases
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (aliases.Count == 0)
                return null;

            var allMaterials = await _db.materials
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var alias in aliases)
            {
                var matched = allMaterials.FirstOrDefault(m =>
                    string.Equals(NormalizeMaterialCode(m.code), alias, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizeMaterialCode(m.name), alias, StringComparison.OrdinalIgnoreCase));

                if (matched != null)
                    return matched;
            }

            return null;
        }

        private async Task<material?> ResolveMaterialByCodesAsync(CancellationToken ct, params string?[] codes)
        {
            var normalized = codes
                .Select(NormalizeMaterialCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var code in normalized)
            {
                var mat = await _db.materials
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.code == code, ct);

                if (mat != null)
                    return mat;
            }

            return null;
        }

        private async Task<List<TaskConsumableMaterialDto>> GetConsumableMaterialsForTaskAsync(
    int taskId,
    CancellationToken ct = default)
        {
            var t = await _db.tasks
                .AsNoTracking()
                .Include(x => x.process)
                .FirstOrDefaultAsync(x => x.task_id == taskId, ct);

            if (t == null || !t.prod_id.HasValue)
                return new List<TaskConsumableMaterialDto>();

            var prod = await _db.productions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.prod_id == t.prod_id.Value, ct);

            if (prod?.order_id == null)
                return new List<TaskConsumableMaterialDto>();

            var req = await _db.order_requests
                .AsNoTracking()
                .Where(x => x.order_id == prod.order_id.Value)
                .OrderByDescending(x => x.order_request_id)
                .FirstOrDefaultAsync(ct);

            if (req == null)
                return new List<TaskConsumableMaterialDto>();

            cost_estimate? est = null;

            if (req.accepted_estimate_id.HasValue && req.accepted_estimate_id.Value > 0)
            {
                est = await _db.cost_estimates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.estimate_id == req.accepted_estimate_id.Value &&
                        x.order_request_id == req.order_request_id, ct);
            }

            est ??= await _db.cost_estimates
                .AsNoTracking()
                .Where(x => x.order_request_id == req.order_request_id)
                .OrderByDescending(x => x.is_active)
                .ThenByDescending(x => x.estimate_id)
                .FirstOrDefaultAsync(ct);

            if (est == null)
                return new List<TaskConsumableMaterialDto>();

            var processCode = (t.process?.process_code ?? "").Trim().ToUpperInvariant();
            var result = new List<TaskConsumableMaterialDto>();

            async Task AddMaterialAsync(
                decimal estimatedQty,
                string fallbackCode,
                string fallbackName,
                string unit,
                params string?[] lookupCodes)
            {
                if (estimatedQty <= 0)
                    return;

                var mat = await ResolveMaterialByCodesAsync(ct, lookupCodes);

                result.Add(new TaskConsumableMaterialDto
                {
                    material_id = mat?.material_id,
                    material_code = mat?.code ?? fallbackCode,
                    material_name = mat?.name ?? fallbackName,
                    unit = mat?.unit ?? unit,
                    estimated_input_qty = Math.Round(estimatedQty, 4),
                    is_mapped = mat != null
                });
            }

            var resolvedPaperCode = EstimateMaterialAlternativeHelper.ResolvePaperCode(
                est.paper_alternative,
                est.paper_code);

            var resolvedWaveType = EstimateMaterialAlternativeHelper.ResolveWaveType(
                est.wave_alternative,
                est.wave_type);

            switch (processCode)
            {
                case "RALO":
                    await AddMaterialAsync(
                        estimatedQty: Math.Max(req.number_of_plates ?? 0, 1),
                        fallbackCode: "PLATE",
                        fallbackName: "Bản kẽm",
                        unit: "bản",
                        "PLATE", "PLATE_INPUT");
                    break;

                case "CAT":
                    await AddMaterialAsync(
                        estimatedQty: est.sheets_total > 0 ? est.sheets_total : est.sheets_required,
                        fallbackCode: resolvedPaperCode ?? "PAPER",
                        fallbackName: est.paper_name ?? "Giấy in",
                        unit: "tờ",
                        resolvedPaperCode, est.paper_code, est.paper_alternative);
                    break;

                case "IN":
                    await AddMaterialAsync(
                        estimatedQty: est.ink_weight_kg,
                        fallbackCode: "INK",
                        fallbackName: "Mực in",
                        unit: "kg",
                        "INK");
                    break;

                case "PHU":
                    await AddMaterialAsync(
                        estimatedQty: est.coating_glue_weight_kg,
                        fallbackCode: NormalizeMaterialCode(est.coating_type),
                        fallbackName: ProductionFlowHelper.ResolveCoatingDisplayName(est.coating_type),
                        unit: "kg",
                        est.coating_type, NormalizeMaterialCode(est.coating_type));
                    break;

                case "CAN":
                case "CAN_MANG":
                    await AddMaterialAsync(
                        estimatedQty: est.lamination_weight_kg,
                        fallbackCode: "MANG_12MIC",
                        fallbackName: "Màng cán",
                        unit: "kg",
                        "MANG_12MIC", "MANG_CAN", "LAMINATION");
                    break;

                case "BOI":
                    await AddMaterialAsync(
                        estimatedQty: est.wave_sheets_used ?? est.wave_sheets_required ?? 0,
                        fallbackCode: resolvedWaveType ?? "WAVE",
                        fallbackName: string.IsNullOrWhiteSpace(resolvedWaveType) ? "Sóng carton" : $"Sóng {resolvedWaveType}",
                        unit: "tờ",
                        resolvedWaveType, est.wave_type, est.wave_alternative);

                    await AddMaterialAsync(
                        estimatedQty: est.mounting_glue_weight_kg,
                        fallbackCode: "KEO_BOI",
                        fallbackName: "Keo bồi",
                        unit: "kg",
                        "KEO_BOI", "MOUNTING_GLUE");
                    break;
            }

            return result;
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static List<TaskMaterialUsageLogItemDto> BuildMaterialUsageSnapshot(
            List<TaskConsumableMaterialDto> expectedMaterials,
            List<TaskMaterialUsageInputDto> inputMaterials)
        {
            var result = new List<TaskMaterialUsageLogItemDto>();

            foreach (var expected in expectedMaterials)
            {
                if (!expected.material_id.HasValue || expected.material_id.Value <= 0)
                    throw new InvalidOperationException(
                        $"NVL {expected.material_code} chưa map material_id.");

                var input = inputMaterials.FirstOrDefault(x => x.material_id == expected.material_id.Value);
                if (input == null)
                    throw new InvalidOperationException(
                        $"Thiếu dữ liệu NVL material_id={expected.material_id.Value}.");

                var waste = expected.estimated_input_qty - input.quantity_used - input.quantity_left;
                if (waste < 0) waste = 0;

                result.Add(new TaskMaterialUsageLogItemDto
                {
                    material_id = expected.material_id.Value,
                    material_code = expected.material_code,
                    material_name = expected.material_name,
                    unit = expected.unit,

                    estimated_input_qty = Math.Round(expected.estimated_input_qty, 4),
                    quantity_used = Math.Round(input.quantity_used, 4),
                    quantity_left = Math.Round(input.quantity_left, 4),
                    quantity_waste = Math.Round(waste, 4),

                    is_stock = input.is_stock
                });
            }

            return result;
        }

        private sealed class TaskEstimateContext
        {
            public task Task { get; init; } = null!;
            public production Production { get; init; } = null!;
            public order_request Request { get; init; } = null!;
            public cost_estimate Estimate { get; init; } = null!;
        }
        private async Task<TaskEstimateContext?> GetTaskEstimateContextAsync(int taskId, CancellationToken ct = default)
        {
            var t = await _db.tasks
                .AsNoTracking()
                .Include(x => x.process)
                .FirstOrDefaultAsync(x => x.task_id == taskId, ct);

            if (t == null || !t.prod_id.HasValue)
                return null;

            var prod = await _db.productions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.prod_id == t.prod_id.Value, ct);

            if (prod?.order_id == null)
                return null;

            var req = await _db.order_requests
                .AsNoTracking()
                .Where(x => x.order_id == prod.order_id.Value)
                .OrderByDescending(x => x.order_request_id)
                .FirstOrDefaultAsync(ct);

            if (req == null)
                return null;

            cost_estimate? est = null;

            if (req.accepted_estimate_id.HasValue && req.accepted_estimate_id.Value > 0)
            {
                est = await _db.cost_estimates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.estimate_id == req.accepted_estimate_id.Value &&
                        x.order_request_id == req.order_request_id, ct);
            }

            est ??= await _db.cost_estimates
                .AsNoTracking()
                .Where(x => x.order_request_id == req.order_request_id)
                .OrderByDescending(x => x.is_active)
                .ThenByDescending(x => x.estimate_id)
                .FirstOrDefaultAsync(ct);

            if (est == null)
                return null;

            return new TaskEstimateContext
            {
                Task = t,
                Production = prod,
                Request = req,
                Estimate = est
            };
        }

        private async Task<string?> GetPreviousProcessCodeAsync(task currentTask, CancellationToken ct = default)
        {
            if (!currentTask.prod_id.HasValue)
                return null;

            var flow = await _db.tasks
                .AsNoTracking()
                .Include(x => x.process)
                .Where(x => x.prod_id == currentTask.prod_id.Value)
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .Select(x => new TaskFlowRefItem
                {
                    task_id = x.task_id,
                    seq_num = x.seq_num,
                    process_code = x.process != null ? x.process.process_code : null,
                    process_name = x.process != null ? x.process.process_name : null
                })
                .ToListAsync(ct);

            if (flow.Count == 0)
                return null;

            var currentIndex = flow.FindIndex(x => x.task_id == currentTask.task_id);
            if (currentIndex <= 0)
                return null;

            var prev = flow[currentIndex - 1];
            return ProductionFlowHelper.Norm(prev.process_code);
        }

        private sealed class TaskFlowRefItem
        {
            public int task_id { get; init; }
            public int? seq_num { get; init; }
            public string? process_code { get; init; }
            public string? process_name { get; init; }
        }

        private static (string unit, decimal qty) ResolveReferenceInputShape(
    string? previousProcessCode,
    TaskEstimateContext ctx)
        {
            var prevCode = ProductionFlowHelper.Norm(previousProcessCode);

            var req = ctx.Request;
            var est = ctx.Estimate;

            decimal sheetQty = est.sheets_total > 0
                ? est.sheets_total
                : est.sheets_required > 0 ? est.sheets_required : 1;

            decimal productQty = req.quantity.HasValue && req.quantity.Value > 0
                ? req.quantity.Value
                : 1;

            decimal plateQty = req.number_of_plates.HasValue && req.number_of_plates.Value > 0
                ? req.number_of_plates.Value
                : 1;

            return prevCode switch
            {
                "RALO" => ("bản", plateQty),
                "DUT" => ("sp", productQty),
                "DAN" => ("sp", productQty),
                _ => ("tờ", sheetQty)
            };
        }

        public async Task<List<TaskConsumableMaterialDto>> GetConsumableMaterialsForTaskPublicAsync(int taskId, CancellationToken ct = default)
        {
            return await GetConsumableMaterialsForTaskAsync(taskId, ct);
        }
    }
}