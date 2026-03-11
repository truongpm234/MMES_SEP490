using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Planning;

namespace AMMS.Application.Services
{
    public class OrderPlanningService : IOrderPlanningService
    {
        private readonly IProductionSchedulingService _scheduling;

        public OrderPlanningService(IProductionSchedulingService scheduling)
        {
            _scheduling = scheduling;
        }

        public async Task<EstimateFinishDateResponse?> EstimateFinishByOrderRequestAsync(int orderRequestId, CancellationToken ct = default)
        {
            var preview = await _scheduling.PreviewByOrderRequestAsync(orderRequestId, ct);
            if (preview == null) return null;

            var desired = preview.desired_delivery_date;
            var estimatedFinish = preview.estimated_finish_date;

            var canMeet = desired.HasValue
                ? estimatedFinish.Date <= desired.Value.Date
                : true;

            var late = 0;
            var early = 0;

            if (desired.HasValue)
            {
                var diff = (estimatedFinish.Date - desired.Value.Date).Days;
                if (diff > 0) late = diff;
                if (diff < 0) early = -diff;
            }

            return new EstimateFinishDateResponse
            {
                order_request_id = orderRequestId,
                desired_delivery_date = desired,
                estimated_finish_date = estimatedFinish,
                can_meet_desired_date = canMeet,
                days_late_if_any = late,
                days_early_if_any = early
            };
        }
    }
}