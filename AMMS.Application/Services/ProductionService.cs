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

        public ProductionService(
            IHubContext<RealtimeHub> rt,
            IProductionRepository repo,
            IRealtimePublisher hub,
            AppDbContext db,
            IOrderRepository orderRepository,
            NotificationService notiService)
        {
            _db = db;
            _rt = rt;
            _repo = repo;
            _hub = hub;
            _orderRepo = orderRepository;
            _notiService = notiService;
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

        // Giữ tên method cũ để controller hiện tại không phải đổi
        // nhưng bên trong vẫn dùng flow mới: KHÔNG promote first task
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
            var ord = await _db.orders
                .AsTracking()
                .FirstOrDefaultAsync(x => x.order_id == orderId, ct);

            if (ord == null)
                return null;

            var hasEnoughMaterial = await _orderRepo.IsOrderEnoughByOrderIdAsync(orderId);
            var hasFreeMachine = await HasFreeMachineForFirstReleaseAsync(orderId, ct);

            if (ord.is_enough != hasEnoughMaterial)
            {
                ord.is_enough = hasEnoughMaterial;
                await _db.SaveChangesAsync(ct);
            }

            return new ProductionReadyCheckResponse
            {
                order_id = orderId,
                is_production_ready = ord.is_production_ready,
                has_enough_material = hasEnoughMaterial,
                has_free_machine = hasFreeMachine
            };
        }

        public async Task<bool> SetProductionReadyAsync(
            int orderId,
            bool isProductionReady,
            CancellationToken ct = default)
        {
            var ord = await _db.orders
                .FirstOrDefaultAsync(x => x.order_id == orderId, ct);

            if (ord == null)
                return false;

            if (isProductionReady)
            {
                var hasEnoughMaterial = await _orderRepo.IsOrderEnoughByOrderIdAsync(orderId);
                var hasFreeMachine = await HasFreeMachineForFirstReleaseAsync(orderId, ct);

                if (!hasEnoughMaterial || !hasFreeMachine)
                    throw new InvalidOperationException(
                        "Order chưa đủ điều kiện sản xuất: thiếu nguyên vật liệu hoặc chưa có máy rảnh.");

                ord.is_enough = hasEnoughMaterial;
            }

            ord.is_production_ready = isProductionReady;
            await _db.SaveChangesAsync(ct);

            await _rt.Clients
                .Group(RealtimeGroups.ByRole("production manager"))
                .SendAsync("scheduled", new
                {
                    message = $"Đơn hàng {orderId} đã được lên lịch sản xuất có thể bắt đầu sản xuất"
                });

            var req = await _db.order_requests.FirstOrDefaultAsync(o => o.order_id == orderId, ct);
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

        private async Task<bool> HasFreeMachineForFirstReleaseAsync(
            int orderId,
            CancellationToken ct = default)
        {
            var prod = await _db.productions
                .AsNoTracking()
                .Where(x => x.order_id == orderId)
                .OrderByDescending(x => x.prod_id)
                .FirstOrDefaultAsync(ct);

            if (prod == null)
                return false;

            var tasks = await _db.tasks
                .Include(x => x.process)
                .AsNoTracking()
                .Where(x => x.prod_id == prod.prod_id)
                .OrderBy(x => x.seq_num)
                .ThenBy(x => x.task_id)
                .ToListAsync(ct);

            if (tasks.Count == 0)
                return false;

            var hasInitialParallel = tasks.Any(x =>
                ProductionFlowHelper.IsInitialParallel(x.process?.process_code));

            var candidateMachineCodes = (hasInitialParallel
                    ? tasks.Where(x => ProductionFlowHelper.IsInitialParallel(x.process?.process_code))
                    : tasks.Take(1))
                .Select(x => x.machine)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (candidateMachineCodes.Count == 0)
                return false;

            return await _db.machines
                .AsNoTracking()
                .AnyAsync(m =>
                    m.is_active &&
                    candidateMachineCodes.Contains(m.machine_code) &&
                    ((m.free_quantity ?? (m.quantity - (m.busy_quantity ?? 0))) > 0),
                    ct);
        }
    }
}