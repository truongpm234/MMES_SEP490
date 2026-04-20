using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

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
            var consultantText = ExtractDocxText(consultantDocxBytes);
            var customerDirectText = ExtractPdfText(customerPdfBytes);

            var usedOcrFallback = ShouldUseOcrFallback(consultantText, customerDirectText);

            var customerText = usedOcrFallback
                ? await OcrWholePdfAsync(customerPdfBytes, ct)
                : customerDirectText;

            // 1) CHỈ lấy phạm vi từ Điều 1 đến hết Điều 7
            var expectedClauseText = ExtractClauseRangeFromDieu1ToDieu7(consultantText);
            var actualClauseText = ExtractClauseRangeFromDieu1ToDieu7(customerText);

            // 2) So sánh chặt chẽ trên toàn bộ text đã normalize
            //    - bỏ bullet đầu dòng
            //    - chuẩn hóa khoảng trắng
            //    - không dùng similarity để cho pass
            var normalizedExpected = NormalizeClauseTextForStrictCompare(expectedClauseText);
            var normalizedActual = NormalizeClauseTextForStrictCompare(actualClauseText);

            var bodyExactMatch = string.Equals(
                normalizedExpected,
                normalizedActual,
                StringComparison.Ordinal);

            // similarity chỉ để tham khảo/debug, KHÔNG dùng để pass
            var similarity = ComputeDiceSimilarity(normalizedExpected, normalizedActual, 2);
            var similarityPercent = Math.Round((decimal)(similarity * 100d), 2);

            // 3) Build diff theo dòng để trả ra FE
            var expectedLines = BuildComparableLines(expectedClauseText);
            var actualLines = BuildComparableLines(actualClauseText);
            var textDifferences = BuildTextDifferences(expectedLines, actualLines, 50);

            // 4) Check chữ ký
            var signatureAnalysis = await AnalyzeCustomerSignatureRegionAsync(
                customerPdfBytes,
                expectedCustomerName,
                ct);

            var digitalSignature = await _pdfDigitalSignatureValidator.ValidateAsync(customerPdfBytes, ct);

            // Hợp lệ nếu:
            // - Nội dung Điều 1 -> Điều 7 khớp đúng tuyệt đối
            // - Và có digital signature hợp lệ
            //   HOẶC có chữ ký tay nhìn thấy nằm đúng trong ô cho phép
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

                consultant_text_length = normalizedExpected.Length,
                customer_text_length = normalizedActual.Length,

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
                    reasons.Add("Phát hiện nét ký nhưng chữ ký nằm ngoài ô ký cho phép của khách hàng.");
                }
            }

            return string.Join(" ", reasons);
        }

        /// <summary>
        /// Chỉ cắt phần nội dung cần kiểm tra:
        /// từ "Điều 1" đến trước block ký "ĐẠI DIỆN BÊN A".
        /// </summary>
        private static string ExtractClauseRangeFromDieu1ToDieu7(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var normalized = input.Replace("\r\n", "\n").Replace('\r', '\n');

            var startIndex = FindIndexByRegex(normalized, @"(?im)^\s*điều\s*1\s*:");
            if (startIndex < 0)
                startIndex = FindIndexByRegex(normalized, @"(?i)\bđiều\s*1\b");

            if (startIndex < 0)
                throw new InvalidOperationException("Không tìm thấy điểm bắt đầu Điều 1 để đối chiếu hợp đồng.");

            var endIndex = FindIndexByRegex(normalized, @"(?im)^\s*đại diện bên a\b");
            if (endIndex < 0)
                endIndex = FindIndexByRegex(normalized, @"(?i)\bđại diện bên a\b");

            if (endIndex < 0 || endIndex <= startIndex)
                throw new InvalidOperationException("Không tìm thấy vùng chữ ký 'ĐẠI DIỆN BÊN A' để kết thúc phạm vi đối chiếu.");

            var segment = normalized.Substring(startIndex, endIndex - startIndex);

            if (!Regex.IsMatch(segment, @"(?i)\bđiều\s*7\b"))
                throw new InvalidOperationException("Không tìm thấy đầy đủ phạm vi từ Điều 1 đến Điều 7 trong hợp đồng để đối chiếu.");

            return segment.Trim();
        }

        private static int FindIndexByRegex(string input, string pattern)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            return match.Success ? match.Index : -1;
        }

        /// <summary>
        /// Normalize để so sánh chặt:
        /// - bỏ bullet ở đầu dòng
        /// - chuẩn hóa khoảng trắng
        /// - giữ nguyên từ/ngữ nghĩa để thêm 1 chữ cũng fail
        /// </summary>
        private static string NormalizeClauseTextForStrictCompare(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var text = WebUtility.HtmlDecode(input);
            text = text.Normalize(NormalizationForm.FormKC);
            text = text.Replace('\u00A0', ' ');

            // Bỏ bullet đầu dòng, không đụng vào chữ phía sau
            text = Regex.Replace(
                text,
                @"(?m)^\s*([•·●▪◦■\-–—\*\+]+\s*)+",
                "");

            // Chuẩn hóa khoảng trắng
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"\s*\n\s*", "\n");
            text = Regex.Replace(text, @"\n{2,}", "\n");

            // Hạ chữ thường để tránh fail oan do hoa/thường
            text = text.ToLowerInvariant().Trim();

            return text;
        }

        private static List<string> BuildComparableLines(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            var lines = input
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeComparableLine)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            return lines;
        }

        private static string NormalizeComparableLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return "";

            var text = WebUtility.HtmlDecode(line);
            text = text.Normalize(NormalizationForm.FormKC);
            text = text.Replace('\u00A0', ' ');

            // Bỏ bullet đầu dòng
            text = Regex.Replace(
                text,
                @"^\s*([•·●▪◦■\-–—\*\+]+\s*)+",
                "");

            text = Regex.Replace(text, @"\s+", " ").Trim();
            text = text.ToLowerInvariant();

            return text;
        }

        private static List<TextDifferenceItemDto> BuildTextDifferences(
            List<string> expectedLines,
            List<string> actualLines,
            int maxItems)
        {
            var diffs = new List<TextDifferenceItemDto>();

            var m = expectedLines.Count;
            var n = actualLines.Count;

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

            // Gộp added + removed liền nhau thành changed để FE đọc dễ hơn
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

                // Khung bên A (trái) ở cuối trang
                var customerPanel = ToRectangle(image.Width, image.Height, 0.04, 0.73, 0.42, 0.24);

                // Ô ký cho phép của khách hàng
                var allowedSignatureBox = ToRectangle(image.Width, image.Height, 0.08, 0.80, 0.28, 0.08);

                // Vùng quan sát chữ ký (lớn hơn ô cho phép một chút)
                var observationBox = ToRectangle(image.Width, image.Height, 0.04, 0.77, 0.40, 0.12);

                // Vùng tên in sẵn ở dưới chữ ký
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

            var startX = Math.Max(0, area.X);
            var startY = Math.Max(0, area.Y);
            var endX = Math.Min(image.Width, area.X + area.Width);
            var endY = Math.Min(image.Height, area.Y + area.Height);

            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    var pixel = image[x, y];

                    // threshold khá nhẹ để bắt nét bút
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

            // Nếu quá ít pixel tối thì coi như chưa có chữ ký
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

        private static bool ShouldUseOcrFallback(string consultantText, string customerPdfText)
        {
            var a = NormalizeClauseTextForStrictCompare(consultantText);
            var b = NormalizeClauseTextForStrictCompare(customerPdfText);

            if (string.IsNullOrWhiteSpace(b))
                return true;

            if (b.Length < Math.Max(200, a.Length / 3))
                return true;

            return false;
        }

        private static string ExtractDocxText(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var doc = WordprocessingDocument.Open(ms, false);

            var allTexts = new List<string>();

            var bodyTexts = doc.MainDocumentPart?
                .Document?
                .Descendants<Text>()
                .Select(x => x.Text)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList() ?? new List<string>();

            allTexts.AddRange(bodyTexts);

            foreach (var headerPart in doc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
            {
                allTexts.AddRange(headerPart.RootElement
                    .Descendants<Text>()
                    .Select(x => x.Text)
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            foreach (var footerPart in doc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
            {
                allTexts.AddRange(footerPart.RootElement
                    .Descendants<Text>()
                    .Select(x => x.Text)
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            return string.Join(" ", allTexts);
        }

        private static string ExtractPdfText(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var pdf = PdfDocument.Open(ms);

            var sb = new StringBuilder();

            foreach (var page in pdf.GetPages())
            {
                sb.Append(' ');
                sb.Append(page.Text);
            }

            return sb.ToString();
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