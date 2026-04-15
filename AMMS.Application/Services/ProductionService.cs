using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Common;
using AMMS.Shared.DTOs.Enums;
using AMMS.Shared.DTOs.Productions;
using AMMS.Shared.DTOs.Socket;
using AMMS.Shared.Helpers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Application.Services
{
    public class ProductionService : IProductionService
    {
        private readonly IProductionRepository _repo;
        private readonly IRealtimePublisher _hub;
        private readonly IOrderRepository _orderRepo;
        private readonly AppDbContext _db;
        private readonly IHubContext<RealtimeHub> _rt;
        private readonly NotificationService _notiService;
        private readonly IRequestRepository _requestRepo;

        public ProductionService(
            IHubContext<RealtimeHub> rt,
            IProductionRepository repo,
            IRealtimePublisher hub,
            AppDbContext db,
            IOrderRepository orderRepository,
            IRequestRepository requestRepository,
            NotificationService notiService)
        {
            _db = db;
            _rt = rt;
            _repo = repo;
            _hub = hub;
            _orderRepo = orderRepository;
            _notiService = notiService;
            _requestRepo = requestRepository;
        }

        public async Task<NearestDeliveryResponse> GetNearestDeliveryAsync()
        {
            var nearestDate = await _repo.GetNearestDeliveryDateAsync();

            var days = nearestDate == null
                ? 0
                : Math.Max(1, (nearestDate.Value.Date - DateTime.UtcNow.Date).Days);

            return new NearestDeliveryResponse
            {
                nearest_delivery_date = nearestDate,
                days_until_free = days
            };
        }

        public Task<List<string>> GetAllProcessTypeAsync()
        {
            var result = Enum.GetNames(typeof(ProcessType)).ToList();
            return Task.FromResult(result);
        }

        public Task<PagedResultLite<ProducingOrderCardDto>> GetProducingOrdersAsync(
            int page,
            int pageSize,
            int? roleId,
            CancellationToken ct = default)
            => _repo.GetProducingOrdersAsync(page, pageSize, roleId, ct);

        public async Task<ProductionProgressResponse> GetProgressAsync(int prodId)
        {
            return await _repo.GetProgressAsync(prodId);
        }

        public async Task<ProductionDetailDto?> GetProductionDetailByOrderIdAsync(int orderId, CancellationToken ct = default)
        {
            return await _repo.GetProductionDetailByOrderIdAsync(orderId, ct);
        }

        public async Task<ProductionWasteReportDto?> GetProductionWasteAsync(int prodId, CancellationToken ct = default)
        {
            return await _repo.GetProductionWasteAsync(prodId, ct);
        }

        public async Task<bool> StartProductionByOrderIdAsync(int orderId, CancellationToken ct = default)
        {
            var ord = await _db.orders
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.order_id == orderId, ct);

            if (ord == null)
                throw new InvalidOperationException("Order not found");

            if (!ord.is_production_ready)
                throw new InvalidOperationException("General manager has not confirmed production readiness.");

            var now = AppTime.NowVnUnspecified();
            var prodId = await _repo.StartProductionByOrderIdOnlyAsync(orderId, now, ct);
            return prodId.HasValue;
        }

        public async Task<bool> SetProductionDeliveryAsync(int orderId, CancellationToken ct = default)
        {
            return await _repo.SetProductionDeliveryByOrderIdAsync(orderId, ct);
        }

        public async Task<bool> SetCompletedAsync(int orderId, CancellationToken ct = default)
        {
            return await _repo.SetCompletedByOrderIdAsync(orderId, ct);
        }

        public async Task<int?> StartProductionAndPromoteFirstTaskAsync(int orderId, CancellationToken ct = default)
        {
            var ord = await _db.orders
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.order_id == orderId, ct);

            if (ord == null)
                throw new InvalidOperationException("Order not found");

            if (!ord.is_production_ready)
                throw new InvalidOperationException("General manager has not confirmed production readiness.");

            return await _repo.StartProductionByOrderIdOnlyAsync(
                orderId,
                AppTime.NowVnUnspecified(),
                ct);
        }

        public async Task<List<MachineScheduleBoardDto>> GetMachineScheduleBoardAsync(
            DateTime from,
            DateTime to,
            CancellationToken ct = default)
        {
            return await _repo.GetMachineScheduleBoardAsync(from, to, ct);
        }

        public async Task<ProductionReadyCheckResponse?> GetProductionReadyAsync(
    int orderId,
    CancellationToken ct = default)
        {
            var ord = await _orderRepo.GetByIdAsync(orderId);

            if (ord == null)
                return null;

            var materials = await GetMaterialReadinessAsync(orderId, ct);
            var machines = await GetMachineReadinessAsync(orderId, ct);

            var hasEnoughMaterial = materials.Count > 0 && materials.All(x => x.is_enough);
            var hasFreeMachine = machines.Count > 0 && machines.Any(x => x.is_available);

            return new ProductionReadyCheckResponse
            {
                order_id = orderId,
                is_production_ready = ord.is_production_ready,
                has_enough_material = hasEnoughMaterial,
                has_free_machine = hasFreeMachine,
                materials = materials,
                machines = machines
            };
        }

        public async Task<bool> SetProductionReadyAsync(int orderId, bool isProductionReady, CancellationToken ct = default)
        {
            var ord = await _orderRepo.GetByIdForUpdateAsync(orderId, ct);

            if (ord == null)
                return false;

            if (isProductionReady)
            {
                var materials = await GetMaterialReadinessAsync(orderId, ct);
                var machines = await GetMachineReadinessAsync(orderId, ct);

                var hasEnoughMaterial = materials.Count > 0 && materials.All(x => x.is_enough);
                var hasFreeMachine = machines.Count > 0 && machines.Any(x => x.is_available);

                if (!hasEnoughMaterial || !hasFreeMachine)
                    throw new InvalidOperationException(
                        "Order chưa đủ điều kiện sản xuất: thiếu nguyên vật liệu hoặc chưa có máy rảnh.");

                ord.is_enough = hasEnoughMaterial;
            }

            ord.is_production_ready = isProductionReady;
            await _orderRepo.SaveChangesAsync();

            await _rt.Clients
                .Group(RealtimeGroups.ByRole("production manager"))
                .SendAsync("scheduled", new
                {
                    message = $"Đơn hàng {orderId} đã được lên lịch sản xuất có thể bắt đầu sản xuất"
                });

            var req = await _requestRepo.GetByOderIdAsync(orderId);
            if (req != null)
            {
                await _notiService.CreateNotfi(
                    6,
                    $"Đơn hàng {orderId} đã được lên lịch sản xuất có thể bắt đầu sản xuất",
                    null,
                    req.order_request_id, "Scheduled");
            }

            return true;
        }

        private async Task<List<ProductionReadyMaterialDto>> GetMaterialReadinessAsync(
    int orderId,
    CancellationToken ct = default)
        {
            var rows = await (
                from oi in _db.order_items.AsNoTracking()
                join b in _db.boms.AsNoTracking() on oi.item_id equals b.order_item_id
                join m in _db.materials.AsNoTracking() on b.material_id equals m.material_id into mj
                from m in mj.DefaultIfEmpty()
                where oi.order_id == orderId
                select new
                {
                    order_qty = oi.quantity,

                    b.material_id,
                    b.material_code,
                    b.material_name,
                    b.unit,
                    b.qty_total,
                    b.qty_per_product,
                    b.wastage_percent,

                    material_exists = m != null,
                    db_material_code = m != null ? m.code : null,
                    db_material_name = m != null ? m.name : null,
                    db_unit = m != null ? m.unit : null,
                    stock_qty = m != null ? (m.stock_qty ?? 0m) : 0m
                }
            ).ToListAsync(ct);

            if (rows.Count == 0)
                return new List<ProductionReadyMaterialDto>();

            var result = rows
                .GroupBy(x => new
                {
                    x.material_id,
                    MaterialCode = !string.IsNullOrWhiteSpace(x.db_material_code) ? x.db_material_code : x.material_code,
                    MaterialName = !string.IsNullOrWhiteSpace(x.db_material_name) ? x.db_material_name : x.material_name,
                    Unit = !string.IsNullOrWhiteSpace(x.db_unit) ? x.db_unit : x.unit,
                    x.material_exists
                })
                .Select(g =>
                {
                    decimal requiredQty = 0m;

                    foreach (var line in g)
                    {
                        decimal lineQty;

                        if (line.qty_total.HasValue && line.qty_total.Value > 0m)
                        {
                            lineQty = line.qty_total.Value;
                        }
                        else
                        {
                            var orderQty = line.order_qty <= 0 ? 1 : line.order_qty;
                            var qtyPerProduct = line.qty_per_product ?? 0m;
                            var wasteFactor = 1m + ((line.wastage_percent ?? 0m) / 100m);

                            lineQty = orderQty * qtyPerProduct * wasteFactor;
                        }

                        if (lineQty < 0m) lineQty = 0m;
                        requiredQty += lineQty;
                    }

                    requiredQty = Math.Round(requiredQty, 4);

                    var mapped = g.Key.material_id.HasValue
                                 && g.Key.material_id.Value > 0
                                 && g.Key.material_exists;

                    var availableQty = mapped
                        ? Math.Round(g.Max(x => x.stock_qty), 4)
                        : 0m;

                    var missingQty = requiredQty - availableQty;
                    if (missingQty < 0m) missingQty = 0m;

                    var isEnough = mapped && missingQty <= 0m;

                    var status = !mapped
                        ? "Unmapped"
                        : isEnough ? "Enough" : "Missing";

                    return new ProductionReadyMaterialDto
                    {
                        material_id = g.Key.material_id,
                        material_code = g.Key.MaterialCode,
                        material_name = g.Key.MaterialName,
                        unit = g.Key.Unit,

                        required_qty = requiredQty,
                        available_qty = availableQty,
                        missing_qty = missingQty,

                        is_enough = isEnough,
                        status = status
                    };
                })
                .OrderBy(x => x.status == "Missing" ? 0 : x.status == "Unmapped" ? 1 : 2)
                .ThenBy(x => x.material_name)
                .ToList();

            return result;
        }

        private async Task<List<ProductionReadyMachineDto>> GetMachineReadinessAsync(
    int orderId,
    CancellationToken ct = default)
        {
            var prod = await _db.productions
                .AsNoTracking()
                .Where(x => x.order_id == orderId)
                .OrderByDescending(x => x.prod_id)
                .FirstOrDefaultAsync(ct);

            if (prod == null)
                return new List<ProductionReadyMachineDto>();

            var tasks = await _db.tasks
                .Include(x => x.process)
                .AsNoTracking()
                .Where(x => x.prod_id == prod.prod_id)
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .ToListAsync(ct);

            if (tasks.Count == 0)
                return new List<ProductionReadyMachineDto>();

            var hasInitialParallel = tasks.Any(x =>
                ProductionFlowHelper.IsInitialParallel(x.process?.process_code));

            var targetTasks = (hasInitialParallel
                    ? tasks.Where(x => ProductionFlowHelper.IsInitialParallel(x.process?.process_code))
                    : tasks.Take(1))
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .ToList();

            if (targetTasks.Count == 0)
                return new List<ProductionReadyMachineDto>();

            var machineCodes = targetTasks
                .Where(x => !string.IsNullOrWhiteSpace(x.machine))
                .Select(x => x.machine!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var machineMap = await _db.machines
                .AsNoTracking()
                .Where(x => machineCodes.Contains(x.machine_code))
                .ToDictionaryAsync(x => x.machine_code, StringComparer.OrdinalIgnoreCase, ct);

            var result = new List<ProductionReadyMachineDto>();

            foreach (var t in targetTasks)
            {
                var machineCode = t.machine?.Trim();
                var processCode = t.process?.process_code;
                var processName = t.process?.process_name ?? t.name;

                if (string.IsNullOrWhiteSpace(machineCode))
                {
                    result.Add(new ProductionReadyMachineDto
                    {
                        process_id = t.process_id,
                        seq_num = t.seq_num,
                        process_code = processCode,
                        process_name = processName,
                        machine_code = null,
                        machine_found = false,
                        is_available = false,
                        total_quantity = 0,
                        busy_quantity = 0,
                        free_quantity = 0,
                        status = "Unmapped"
                    });

                    continue;
                }

                if (!machineMap.TryGetValue(machineCode, out var machine) || !machine.is_active)
                {
                    result.Add(new ProductionReadyMachineDto
                    {
                        process_id = t.process_id,
                        seq_num = t.seq_num,
                        process_code = processCode,
                        process_name = processName,
                        machine_code = machineCode,
                        machine_found = false,
                        is_available = false,
                        total_quantity = 0,
                        busy_quantity = 0,
                        free_quantity = 0,
                        status = "Unmapped"
                    });

                    continue;
                }

                var totalQty = machine.quantity;
                var busyQty = machine.busy_quantity ?? 0;
                var freeQty = machine.free_quantity ?? (totalQty - busyQty);
                if (freeQty < 0) freeQty = 0;

                var isAvailable = freeQty > 0;

                result.Add(new ProductionReadyMachineDto
                {
                    process_id = t.process_id,
                    seq_num = t.seq_num,
                    process_code = processCode,
                    process_name = processName,
                    machine_code = machineCode,
                    machine_found = true,
                    is_available = isAvailable,
                    total_quantity = totalQty,
                    busy_quantity = busyQty,
                    free_quantity = freeQty,
                    status = isAvailable ? "Available" : "Busy"
                });
            }

            return result;
        }
    }
}