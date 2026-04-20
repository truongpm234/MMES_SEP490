using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using SixLabors.ImageSharp.Processing;

namespace AMMS.Application.Services
{
    public class ContractCompareService : IContractCompareService
    {
        private readonly HttpClient _httpClient;
        private readonly IPdfDigitalSignatureValidator _pdfDigitalSignatureValidator;

        public ContractCompareService(
            HttpClient httpClient,
            IPdfDigitalSignatureValidator pdfDigitalSignatureValidator)
        {
            _httpClient = httpClient;
            _pdfDigitalSignatureValidator = pdfDigitalSignatureValidator;
        }

        public async Task<CompareContractResponse> CompareAsync(
            int requestId,
            int estimateId,
            string expectedCustomerName,
            string consultantDocxUrl,
            string customerPdfUrl,
            CancellationToken ct = default)
        {
            var consultantBytes = await DownloadBytesAsync(consultantDocxUrl, "hợp đồng tư vấn viên", ct);
            var customerPdfBytes = await DownloadBytesAsync(customerPdfUrl, "hợp đồng khách hàng đã ký", ct);

            return await CompareCoreAsync(
                requestId,
                estimateId,
                expectedCustomerName,
                consultantBytes,
                customerPdfBytes,
                ct);
        }

        public async Task<CompareContractResponse> CompareAsync(
            int requestId,
            int estimateId,
            string expectedCustomerName,
            string consultantDocxUrl,
            byte[] customerPdfBytes,
            CancellationToken ct = default)
        {
            var consultantBytes = await DownloadBytesAsync(consultantDocxUrl, "hợp đồng tư vấn viên", ct);

            return await CompareCoreAsync(
                requestId,
                estimateId,
                expectedCustomerName,
                consultantBytes,
                customerPdfBytes,
                ct);
        }

