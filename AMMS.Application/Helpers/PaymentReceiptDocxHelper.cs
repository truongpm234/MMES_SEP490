using AMMS.Infrastructure.Entities;
using AMMS.Shared.Constants;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AMMS.Application.Helpers
{
    public sealed class ReceiptCompanyInfo
    {
        public string CompanyName { get; set; } = "";
        public string Address { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string TaxCode { get; set; } = "";
        public string BankAccount { get; set; } = "";
        public string BankName { get; set; } = "";
    }

    public static class PaymentReceiptDocxHelper
    {
        private static readonly Regex PlaceholderRegex =
            new(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);

        public static Dictionary<string, string> BuildPlaceholders(
            order_request request,
            payment payment,
            order? order,
            cost_estimate? estimate,
            ReceiptCompanyInfo company,
            DateTime receiptDate,
            string receiptNo,
            string consultantName,
            decimal paidBeforeThisReceipt,
            decimal remainingAfterThisReceipt)
        {
            var requestCode = $"AM{request.order_request_id:D6}";
            var orderCode = !string.IsNullOrWhiteSpace(order?.code)
                ? order!.code
                : requestCode;

            var paymentTypeDisplay = string.Equals(
                payment.payment_type,
                PaymentTypes.Remaining,
                StringComparison.OrdinalIgnoreCase)
                ? "Thanh toán phần còn lại"
                : "Thanh toán tiền đặt cọc";

            var totalOrderValue =
                estimate?.final_total_cost
                ?? order?.total_amount
                ?? payment.amount;

            var reason = BuildReceiptReason(request, payment, orderCode);

            var paymentAmountText = ContractDocxHelper.NumberToVietnameseText(
                (long)Math.Round(payment.amount, 0, MidpointRounding.AwayFromZero));

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["{{COMPANY_NAME}}"] = company.CompanyName,
                ["{{COMPANY_ADDRESS}}"] = company.Address,
                ["{{COMPANY_PHONE}}"] = company.Phone,
                ["{{COMPANY_EMAIL}}"] = company.Email,
                ["{{COMPANY_TAX_CODE}}"] = company.TaxCode,
                ["{{COMPANY_BANK_ACCOUNT}}"] = company.BankAccount,
                ["{{COMPANY_BANK_NAME}}"] = company.BankName,

                ["{{RECEIPT_NO}}"] = receiptNo,
                ["{{RECEIPT_DAY}}"] = receiptDate.Day.ToString(),
                ["{{RECEIPT_MONTH}}"] = receiptDate.Month.ToString(),
                ["{{RECEIPT_YEAR}}"] = receiptDate.Year.ToString(),

                ["{{PAYER_NAME}}"] = request.customer_name ?? "",
                ["{{PAYER_ADDRESS}}"] = request.detail_address ?? "",

                ["{{RECEIPT_REASON}}"] = reason,

                ["{{REQUEST_CODE}}"] = requestCode,
                ["{{ORDER_CODE}}"] = orderCode,
                ["{{QUOTE_ID}}"] = payment.quote_id?.ToString() ?? request.quote_id?.ToString() ?? "",
                ["{{ESTIMATE_ID}}"] = payment.estimate_id?.ToString() ?? request.accepted_estimate_id?.ToString() ?? "",

                ["{{PRODUCT_NAME}}"] = request.product_name ?? "",
                ["{{QUANTITY}}"] = FormatNumber(request.quantity ?? 0),
                ["{{PAYMENT_TYPE_DISPLAY}}"] = paymentTypeDisplay,
                ["{{PAYMENT_METHOD}}"] = "Chuyển khoản ngân hàng",

                ["{{PAYMENT_AMOUNT}}"] = FormatNumber(payment.amount),
                ["{{PAYMENT_AMOUNT_TEXT}}"] = paymentAmountText,

                ["{{TOTAL_ORDER_VALUE}}"] = FormatNumber(totalOrderValue),
                ["{{PAID_BEFORE}}"] = FormatNumber(paidBeforeThisReceipt),
                ["{{REMAINING_AFTER}}"] = FormatNumber(remainingAfterThisReceipt),

                ["{{TRANSACTION_ID}}"] = payment.payos_transaction_id ?? "",
                ["{{PAYOS_ORDER_CODE}}"] = payment.order_code.ToString(),

                ["{{PAYER_SIGN_NAME}}"] = request.customer_name ?? "",
                ["{{CONSULTANT_SIGN_NAME}}"] = consultantName
            };
        }

        public static byte[] GenerateDocx(byte[] templateBytes, IDictionary<string, string> placeholders)
        {
            var normalizedPlaceholders = NormalizePlaceholderMap(placeholders);

            using var memoryStream = new MemoryStream();
            memoryStream.Write(templateBytes, 0, templateBytes.Length);
            memoryStream.Position = 0;

            using (var document = WordprocessingDocument.Open(memoryStream, true))
            {
                var mainPart = document.MainDocumentPart
                    ?? throw new InvalidOperationException("MainDocumentPart is missing.");

                ProcessRoot(mainPart.Document.Body, normalizedPlaceholders);

                foreach (var headerPart in mainPart.HeaderParts)
                {
                    ProcessRoot(headerPart.Header, normalizedPlaceholders);
                    headerPart.Header.Save();
                }

                foreach (var footerPart in mainPart.FooterParts)
                {
                    ProcessRoot(footerPart.Footer, normalizedPlaceholders);
                    footerPart.Footer.Save();
                }

                mainPart.Document.Save();
            }

            return memoryStream.ToArray();
        }

        private static void ProcessRoot(OpenXmlElement? root, IReadOnlyDictionary<string, string> placeholders)
        {
            if (root == null) return;

            foreach (var paragraph in root.Descendants<Paragraph>())
            {
                ReplaceInParagraphPreserveFormatting(paragraph, placeholders);
            }
        }

        private static void ReplaceInParagraphPreserveFormatting(
            Paragraph paragraph,
            IReadOnlyDictionary<string, string> placeholders)
        {
            var textNodes = paragraph.Descendants<Text>().ToList();
            if (textNodes.Count == 0) return;

            var styledChars = BuildStyledChars(textNodes);
            if (styledChars.Count == 0) return;

            var originalText = new string(styledChars.Select(x => x.Character).ToArray());
            if (!PlaceholderRegex.IsMatch(originalText))
                return;

            var matches = PlaceholderRegex.Matches(originalText);
            if (matches.Count == 0)
                return;

            var segments = new List<StyledTextSegment>();
            int cursor = 0;
            bool changed = false;

            foreach (Match match in matches)
            {
                if (match.Index > cursor)
                {
                    AppendOriginalRange(segments, styledChars, cursor, match.Index - cursor);
                }

                var rawKey = match.Groups[1].Value.Trim();

                if (placeholders.TryGetValue(rawKey, out var replacement))
                {
                    changed = true;
                    var runProps = GetRunPropsAt(styledChars, match.Index);
                    AddSegment(segments, replacement ?? string.Empty, runProps);
                }
                else
                {
                    AppendOriginalRange(segments, styledChars, match.Index, match.Length);
                }

                cursor = match.Index + match.Length;
            }

            if (cursor < styledChars.Count)
            {
                AppendOriginalRange(segments, styledChars, cursor, styledChars.Count - cursor);
            }

            if (!changed)
                return;

            paragraph.RemoveAllChildren<Run>();

            foreach (var seg in segments)
            {
                if (string.IsNullOrEmpty(seg.Text))
                    continue;

                var run = new Run();

                if (seg.RunProperties != null)
                    run.RunProperties = (RunProperties)seg.RunProperties.CloneNode(true);

                run.AppendChild(new Text(seg.Text)
                {
                    Space = SpaceProcessingModeValues.Preserve
                });

                paragraph.AppendChild(run);
            }
        }

        private static List<StyledCharInfo> BuildStyledChars(List<Text> textNodes)
        {
            var result = new List<StyledCharInfo>();

            foreach (var textNode in textNodes)
            {
                var textValue = textNode.Text ?? string.Empty;
                if (textValue.Length == 0) continue;

                var run = textNode.Ancestors<Run>().FirstOrDefault();
                var runProps = run?.RunProperties?.CloneNode(true) as RunProperties;
                var propsKey = runProps?.OuterXml ?? string.Empty;

                foreach (var ch in textValue)
                {
                    result.Add(new StyledCharInfo
                    {
                        Character = ch,
                        RunProperties = runProps,
                        RunPropertiesKey = propsKey
                    });
                }
            }

            return result;
        }

        private static void AppendOriginalRange(
            List<StyledTextSegment> segments,
            List<StyledCharInfo> styledChars,
            int start,
            int length)
        {
            if (length <= 0) return;

            int end = start + length;
            int i = start;

            while (i < end)
            {
                var currentKey = styledChars[i].RunPropertiesKey;
                var currentProps = styledChars[i].RunProperties;
                var sb = new StringBuilder();

                while (i < end && styledChars[i].RunPropertiesKey == currentKey)
                {
                    sb.Append(styledChars[i].Character);
                    i++;
                }

                AddSegment(segments, sb.ToString(), currentProps);
            }
        }

        private static void AddSegment(
            List<StyledTextSegment> segments,
            string text,
            RunProperties? runProps)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var propsKey = runProps?.OuterXml ?? string.Empty;

            if (segments.Count > 0 && segments[^1].RunPropertiesKey == propsKey)
            {
                segments[^1].Text += text;
                return;
            }

            segments.Add(new StyledTextSegment
            {
                Text = text,
                RunProperties = runProps == null ? null : (RunProperties)runProps.CloneNode(true),
                RunPropertiesKey = propsKey
            });
        }

        private static RunProperties? GetRunPropsAt(List<StyledCharInfo> styledChars, int index)
        {
            if (styledChars.Count == 0)
                return null;

            if (index < 0)
                index = 0;

            if (index >= styledChars.Count)
                index = styledChars.Count - 1;

            return styledChars[index].RunProperties == null
                ? null
                : (RunProperties)styledChars[index].RunProperties!.CloneNode(true);
        }

        private static Dictionary<string, string> NormalizePlaceholderMap(IDictionary<string, string> placeholders)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in placeholders)
            {
                var key = kv.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var match = PlaceholderRegex.Match(key);
                if (match.Success)
                {
                    result[match.Groups[1].Value.Trim()] = kv.Value ?? string.Empty;
                }
                else
                {
                    result[key.Trim('{', '}', ' ')] = kv.Value ?? string.Empty;
                }
            }

            return result;
        }

        private static string BuildReceiptReason(order_request request, payment payment, string orderCode)
        {
            var productName = string.IsNullOrWhiteSpace(request.product_name)
                ? "đơn hàng in ấn"
                : request.product_name.Trim();

            if (string.Equals(payment.payment_type, PaymentTypes.Remaining, StringComparison.OrdinalIgnoreCase))
            {
                return $"Thu tiền thanh toán phần còn lại của đơn hàng {orderCode} - {productName}.";
            }

            return $"Thu tiền đặt cọc cho đơn hàng {orderCode} - {productName}.";
        }

        public static string FormatNumber(decimal value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);
        }

        public static string FormatNumber(int value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);
        }

        private sealed class StyledCharInfo
        {
            public char Character { get; set; }
            public RunProperties? RunProperties { get; set; }
            public string RunPropertiesKey { get; set; } = "";
        }

        private sealed class StyledTextSegment
        {
            public string Text { get; set; } = "";
            public RunProperties? RunProperties { get; set; }
            public string RunPropertiesKey { get; set; } = "";
        }
    }
}