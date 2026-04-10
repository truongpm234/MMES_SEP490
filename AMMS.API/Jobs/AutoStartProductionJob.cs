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


            var dueOrders = await (
                from p in _db.productions.AsNoTracking()
                join o in _db.orders.AsNoTracking() on p.order_id equals o.order_id
                where p.order_id != null
                      && p.end_date == null
                      && p.actual_start_date == null
                      && p.planned_start_date != null
                      && p.planned_start_date <= now
                      && p.status == "Scheduled"
                      && o.layout_confirmed == true
                      && o.is_production_ready == true
                orderby p.planned_start_date, p.prod_id
                select new
                {
                    OrderId = p.order_id!.Value,
                    ProdId = p.prod_id,
                    PlannedStart = p.planned_start_date,
                    ProductionStatus = p.status,
                    OrderStatus = o.status
                }
            )
            .Distinct()
            .ToListAsync(ct);

            if (dueOrders.Count == 0)
            {
                _logger.LogInformation(
                    "[AutoStartProductionJob] No due scheduled productions at {Now}",
                    now);
                return;
            }

            _logger.LogInformation(
                "[AutoStartProductionJob] Found {Count} due productions to auto start at {Now}",
                dueOrders.Count,
                now);

            var successCount = 0;
            var failCount = 0;

            foreach (var item in dueOrders)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    var prodId = await _productionService.StartProductionAndPromoteFirstTaskAsync(item.OrderId, ct);

                    if (prodId.HasValue)
                    {
                        successCount++;

                        _logger.LogInformation(
                            "[AutoStartProductionJob] Auto started production successfully. OrderId={OrderId}, ProdId={ProdId}, PlannedStart={PlannedStart}",
                            item.OrderId,
                            prodId.Value,
                            item.PlannedStart);
                    }
                    else
                    {
                        failCount++;

                        _logger.LogWarning(
                            "[AutoStartProductionJob] StartProductionAndPromoteFirstTaskAsync returned null. OrderId={OrderId}, PlannedStart={PlannedStart}",
                            item.OrderId,
                            item.PlannedStart);
                    }
                }
                catch (Exception ex)
                {
                    failCount++;

                    _logger.LogError(
                        ex,
                        "[AutoStartProductionJob] Auto start failed. OrderId={OrderId}, ProdId={ProdId}, PlannedStart={PlannedStart}, ProductionStatus={ProductionStatus}, OrderStatus={OrderStatus}",
                        item.OrderId,
                        item.ProdId,
                        item.PlannedStart,
                        item.ProductionStatus,
                        item.OrderStatus);
                }
            }

            _logger.LogInformation(
                "[AutoStartProductionJob] Done. Success={SuccessCount}, Failed={FailCount}",
                successCount,
                failCount);
        }
    }
}