        private async Task<CompareContractResponse> CompareCoreAsync(
            int requestId,
            int estimateId,
            string expectedCustomerName,
            byte[] consultantDocxBytes,
            byte[] customerPdfBytes,
            CancellationToken ct)
        {
            // 1) Đọc DOCX theo paragraph, không ghép tất cả thành 1 dòng dài
            var consultantStructuredText = ExtractDocxStructuredText(consultantDocxBytes);

            // 2) Đọc PDF text-layer trước
            var customerPdfText = ExtractPdfStructuredText(customerPdfBytes);

            // 3) Cắt đúng phạm vi Điều 1 -> trước block ký
            var consultantClauseText = ExtractClauseRangeFromDieu1ToDieu7(consultantStructuredText);

            var customerClauseTextFromPdf = SafeExtractClauseRange(customerPdfText);

            // 4) OCR toàn bộ PDF để có thêm lớp kiểm tra “nội dung hiển thị thực tế”
            //    Nếu OCR lỗi thì vẫn fallback về PDF text
            string? customerOcrText = null;
            string? customerClauseTextFromOcr = null;
            bool usedOcrFallback = false;

            try
            {
                customerOcrText = await OcrWholePdfAsync(customerPdfBytes, ct);
                customerClauseTextFromOcr = SafeExtractClauseRange(customerOcrText);
                usedOcrFallback = !string.IsNullOrWhiteSpace(customerClauseTextFromOcr);
            }
            catch
            {
                // không throw ở đây để tránh chết flow nếu môi trường OCR chưa sẵn
                customerOcrText = null;
                customerClauseTextFromOcr = null;
                usedOcrFallback = false;
            }

            if (string.IsNullOrWhiteSpace(customerClauseTextFromPdf) &&
                string.IsNullOrWhiteSpace(customerClauseTextFromOcr))
            {
                throw new InvalidOperationException(
                    "Không trích xuất được nội dung hợp đồng trong phạm vi Điều 1 đến Điều 7 từ file PDF khách hàng.");
            }

            // 5) Build line list để so sánh chặt
            var expectedLines = BuildComparableLines(consultantClauseText);

            var actualPdfLines = string.IsNullOrWhiteSpace(customerClauseTextFromPdf)
                ? new List<string>()
                : BuildComparableLines(customerClauseTextFromPdf);

            var actualOcrLines = string.IsNullOrWhiteSpace(customerClauseTextFromOcr)
                ? new List<string>()
                : BuildComparableLines(customerClauseTextFromOcr);

            var expectedJoined = string.Join("\n", expectedLines);
            var actualPdfJoined = string.Join("\n", actualPdfLines);
            var actualOcrJoined = string.Join("\n", actualOcrLines);

            var pdfBodyExactMatch = !string.IsNullOrWhiteSpace(actualPdfJoined) &&
                                    string.Equals(expectedJoined, actualPdfJoined, StringComparison.Ordinal);

            var ocrBodyExactMatch = !string.IsNullOrWhiteSpace(actualOcrJoined) &&
                                    string.Equals(expectedJoined, actualOcrJoined, StringComparison.Ordinal);

            // Nếu có OCR thì yêu cầu cả PDF text layer và OCR đều không được phát hiện sai khác
            bool bodyExactMatch;
            decimal similarityPercent;
            List<TextDifferenceItemDto> textDifferences;

            if (!string.IsNullOrWhiteSpace(actualOcrJoined))
            {
                bodyExactMatch = pdfBodyExactMatch && ocrBodyExactMatch;

                var pdfSimilarity = Math.Round(
                    (decimal)(ComputeDiceSimilarity(expectedJoined, actualPdfJoined, 2) * 100d), 2);

                var ocrSimilarity = Math.Round(
                    (decimal)(ComputeDiceSimilarity(expectedJoined, actualOcrJoined, 2) * 100d), 2);

                similarityPercent = Math.Min(pdfSimilarity, ocrSimilarity);

                var pdfDiffs = BuildTextDifferences(expectedLines, actualPdfLines, 50);
                var ocrDiffs = BuildTextDifferences(expectedLines, actualOcrLines, 50);

                // ưu tiên diff từ OCR nếu OCR thấy lỗi rõ hơn
                textDifferences = ocrDiffs.Count > 0 ? ocrDiffs : pdfDiffs;
            }
            else
            {
                bodyExactMatch = pdfBodyExactMatch;

                similarityPercent = Math.Round(
                    (decimal)(ComputeDiceSimilarity(expectedJoined, actualPdfJoined, 2) * 100d), 2);

                textDifferences = BuildTextDifferences(expectedLines, actualPdfLines, 50);
            }

            // 6) Check chữ ký
            var signatureAnalysis = await AnalyzeCustomerSignatureRegionAsync(
                customerPdfBytes,
                expectedCustomerName,
                ct);

            var digitalSignature = await _pdfDigitalSignatureValidator.ValidateAsync(customerPdfBytes, ct);

            var visibleSignatureAccepted =
                signatureAnalysis.has_visible_signature &&
                signatureAnalysis.signature_inside_allowed_box;

            var signatureAccepted =
                digitalSignature.is_valid || visibleSignatureAccepted;

            var isMatch = bodyExactMatch && signatureAccepted;

            return new CompareContractResponse
            {
                request_id = requestId,
                estimate_id = estimateId,

                is_match = isMatch,

                body_text_exact_match = bodyExactMatch,
                similarity_percent = similarityPercent,

                signature_name_present = signatureAnalysis.signature_name_present,
                signature_mark_present = signatureAnalysis.has_visible_signature,

                has_digital_signature = digitalSignature.has_signature,
                digital_signature_valid = digitalSignature.is_valid,

                used_ocr_fallback = usedOcrFallback,

                consultant_text_length = expectedJoined.Length,
                customer_text_length = !string.IsNullOrWhiteSpace(actualOcrJoined)
                    ? actualOcrJoined.Length
                    : actualPdfJoined.Length,

                verification_mode = digitalSignature.is_valid
                    ? "ĐỐI CHIẾU CHẶT ĐIỀU 1-7 + CHỮ KÝ SỐ"
                    : "ĐỐI CHIẾU CHẶT ĐIỀU 1-7 + CHỮ KÝ TAY ĐÚNG Ô",

                reject_reason = BuildRejectReason(
                    bodyExactMatch,
                    signatureAnalysis,
                    digitalSignature.is_valid,
                    textDifferences),

                text_differences = textDifferences,

                debug_signature_box_rect = ToRectString(signatureAnalysis.allowed_signature_box),
                debug_signature_name_rect = ToRectString(signatureAnalysis.name_band)
            };
        }

