using AMMS.Infrastructure.Entities;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace AMMS.Application.Helpers
{
    public static class ContractDocxHelper
    {
        private static readonly string[] DigitTexts =
        {
            "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"
        };

        public static ContractAmountBreakdown CalculateAmounts(cost_estimate estimate, decimal vatPercent)
        {
            var finalTotalCost = estimate.final_total_cost;

            decimal subtotalBeforeVat;
            decimal vatAmount;

            if (vatPercent > 0m)
            {
                subtotalBeforeVat = Math.Round(
                    finalTotalCost / (1 + vatPercent / 100m),
                    0,
                    MidpointRounding.AwayFromZero);

                vatAmount = finalTotalCost - subtotalBeforeVat;
            }
            else
            {
                subtotalBeforeVat = finalTotalCost;
                vatAmount = 0m;
            }

            var depositAmount = estimate.deposit_amount;
            var remainingAmount = finalTotalCost - depositAmount;
            if (remainingAmount < 0m) remainingAmount = 0m;

            return new ContractAmountBreakdown(
                vatPercent,
                subtotalBeforeVat,
                vatAmount,
                finalTotalCost,
                depositAmount,
                remainingAmount
            );
        }

        public static Dictionary<string, string> BuildPlaceholders(
            order_request request,
            cost_estimate estimate,
            decimal vatPercent,
            DateTime signDate)
        {
            var amounts = CalculateAmounts(estimate, vatPercent);

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["{{CONTRACT_NO}}"] = $"AM{request.order_request_id:D6}",
                ["{{CONTRACT_DAY}}"] = signDate.Day.ToString(),
                ["{{CONTRACT_MONTH}}"] = signDate.Month.ToString(),
                ["{{CONTRACT_YEAR}}"] = signDate.Year.ToString(),

                ["{{CUSTOMER_NAME}}"] = request.customer_name ?? "",
                ["{{CUSTOMER_ADDRESS}}"] = request.detail_address ?? "",
                ["{{CUSTOMER_PHONE}}"] = request.customer_phone ?? "",
                ["{{CUSTOMER_REPRESENTATIVE}}"] = request.customer_name ?? "",

                ["{{REQUEST_ID}}"] = request.order_request_id.ToString(),

                ["{{PRODUCT_NAME}}"] = request.product_name ?? "",
                ["{{CUSTOMER_SIGN_NAME}}"] = request.customer_name ?? "",
                ["{{PRODUCT_SPEC}}"] = BuildProductSpec(request, estimate),
                ["{{QUANTITY}}"] = FormatNumber(request.quantity ?? 0),

                ["{{SUBTOTAL_BEFORE_VAT}}"] = FormatNumber(amounts.SubtotalBeforeVat),
                ["{{VAT_PERCENT}}"] = amounts.VatPercent.ToString("0.##", CultureInfo.InvariantCulture),
                ["{{VAT_AMOUNT}}"] = FormatNumber(amounts.VatAmount),
                ["{{FINAL_TOTAL_COST}}"] = FormatNumber(amounts.FinalTotalCost),
                ["{{FINAL_TOTAL_COST_TEXT}}"] = NumberToVietnameseText(
                    (long)Math.Round(amounts.FinalTotalCost, 0, MidpointRounding.AwayFromZero)),

                ["{{DELIVERY_DATE}}"] = FormatDate(request.delivery_date ?? estimate.desired_delivery_date),
                ["{{DELIVERY_ADDRESS}}"] = request.detail_address ?? "",

                ["{{DEPOSIT_AMOUNT}}"] = FormatNumber(amounts.DepositAmount),
                ["{{REMAINING_AMOUNT}}"] = FormatNumber(amounts.RemainingAmount)
            };
        }

        public static byte[] GenerateDocx(byte[] templateBytes, IDictionary<string, string> placeholders)
        {
            using var memoryStream = new MemoryStream();
            memoryStream.Write(templateBytes, 0, templateBytes.Length);
            memoryStream.Position = 0;

            using (var document = WordprocessingDocument.Open(memoryStream, true))
            {
                var mainPart = document.MainDocumentPart;
                if (mainPart == null)
                    throw new InvalidOperationException("MainDocumentPart is missing.");

                ReplaceInOpenXmlElement(mainPart.Document.Body, placeholders);

                foreach (var headerPart in mainPart.HeaderParts)
                {
                    ReplaceInOpenXmlElement(headerPart.Header, placeholders);
                    headerPart.Header.Save();
                }

                foreach (var footerPart in mainPart.FooterParts)
                {
                    ReplaceInOpenXmlElement(footerPart.Footer, placeholders);
                    footerPart.Footer.Save();
                }

                mainPart.Document.Save();
            }

            return memoryStream.ToArray();
        }

        private static void ReplaceInOpenXmlElement(OpenXmlElement? root, IDictionary<string, string> placeholders)
        {
            if (root == null) return;

            foreach (var paragraph in root.Descendants<Paragraph>())
            {
                ReplaceInParagraph(paragraph, placeholders);
            }
        }

        private static void ReplaceInParagraph(Paragraph paragraph, IDictionary<string, string> placeholders)
        {
            var texts = paragraph.Descendants<Text>().ToList();
            if (texts.Count == 0) return;

            var originalText = string.Concat(texts.Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(originalText)) return;

            var replacedText = ApplyPlaceholders(originalText, placeholders);

            if (replacedText == originalText)
                return;

            var firstRun = paragraph.Elements<Run>().FirstOrDefault();
            RunProperties? runProps = firstRun?.RunProperties?.CloneNode(true) as RunProperties;

            // bỏ bold khỏi nội dung sinh ra
            if (runProps != null)
            {
                runProps.RemoveAllChildren<Bold>();
                runProps.RemoveAllChildren<BoldComplexScript>();
            }

            paragraph.RemoveAllChildren<Run>();

            var newRun = new Run();
            if (runProps != null)
                newRun.RunProperties = runProps;

            newRun.AppendChild(new Text(replacedText)
            {
                Space = SpaceProcessingModeValues.Preserve
            });

            paragraph.AppendChild(newRun);
        }

        private static string ApplyPlaceholders(string input, IDictionary<string, string> placeholders)
        {
            var result = input;

            foreach (var kv in placeholders)
            {
                result = result.Replace(kv.Key, kv.Value ?? string.Empty, StringComparison.Ordinal);
            }

            return result;
        }

        public static string BuildProductSpec(order_request request, cost_estimate estimate)
        {
            var parts = new List<string>();

            if (request.product_length_mm.HasValue || request.product_width_mm.HasValue || request.product_height_mm.HasValue)
            {
                parts.Add(
                    $"Kích thước thành phẩm: {request.product_length_mm ?? 0} x {request.product_width_mm ?? 0} x {request.product_height_mm ?? 0} mm");
            }

            if (request.print_width_mm.HasValue || request.print_length_mm.HasValue)
            {
                parts.Add(
                    $"Khổ in: {request.print_width_mm ?? 0} x {request.print_length_mm ?? 0} mm");
            }

            if (!string.IsNullOrWhiteSpace(estimate.paper_name))
                parts.Add($"Giấy: {estimate.paper_name.Trim()}");

            if (!string.IsNullOrWhiteSpace(estimate.wave_type))
                parts.Add($"Sóng: {estimate.wave_type.Trim()}");

            var coatingDisplay = GetDisplayCoatingType(estimate.coating_type);
            if (!string.IsNullOrWhiteSpace(coatingDisplay))
                parts.Add($"Phủ: {coatingDisplay}");

            return string.Join(" | ", parts);
        }

        private static string GetDisplayCoatingType(string? coatingType)
        {
            var code = (coatingType ?? "").Trim().ToUpperInvariant();

            return code switch
            {
                "KEO_PHU_NUOC" => "Keo phủ nước",
                "KEO_PHU_DAU" => "Keo phủ dầu",
                "KEO_NUOC" => "Keo phủ nước",
                "KEO_DAU" => "Keo phủ dầu",
                "UV" => "Phủ UV",
                _ => (coatingType ?? "").Trim()
            };
        }

        public static string FormatDate(DateTime? value)
        {
            return value?.ToString("dd/MM/yyyy") ?? "";
        }

        public static string FormatNumber(decimal value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);
        }

        public static string FormatNumber(int value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);
        }

        public static string NumberToVietnameseText(long number)
        {
            if (number == 0) return "Không đồng";

            var unitGroups = new[]
            {
                "",
                " nghìn",
                " triệu",
                " tỷ",
                " nghìn tỷ",
                " triệu tỷ"
            };

            var groups = new List<int>();

            while (number > 0)
            {
                groups.Add((int)(number % 1000));
                number /= 1000;
            }

            var parts = new List<string>();

            for (int i = groups.Count - 1; i >= 0; i--)
            {
                var groupValue = groups[i];
                if (groupValue == 0) continue;

                var full = i < groups.Count - 1;
                var groupText = ReadThreeDigits(groupValue, full);

                if (!string.IsNullOrWhiteSpace(groupText))
                {
                    var unit = i < unitGroups.Length ? unitGroups[i] : "";
                    parts.Add(groupText + unit);
                }
            }

            var result = string.Join(" ", parts)
                .Replace("  ", " ")
                .Trim();

            if (string.IsNullOrWhiteSpace(result))
                return "Không đồng";

            return char.ToUpper(result[0]) + result.Substring(1) + " đồng";
        }

        private static string ReadThreeDigits(int number, bool full)
        {
            int hundred = number / 100;
            int ten = (number % 100) / 10;
            int unit = number % 10;

            var parts = new List<string>();

            if (hundred > 0 || full)
            {
                if (hundred > 0)
                    parts.Add(DigitTexts[hundred] + " trăm");
                else
                    parts.Add("không trăm");
            }

            if (ten > 1)
            {
                parts.Add(DigitTexts[ten] + " mươi");

                if (unit == 1) parts.Add("mốt");
                else if (unit == 4) parts.Add("bốn");
                else if (unit == 5) parts.Add("lăm");
                else if (unit > 0) parts.Add(DigitTexts[unit]);
            }
            else if (ten == 1)
            {
                parts.Add("mười");

                if (unit == 5) parts.Add("lăm");
                else if (unit > 0) parts.Add(DigitTexts[unit]);
            }
            else
            {
                if (unit > 0)
                {
                    if (hundred > 0 || full)
                        parts.Add("lẻ");

                    parts.Add(DigitTexts[unit]);
                }
            }

            return string.Join(" ", parts).Trim();
        }
    }

    public readonly record struct ContractAmountBreakdown(
        decimal VatPercent,
        decimal SubtotalBeforeVat,
        decimal VatAmount,
        decimal FinalTotalCost,
        decimal DepositAmount,
        decimal RemainingAmount
    );
}
