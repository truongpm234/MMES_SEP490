using AMMS.Infrastructure.Entities;
using AMMS.Shared.DTOs.Estimates;
using Microsoft.Extensions.Configuration;

namespace AMMS.Application.Helpers
{
    public static class PaymentAmountHelper
    {
        public static int GetDepositAmount(cost_estimate est, PaymentTermsConfig paymentTerms)
        {
            if (est == null) throw new ArgumentNullException(nameof(est));
            if (paymentTerms == null) throw new ArgumentNullException(nameof(paymentTerms));

            var finalTotal = NormalizeMoneyToInt(est.final_total_cost);
            var depositPercent = NormalizePercent(paymentTerms.deposit_percent);

            var depositActual = Convert.ToInt32(Math.Round(
                finalTotal * depositPercent / 100m,
                0,
                MidpointRounding.AwayFromZero));

            return depositActual;
        }

        public static int GetRemainingAmount(cost_estimate est, PaymentTermsConfig paymentTerms)
        {
            if (est == null) throw new ArgumentNullException(nameof(est));
            if (paymentTerms == null) throw new ArgumentNullException(nameof(paymentTerms));

            var finalTotal = NormalizeMoneyToInt(est.final_total_cost);
            var depositPercent = NormalizePercent(paymentTerms.deposit_percent);

            var depositActual = Convert.ToInt32(Math.Round(
                finalTotal * depositPercent / 100m,
                0,
                MidpointRounding.AwayFromZero));

            var remaining = finalTotal - depositActual;
            return remaining > 0 ? remaining : 0;
        }

        public static decimal GetDepositDisplayAmount(cost_estimate est, PaymentTermsConfig paymentTerms)
        {
            return GetDepositAmount(est, paymentTerms);
        }

        public static decimal GetRemainingDisplayAmount(cost_estimate est, PaymentTermsConfig paymentTerms)
        {
            return GetRemainingAmount(est, paymentTerms);
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

        private static decimal NormalizePercent(decimal value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
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