        private static string? BuildRejectReason(
            bool bodyExactMatch,
            SignatureAnalysisResult signatureAnalysis,
            bool digitalSignatureValid,
            List<TextDifferenceItemDto> diffs)
        {
            if (bodyExactMatch &&
                (digitalSignatureValid ||
                 (signatureAnalysis.has_visible_signature && signatureAnalysis.signature_inside_allowed_box)))
            {
                return null;
            }

            var reasons = new List<string>();

            if (!bodyExactMatch)
            {
                if (diffs.Count == 0)
                {
                    reasons.Add("Nội dung hợp đồng trong phạm vi Điều 1 đến Điều 7 đã bị thay đổi.");
                }
                else
                {
                    reasons.Add("Nội dung hợp đồng trong phạm vi Điều 1 đến Điều 7 đã bị thay đổi.");
                }
            }

            if (!digitalSignatureValid)
            {
                if (!signatureAnalysis.has_visible_signature)
                {
                    reasons.Add("Không phát hiện chữ ký tay trong vùng ký của khách hàng.");
                }
                else if (!signatureAnalysis.signature_inside_allowed_box)
                {
                    reasons.Add("Phát hiện chữ ký nhưng chữ ký nằm ngoài ô ký cho phép của khách hàng.");
                }
            }

            return string.Join(" ", reasons);
        }

