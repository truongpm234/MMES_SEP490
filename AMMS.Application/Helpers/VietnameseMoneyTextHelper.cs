using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Text;

namespace AMMS.Application.Helpers
{
    public static class VietnameseMoneyTextHelper
    {
        private static readonly string[] Digits =
        {
            "không", "một", "hai", "ba", "bốn",
            "năm", "sáu", "bảy", "tám", "chín"
        };

        private static readonly string[] Units =
        {
            "", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ"
        };

        public static string ToVietnameseText(decimal amount)
        {
            long number = Convert.ToInt64(Math.Round(amount, 0, MidpointRounding.AwayFromZero));

            if (number == 0)
                return "Không đồng";

            if (number < 0)
                return "Âm " + ToVietnameseText(Math.Abs(number));

            var groups = new List<int>();
            while (number > 0)
            {
                groups.Add((int)(number % 1000));
                number /= 1000;
            }

            var parts = new List<string>();

            for (int i = groups.Count - 1; i >= 0; i--)
            {
                int groupValue = groups[i];
                if (groupValue == 0) continue;

                bool isFirstGroup = i == groups.Count - 1;
                bool forceFull = !isFirstGroup && groupValue < 100;

                var groupText = ReadThreeDigits(groupValue, forceFull);
                if (!string.IsNullOrWhiteSpace(groupText))
                {
                    if (!string.IsNullOrWhiteSpace(Units[i]))
                        groupText += " " + Units[i];

                    parts.Add(groupText);
                }
            }

            var result = string.Join(" ", parts);
            result = NormalizeSpaces(result);

            if (string.IsNullOrWhiteSpace(result))
                return "Không đồng";

            return char.ToUpper(result[0]) + result.Substring(1) + " đồng";
        }

        private static string ReadThreeDigits(int number, bool forceFull)
        {
            int hundreds = number / 100;
            int tens = (number % 100) / 10;
            int ones = number % 10;

            var parts = new List<string>();

            if (hundreds > 0 || forceFull)
            {
                parts.Add(Digits[hundreds]);
                parts.Add("trăm");
            }

            if (tens > 1)
            {
                parts.Add(Digits[tens]);
                parts.Add("mươi");

                if (ones == 1)
                    parts.Add("mốt");
                else if (ones == 4)
                    parts.Add("tư");
                else if (ones == 5)
                    parts.Add("lăm");
                else if (ones > 0)
                    parts.Add(Digits[ones]);
            }
            else if (tens == 1)
            {
                parts.Add("mười");

                if (ones == 5)
                    parts.Add("lăm");
                else if (ones > 0)
                    parts.Add(Digits[ones]);
            }
            else
            {
                if (ones > 0)
                {
                    if (hundreds > 0 || forceFull)
                        parts.Add("linh");

                    parts.Add(Digits[ones]);
                }
            }

            return NormalizeSpaces(string.Join(" ", parts));
        }

        private static string NormalizeSpaces(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            return string.Join(" ", input
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
