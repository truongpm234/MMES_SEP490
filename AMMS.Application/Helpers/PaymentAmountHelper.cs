using AMMS.Infrastructure.Entities;
using Microsoft.Extensions.Configuration;

namespace AMMS.Application.Helpers
{
    public static class PaymentAmountHelper
    {
        public static int GetDepositAmount(cost_estimate est)
        {
            if (est == null) throw new ArgumentNullException(nameof(est));

            var finalTotal = NormalizeMoneyToInt(est.final_total_cost);
            var depositActual = NormalizeMoneyToInt(est.deposit_amount);

            if (depositActual < 0)
                depositActual = 0;

            if (finalTotal > 0 && depositActual > finalTotal)
                depositActual = finalTotal;

            return depositActual;
        }

        public static int GetRemainingAmount(cost_estimate est)
        {
            if (est == null) throw new ArgumentNullException(nameof(est));

            var finalTotal = NormalizeMoneyToInt(est.final_total_cost);
            var depositActual = GetDepositAmount(est);

            var remaining = finalTotal - depositActual;
            return remaining > 0 ? remaining : 0;
        }

        public static decimal GetDepositDisplayAmount(cost_estimate est)
        {
            return GetDepositAmount(est);
        }

        public static decimal GetRemainingDisplayAmount(cost_estimate est)
        {
            return GetRemainingAmount(est);
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

        private static int NormalizeMoneyToInt(object? value)
        {
            if (value == null)
                return 0;

            var amount = Convert.ToDecimal(value);
            if (amount <= 0m)
                return 0;

            return Convert.ToInt32(Math.Round(amount, 0, MidpointRounding.AwayFromZero));
        }
    }
}