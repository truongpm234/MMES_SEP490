using AMMS.Infrastructure.Entities;
using Microsoft.Extensions.Configuration;

namespace AMMS.Application.Helpers
{
    public static class PaymentAmountHelper
    {
        public static int GetDepositAmount(cost_estimate est)
        {
            if (est == null) throw new ArgumentNullException(nameof(est));

            return (int)Math.Round(
                est.deposit_amount,
                0,
                MidpointRounding.AwayFromZero)/100;
        }

        public static int GetRemainingAmount(cost_estimate est)
        {
            if (est == null) throw new ArgumentNullException(nameof(est));

            var finalTotal = (int)Math.Round(
                est.final_total_cost,
                0,
                MidpointRounding.AwayFromZero);

            var deposit = GetDepositAmount(est);

            var remaining = (finalTotal - deposit)/100;
            return remaining > 0 ? remaining : 0;
        }

        public static int ToGatewayAmount(int actualAmount, IConfiguration config)
        {
            if (actualAmount <= 0) return 1;

            var useDivider =
                bool.TryParse(config["PayOS:UseTestAmountDivider"], out var enabled) && enabled;

            if (!useDivider)
                return actualAmount;

            var divider =
                int.TryParse(config["PayOS:TestAmountDivider"], out var d) && d > 1
                    ? d
                    : 100;

            return Math.Max(1, actualAmount / divider);
        }
    }
}