        /// <summary>
        /// Đọc DOCX theo paragraph để giữ cấu trúc dòng/đoạn.
        /// </summary>
        private static string ExtractDocxStructuredText(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var doc = WordprocessingDocument.Open(ms, false);

            var lines = new List<string>();

            static void ReadParagraphs(OpenXmlPartRootElement? root, List<string> output)
            {
                if (root == null) return;

                foreach (var p in root.Descendants<Paragraph>())
                {
                    var text = (p.InnerText ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        output.Add(text);
                }
            }

            ReadParagraphs(doc.MainDocumentPart?.Document, lines);

            foreach (var headerPart in doc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
            {
                ReadParagraphs(headerPart.Header, lines);
            }

            foreach (var footerPart in doc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
            {
                ReadParagraphs(footerPart.Footer, lines);
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Đọc PDF text-layer, giữ tách trang/tách dòng ở mức tốt nhất có thể.
        /// </summary>
        private static string ExtractPdfStructuredText(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var pdf = PdfDocument.Open(ms);

            var sb = new StringBuilder();

            foreach (var page in pdf.GetPages())
            {
                sb.AppendLine(page.Text);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Chỉ lấy nội dung từ Điều 1 đến trước phần ký "ĐẠI DIỆN BÊN A".
        /// </summary>
        private static string ExtractClauseRangeFromDieu1ToDieu7(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new InvalidOperationException("Nội dung hợp đồng trống, không thể đối chiếu.");

            var lines = input
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            var result = new List<string>();
            bool started = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine ?? "";
                var marker = CanonicalMarker(line);

                if (!started)
                {
                    if (Regex.IsMatch(marker, @"\bdieu\s*1\b"))
                    {
                        started = true;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (Regex.IsMatch(marker, @"\bdai\s*dien\s*ben\s*a\b"))
                    break;

                result.Add(line);
            }

            var text = string.Join("\n", result).Trim();

            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Không tìm thấy phạm vi từ Điều 1 để đối chiếu.");

            var wholeMarker = CanonicalMarker(text);
            if (!Regex.IsMatch(wholeMarker, @"\bdieu\s*7\b"))
            {
                throw new InvalidOperationException(
                    "Không tìm thấy đầy đủ phạm vi từ Điều 1 đến Điều 7 trong hợp đồng để đối chiếu.");
            }

            return text;
        }

        private static string? SafeExtractClauseRange(string input)
        {
            try
            {
                return ExtractClauseRangeFromDieu1ToDieu7(input);
            }
            catch
            {
                return null;
            }
        }

        private static string CanonicalMarker(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var text = WebUtility.HtmlDecode(input);
            text = text.Normalize(NormalizationForm.FormKC);
            text = RemoveDiacritics(text).ToLowerInvariant();
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return sb.ToString()
                .Normalize(NormalizationForm.FormC)
                .Replace('đ', 'd')
                .Replace('Đ', 'D');
        }

        /// <summary>
        /// Dùng để so sánh chặt theo line:
        /// - bỏ bullet đầu dòng
        /// - chuẩn hóa khoảng trắng
        /// - không dùng similarity để cho pass
        /// </summary>
        private static List<string> BuildComparableLines(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            return input
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeComparableLine)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static string NormalizeComparableLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return "";

            var text = WebUtility.HtmlDecode(line);
            text = text.Normalize(NormalizationForm.FormKC);
            text = text.Replace('\u00A0', ' ');

            // bỏ bullet đầu dòng
            text = Regex.Replace(
                text,
                @"^\s*([•·●▪◦■\-–—\*\+]+\s*)+",
                "");

            // chuẩn hóa khoảng trắng
            text = Regex.Replace(text, @"\s+", " ").Trim();

            // bỏ khác biệt hoa/thường
            text = text.ToLowerInvariant();

            return text;
        }

        private static List<TextDifferenceItemDto> BuildTextDifferences(
            List<string> expectedLines,
            List<string> actualLines,
            int maxItems)
        {
            var diffs = new List<TextDifferenceItemDto>();

            int m = expectedLines.Count;
            int n = actualLines.Count;

            var dp = new int[m + 1, n + 1];

            for (int i = m - 1; i >= 0; i--)
            {
                for (int j = n - 1; j >= 0; j--)
                {
                    if (expectedLines[i] == actualLines[j])
                        dp[i, j] = dp[i + 1, j + 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }

            int x = 0, y = 0;
            var rawOps = new List<TextDifferenceItemDto>();

            while (x < m && y < n)
            {
                if (expectedLines[x] == actualLines[y])
                {
                    x++;
                    y++;
                    continue;
                }

                if (dp[x + 1, y] >= dp[x, y + 1])
                {
                    rawOps.Add(new TextDifferenceItemDto
                    {
                        type = "removed",
                        expected_line = x + 1,
                        actual_line = y + 1,
                        expected_text = expectedLines[x],
                        actual_text = ""
                    });
                    x++;
                }
                else
                {
                    rawOps.Add(new TextDifferenceItemDto
                    {
                        type = "added",
                        expected_line = x + 1,
                        actual_line = y + 1,
                        expected_text = "",
                        actual_text = actualLines[y]
                    });
                    y++;
                }
            }

            while (x < m)
            {
                rawOps.Add(new TextDifferenceItemDto
                {
                    type = "removed",
                    expected_line = x + 1,
                    actual_line = y + 1,
                    expected_text = expectedLines[x],
                    actual_text = ""
                });
                x++;
            }

            while (y < n)
            {
                rawOps.Add(new TextDifferenceItemDto
                {
                    type = "added",
                    expected_line = x + 1,
                    actual_line = y + 1,
                    expected_text = "",
                    actual_text = actualLines[y]
                });
                y++;
            }

            for (int i = 0; i < rawOps.Count; i++)
            {
                if (diffs.Count >= maxItems)
                    break;

                var current = rawOps[i];

                if (i + 1 < rawOps.Count)
                {
                    var next = rawOps[i + 1];

                    var isReplacePair =
                        (current.type == "removed" && next.type == "added") ||
                        (current.type == "added" && next.type == "removed");

                    if (isReplacePair)
                    {
                        diffs.Add(new TextDifferenceItemDto
                        {
                            type = "changed",
                            expected_line = current.type == "removed" ? current.expected_line : next.expected_line,
                            actual_line = current.type == "added" ? current.actual_line : next.actual_line,
                            expected_text = current.type == "removed" ? current.expected_text : next.expected_text,
                            actual_text = current.type == "added" ? current.actual_text : next.actual_text
                        });

                        i++;
                        continue;
                    }
                }

                diffs.Add(current);
            }

            return diffs;
        }

        private async Task<SignatureAnalysisResult> AnalyzeCustomerSignatureRegionAsync(
            byte[] pdfBytes,
            string expectedCustomerName,
            CancellationToken ct)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "contract-sign-check", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var pdfPath = Path.Combine(tempRoot, "signed.pdf");
                await File.WriteAllBytesAsync(pdfPath, pdfBytes, ct);

                var pageCount = GetPdfPageCount(pdfBytes);
                var imagePrefix = Path.Combine(tempRoot, "page");

                await RunProcessAsync(
                    "pdftoppm",
                    $"-f {pageCount} -l {pageCount} -png \"{pdfPath}\" \"{imagePrefix}\"",
                    ct);

                var renderedFile = Directory.GetFiles(tempRoot, "page-*.png")
                    .OrderBy(x => x)
                    .LastOrDefault();

                if (renderedFile == null)
                    return new SignatureAnalysisResult();

                using var image = await Image.LoadAsync<Rgba32>(renderedFile, ct);

                // Panel khách hàng bên trái cuối trang
                var customerPanel = ToRectangle(image.Width, image.Height, 0.04, 0.73, 0.42, 0.24);

                // Ô ký hợp lệ
                var allowedSignatureBox = ToRectangle(image.Width, image.Height, 0.08, 0.80, 0.28, 0.08);

                // Vùng quan sát lớn hơn ô ký một chút để bắt trường hợp ký lệch
                var observationBox = ToRectangle(image.Width, image.Height, 0.04, 0.77, 0.40, 0.12);

                // Dòng họ tên in sẵn dưới chữ ký
                var nameBand = ToRectangle(image.Width, image.Height, 0.06, 0.90, 0.32, 0.04);

                var nameBandPath = Path.Combine(tempRoot, "signature-name-band.png");

                using (var nameCrop = image.Clone(ctx => ctx.Crop(nameBand)))
                {
                    await nameCrop.SaveAsPngAsync(nameBandPath, ct);
                }

                var nameBandText = await OcrImageAsync(nameBandPath, ct);

                var signatureNamePresent = NormalizeComparableLine(nameBandText)
                    .Contains(NormalizeComparableLine(expectedCustomerName), StringComparison.Ordinal);

                var inkBounds = DetectInkBounds(image, observationBox);

                var hasVisibleSignature = inkBounds.has_ink;
                var insideAllowedBox = false;

                if (hasVisibleSignature)
                {
                    var detectedRect = inkBounds.bounds;
                    var intersection = Intersect(detectedRect, allowedSignatureBox);

                    var detectedArea = detectedRect.Width * detectedRect.Height;
                    var intersectionArea = intersection.Width * intersection.Height;

                    var insideRatio = detectedArea <= 0
                        ? 0d
                        : (double)intersectionArea / detectedArea;

                    // yêu cầu chữ ký phải nằm chủ yếu trong ô cho phép
                    insideAllowedBox = insideRatio >= 0.85d;
                }

                return new SignatureAnalysisResult
                {
                    signature_name_present = signatureNamePresent,
                    has_visible_signature = hasVisibleSignature,
                    signature_inside_allowed_box = insideAllowedBox,
                    customer_panel = customerPanel,
                    allowed_signature_box = allowedSignatureBox,
                    observation_box = observationBox,
                    name_band = nameBand
                };
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                        Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                    // ignore cleanup error
                }
            }
        }

        private static (bool has_ink, Rectangle bounds) DetectInkBounds(
            Image<Rgba32> image,
            Rectangle area)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            long darkPixelCount = 0;

            int startX = Math.Max(0, area.X);
            int startY = Math.Max(0, area.Y);
            int endX = Math.Min(image.Width, area.X + area.Width);
            int endY = Math.Min(image.Height, area.Y + area.Height);

            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    var pixel = image[x, y];

                    var isDark =
                        pixel.A > 0 &&
                        (pixel.R < 220 || pixel.G < 220 || pixel.B < 220);

                    if (!isDark)
                        continue;

                    darkPixelCount++;

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (darkPixelCount < 120 ||
                minX == int.MaxValue ||
                minY == int.MaxValue ||
                maxX <= minX ||
                maxY <= minY)
            {
                return (false, Rectangle.Empty);
            }

            return (
                true,
                new Rectangle(
                    minX,
                    minY,
                    maxX - minX + 1,
                    maxY - minY + 1));
        }

        private static Rectangle Intersect(Rectangle a, Rectangle b)
        {
            var x1 = Math.Max(a.X, b.X);
            var y1 = Math.Max(a.Y, b.Y);
            var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

            if (x2 <= x1 || y2 <= y1)
                return Rectangle.Empty;

            return new Rectangle(x1, y1, x2 - x1, y2 - y1);
        }

        private static Rectangle ToRectangle(int width, int height, double xRatio, double yRatio, double wRatio, double hRatio)
        {
            return new Rectangle(
                x: (int)Math.Round(width * xRatio),
                y: (int)Math.Round(height * yRatio),
                width: Math.Max(1, (int)Math.Round(width * wRatio)),
                height: Math.Max(1, (int)Math.Round(height * hRatio)));
        }

        private static string ToRectString(Rectangle rect)
            => $"{rect.X},{rect.Y},{rect.Width},{rect.Height}";

        private static int GetPdfPageCount(byte[] pdfBytes)
        {
            using var ms = new MemoryStream(pdfBytes);
            using var pdf = PdfDocument.Open(ms);
            return pdf.NumberOfPages;
        }

        private async Task<string> OcrWholePdfAsync(byte[] pdfBytes, CancellationToken ct)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "contract-ocr", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var pdfPath = Path.Combine(tempRoot, "input.pdf");
                await File.WriteAllBytesAsync(pdfPath, pdfBytes, ct);

                var imagePrefix = Path.Combine(tempRoot, "page");

                await RunProcessAsync(
                    "pdftoppm",
                    $"-png \"{pdfPath}\" \"{imagePrefix}\"",
                    ct);

                var pages = Directory.GetFiles(tempRoot, "page-*.png")
                    .OrderBy(x => x)
                    .ToList();

                var sb = new StringBuilder();

                foreach (var page in pages)
                {
                    var ocrText = await OcrImageAsync(page, ct);
                    sb.AppendLine(ocrText);
                }

                return sb.ToString();
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                        Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                }
            }
        }

        private async Task<string> OcrImageAsync(string imagePath, CancellationToken ct)
        {
            return await RunProcessCaptureAsync(
                "tesseract",
                $"\"{imagePath}\" stdout -l vie+eng --psm 6",
                ct);
        }

        private async Task<byte[]> DownloadBytesAsync(string url, string label, CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Không tải được {label}: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            return await response.Content.ReadAsByteArrayAsync(ct);
        }

        private static double ComputeDiceSimilarity(string a, string b, int shingleWordSize)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return 0d;

            var setA = BuildWordShingles(a, shingleWordSize);
            var setB = BuildWordShingles(b, shingleWordSize);

            if (setA.Count == 0 || setB.Count == 0)
                return 0d;

            var intersect = setA.Intersect(setB).Count();
            return (2d * intersect) / (setA.Count + setB.Count);
        }

        private static HashSet<string> BuildWordShingles(string input, int n)
        {
            var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new HashSet<string>(StringComparer.Ordinal);

            if (words.Length == 0) return result;

            if (words.Length < n)
            {
                result.Add(string.Join(" ", words));
                return result;
            }

            for (int i = 0; i <= words.Length - n; i++)
            {
                result.Add(string.Join(" ", words.Skip(i).Take(n)));
            }

            return result;
        }

        private static async Task RunProcessAsync(
            string fileName,
            string arguments,
            CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            _ = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{fileName} chạy lỗi với mã {process.ExitCode}. Chi tiết: {error}");
            }
        }

        private static async Task<string> RunProcessCaptureAsync(
            string fileName,
            string arguments,
            CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{fileName} chạy lỗi với mã {process.ExitCode}. Chi tiết: {error}");
            }

            return output;
        }

        private sealed class SignatureAnalysisResult
        {
            public bool signature_name_present { get; set; }
            public bool has_visible_signature { get; set; }
            public bool signature_inside_allowed_box { get; set; }

            public Rectangle customer_panel { get; set; }
            public Rectangle allowed_signature_box { get; set; }
            public Rectangle observation_box { get; set; }
            public Rectangle name_band { get; set; }
        }
    }
}