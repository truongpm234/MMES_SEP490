using AMMS.Application.Interfaces;
using AMMS.Infrastructure.DBContext;
using AMMS.Shared.Helpers;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AMMS.API.Jobs
{
    public sealed class AutoStartProductionJob
    {
        private readonly AppDbContext _db;
        private readonly IProductionService _productionService;
        private readonly ILogger<AutoStartProductionJob> _logger;

        public AutoStartProductionJob(
            AppDbContext db,
            IProductionService productionService,
            ILogger<AutoStartProductionJob> logger)
        {
            _db = db;
            _productionService = productionService;
            _logger = logger;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
        public async Task RunAsync(CancellationToken ct)
        {
            var now = AppTime.NowVnUnspecified();

            /*
             * Sửa quan trọng:
             * Trước đây job lấy theo order_id và gọi StartProductionAndPromoteFirstTaskAsync(orderId).
             * Bây giờ phải lấy theo prod_id và gọi StartProductionAndPromoteFirstTaskByProdIdAsync(prodId).
             *
             * Lý do:
             * Một order có thể có nhiều production:
             * - SINGLE
             * - GROUP
             * - SPLIT
             *
             * Nếu start bằng order_id thì SINGLE/SPLIT dễ bị start nhầm cùng lúc.
             */
            var dueProductions = await (
                from p in _db.productions.AsNoTracking()

                join o0 in _db.orders.AsNoTracking()
                    on p.order_id equals (int?)o0.order_id into oj
                from o in oj.DefaultIfEmpty()

                where p.end_date == null
                      && p.actual_start_date == null
                      && p.planned_start_date != null
                      && p.planned_start_date <= now
                      && p.status == "Scheduled"

                      /*
                       * SINGLE/SPLIT có order_id:
                       * phải check order đã confirmed layout và ready.
                       *
                       * GROUP không có order_id:
                       * không join được order trực tiếp.
                       * Service sẽ tự check member orders trong prod_orders.
                       */
                      && (
                            (
                                p.order_id != null
                                && o != null
                                && o.layout_confirmed == true
                                && o.is_production_ready == true
                            )
                            ||
                            (
                                p.order_id == null
                                && p.prod_kind == "GROUP"
                            )
                         )

                orderby p.planned_start_date, p.prod_id

                select new DueProductionRow
                {
                    ProdId = p.prod_id,
                    OrderId = p.order_id,
                    ProdKind = p.prod_kind,
                    ProductionCode = p.code,
                    PlannedStart = p.planned_start_date,
                    ProductionStatus = p.status,
                    OrderStatus = o != null ? o.status : null,
                    LayoutConfirmed = o != null ? o.layout_confirmed : null,
                    IsProductionReady = o != null ? o.is_production_ready : null
                }
            ).ToListAsync(ct);

            if (dueProductions.Count == 0)
            {
                _logger.LogInformation(
                    "[AutoStartProductionJob] No due scheduled productions at {Now}",
                    now);

                return;
            }

            _logger.LogInformation(
                "[AutoStartProductionJob] Found {Count} due productions to auto start at {Now}",
                dueProductions.Count,
                now);

            var successCount = 0;
            var skippedCount = 0;
            var failCount = 0;

            foreach (var item in dueProductions)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    /*
                     * Sửa quan trọng:
                     * Gọi start bằng ProdId, không gọi bằng OrderId nữa.
                     */
                    var startedProdId =
                        await _productionService.StartProductionAndPromoteFirstTaskByProdIdAsync(
                            item.ProdId,
                            ct);

                    if (startedProdId.HasValue)
                    {
                        successCount++;

                        _logger.LogInformation(
                            "[AutoStartProductionJob] Auto started production successfully. ProdId={ProdId}, OrderId={OrderId}, ProdKind={ProdKind}, ProductionCode={ProductionCode}, PlannedStart={PlannedStart}",
                            startedProdId.Value,
                            item.OrderId,
                            item.ProdKind,
                            item.ProductionCode,
                            item.PlannedStart);
                    }
                    else
                    {
                        skippedCount++;

                        _logger.LogWarning(
                            "[AutoStartProductionJob] StartProductionAndPromoteFirstTaskByProdIdAsync returned null. ProdId={ProdId}, OrderId={OrderId}, ProdKind={ProdKind}, PlannedStart={PlannedStart}",
                            item.ProdId,
                            item.OrderId,
                            item.ProdKind,
                            item.PlannedStart);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    /*
                     * GROUP/SPLIT có thể tới giờ scheduled nhưng công đoạn trước chưa xong.
                     * Trường hợp này không nên làm crash job, chỉ skip để lần sau chạy lại.
                     */
                    skippedCount++;

                    _logger.LogWarning(
                        ex,
                        "[AutoStartProductionJob] Auto start skipped. ProdId={ProdId}, OrderId={OrderId}, ProdKind={ProdKind}, PlannedStart={PlannedStart}, ProductionStatus={ProductionStatus}, OrderStatus={OrderStatus}. Reason={Reason}",
                        item.ProdId,
                        item.OrderId,
                        item.ProdKind,
                        item.PlannedStart,
                        item.ProductionStatus,
                        item.OrderStatus,
                        ex.Message);
                }
                catch (Exception ex)
                {
                    failCount++;

                    _logger.LogError(
                        ex,
                        "[AutoStartProductionJob] Auto start failed. ProdId={ProdId}, OrderId={OrderId}, ProdKind={ProdKind}, PlannedStart={PlannedStart}, ProductionStatus={ProductionStatus}, OrderStatus={OrderStatus}",
                        item.ProdId,
                        item.OrderId,
                        item.ProdKind,
                        item.PlannedStart,
                        item.ProductionStatus,
                        item.OrderStatus);
                }
            }

            _logger.LogInformation(
                "[AutoStartProductionJob] Done. Success={SuccessCount}, Skipped={SkippedCount}, Failed={FailCount}",
                successCount,
                skippedCount,
                failCount);
        }

        private sealed class DueProductionRow
        {
            public int ProdId { get; set; }

            public int? OrderId { get; set; }

            public string? ProdKind { get; set; }

            public string? ProductionCode { get; set; }

            public DateTime? PlannedStart { get; set; }

            public string? ProductionStatus { get; set; }

            public string? OrderStatus { get; set; }

            public bool? LayoutConfirmed { get; set; }

            public bool? IsProductionReady { get; set; }
        }
    }
}