using AMMS.Infrastructure.Entities;
using Microsoft.Extensions.Configuration;

namespace AMMS.Application.Helpers
{
    public static class PaymentAmountHelper
    {
        public static int GetDepositAmount(cost_estimate est)
        {
            if (est == null) throw new ArgumentNullException(nameof(est));

            return NormalizeMoneyToInt(est.deposit_amount);
        }

        public static int GetRemainingAmount(cost_estimate est)
        {
            if (est == null) throw new ArgumentNullException(nameof(est));

            var finalTotal = NormalizeMoneyToInt(est.final_total_cost);
            var deposit = NormalizeMoneyToInt(est.deposit_amount);

            var remaining = finalTotal - deposit;
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