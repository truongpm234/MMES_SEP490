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
            // 1) Extract text từ DOCX consultant
            var consultantRawText = ExtractDocxStructuredText(consultantDocxBytes);
            var expectedClauses = ExtractClauseMap(consultantRawText);

            if (expectedClauses.Count < 7)
            {
                throw new InvalidOperationException(
                    "Không trích xuất đủ nội dung từ Điều 1 đến Điều 7 trong hợp đồng gốc của tư vấn viên.");
            }

            // 2) Ưu tiên text layer từ PDF khách hàng
            var customerPdfRawText = ExtractPdfStructuredText(customerPdfBytes);
            var actualClauses = ExtractClauseMap(customerPdfRawText);

            bool usedOcrFallback = false;

            // Chỉ OCR nếu PDF text layer không extract đủ 7 điều
            if (actualClauses.Count < 7)
            {
                var customerOcrText = await OcrWholePdfAsync(customerPdfBytes, ct);
                actualClauses = ExtractClauseMap(customerOcrText);
                usedOcrFallback = true;
            }

            if (actualClauses.Count < 7)
            {
                throw new InvalidOperationException(
                    "Không trích xuất được đầy đủ nội dung từ Điều 1 đến Điều 7 trong file PDF khách hàng.");
            }

            // 3) So sánh theo từng điều
            var clauseDiffs = new List<ContractClauseDifferenceDto>();

            for (int i = 1; i <= 7; i++)
            {
                if (!expectedClauses.TryGetValue(i, out var expectedClause))
                {
                    clauseDiffs.Add(new ContractClauseDifferenceDto
                    {
                        clause_number = i,
                        clause_title = $"Điều {i}",
                        expected_text = "",
                        actual_text = "(Thiếu điều này trong hợp đồng gốc)"
                    });
                    continue;
                }

                if (!actualClauses.TryGetValue(i, out var actualClause))
                {
                    clauseDiffs.Add(new ContractClauseDifferenceDto
                    {
                        clause_number = i,
                        clause_title = expectedClause.Title,
                        expected_text = expectedClause.DisplayText,
                        actual_text = "(Không tìm thấy điều này trong file PDF khách hàng)"
                    });
                    continue;
                }

                if (!string.Equals(expectedClause.NormalizedText, actualClause.NormalizedText, StringComparison.Ordinal))
                {
                    clauseDiffs.Add(new ContractClauseDifferenceDto
                    {
                        clause_number = i,
                        clause_title = expectedClause.Title,
                        expected_text = expectedClause.DisplayText,
                        actual_text = actualClause.DisplayText
                    });
                }
            }

            var expectedAll = string.Join("\n", expectedClauses.OrderBy(x => x.Key).Select(x => x.Value.NormalizedText));
            var actualAll = string.Join("\n", actualClauses.OrderBy(x => x.Key).Select(x => x.Value.NormalizedText));

            var bodyExactMatch = clauseDiffs.Count == 0;
            var similarityPercent = Math.Round((decimal)(ComputeDiceSimilarity(expectedAll, actualAll, 2) * 100d), 2);

            // 4) Check chữ ký
            var signatureAnalysis = await AnalyzeCustomerSignatureRegionAsync(
                customerPdfBytes,
                expectedCustomerName,
                ct);

            var digitalSignature = await _pdfDigitalSignatureValidator.ValidateAsync(customerPdfBytes, ct);

            var visibleSignatureAccepted =
                signatureAnalysis.has_visible_signature &&
                signatureAnalysis.signature_inside_allowed_box;

            var signatureAccepted = digitalSignature.is_valid || visibleSignatureAccepted;

            var isMatch = bodyExactMatch && signatureAccepted;

            var message = BuildUserMessage(
                bodyExactMatch,
                clauseDiffs,
                signatureAnalysis,
                digitalSignature.is_valid);

            return new CompareContractResponse
            {
                request_id = requestId,
                estimate_id = estimateId,

                is_match = isMatch,

                body_text_exact_match = bodyExactMatch,
                similarity_percent = similarityPercent,

                signature_name_present = signatureAnalysis.signature_name_present,
                signature_mark_present = signatureAnalysis.has_visible_signature,
                signature_inside_allowed_box = signatureAnalysis.signature_inside_allowed_box,

                has_digital_signature = digitalSignature.has_signature,
                digital_signature_valid = digitalSignature.is_valid,

                used_ocr_fallback = usedOcrFallback,

                consultant_text_length = expectedAll.Length,
                customer_text_length = actualAll.Length,

                verification_mode = digitalSignature.is_valid
                    ? "ĐỐI CHIẾU CHẶT ĐIỀU 1-7 + CHỮ KÝ SỐ"
                    : "ĐỐI CHIẾU CHẶT ĐIỀU 1-7 + CHỮ KÝ TAY ĐÚNG Ô",

                message = message,
                reject_reason = isMatch ? null : message,

                debug_signature_box_rect = ToRectString(signatureAnalysis.allowed_signature_box),
                debug_signature_name_rect = ToRectString(signatureAnalysis.name_band),

                clause_differences = clauseDiffs
            };
        }

        private static string BuildUserMessage(
            bool bodyExactMatch,
            List<ContractClauseDifferenceDto> clauseDiffs,
            SignatureAnalysisResult signatureAnalysis,
            bool digitalSignatureValid)
        {
            if (bodyExactMatch &&
                (digitalSignatureValid ||
                 (signatureAnalysis.has_visible_signature && signatureAnalysis.signature_inside_allowed_box)))
            {
                return "Hợp đồng hợp lệ. Nội dung từ Điều 1 đến Điều 7 khớp với bản gốc và chữ ký hợp lệ.";
            }

            var messages = new List<string>();

            if (!bodyExactMatch)
            {
                var clauseList = clauseDiffs
                    .Select(x => $"Điều {x.clause_number}")
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                messages.Add($"Nội dung hợp đồng đã bị thay đổi tại: {string.Join(", ", clauseList)}.");
            }

            if (!digitalSignatureValid)
            {
                if (!signatureAnalysis.has_visible_signature)
                {
                    messages.Add("Không phát hiện chữ ký tay trong vùng ký của khách hàng.");
                }
                else if (!signatureAnalysis.signature_inside_allowed_box)
                {
                    messages.Add("Phát hiện chữ ký nhưng chữ ký nằm ngoài ô ký cho phép của khách hàng.");
                }
            }

            if (messages.Count == 0)
                messages.Add("Hợp đồng tải lên không vượt qua bước đối chiếu.");

            return string.Join(" ", messages);
        }

        private static Dictionary<int, ClauseContent> ExtractClauseMap(string rawText)
        {
            var logicalLines = BuildLogicalLines(rawText);
            var result = new Dictionary<int, ClauseContent>();

            int? currentClause = null;
            string currentTitle = "";
            var buffer = new List<string>();

            void FlushCurrent()
            {
                if (!currentClause.HasValue || buffer.Count == 0)
                    return;

                if (currentClause.Value < 1 || currentClause.Value > 7)
                    return;

                var originalText = string.Join("\n", buffer).Trim();

                result[currentClause.Value] = new ClauseContent
                {
                    ClauseNumber = currentClause.Value,
                    Title = string.IsNullOrWhiteSpace(currentTitle) ? $"Điều {currentClause.Value}" : currentTitle,
                    DisplayText = SanitizeForDisplay(originalText),
                    NormalizedText = NormalizeForCompare(originalText)
                };
            }

            foreach (var line in logicalLines)
            {
                if (IsSignatureMarker(line))
                    break;

                if (TryParseClauseHeader(line, out var clauseNo, out var clauseTitle))
                {
                    FlushCurrent();

                    currentClause = clauseNo;
                    currentTitle = clauseTitle;
                    buffer = new List<string> { line };
                    continue;
                }

                if (currentClause.HasValue)
                {
                    buffer.Add(line);
                }
            }

            FlushCurrent();
            return result;
        }

        private static List<string> BuildLogicalLines(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return new List<string>();

            var preLines = rawText
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Replace("\t", " ").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var expanded = new List<string>();

            foreach (var line in preLines)
            {
                foreach (var part in SplitSpecialLine(line))
                {
                    if (!string.IsNullOrWhiteSpace(part))
                        expanded.Add(part.Trim());
                }
            }

            var result = new List<string>();

            foreach (var line in expanded)
            {
                if (result.Count == 0)
                {
                    result.Add(line);
                    continue;
                }

                if (IsNewLogicalLine(line))
                {
                    result.Add(line);
                }
                else
                {
                    result[^1] = $"{result[^1]} {line}".Trim();
                }
            }

            return result;
        }

        private static IEnumerable<string> SplitSpecialLine(string line)
        {
            // Ví dụ PDF có thể ra:
            // "Điều 5: Điều khoản Thanh toán • Phương thức thanh toán: ..."
            var match = Regex.Match(
                line,
                @"^(Điều\s*[1-7]\s*:\s*[^•·●▪◦■]*)([•·●▪◦■].+)$",
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                yield return match.Groups[1].Value.Trim();
                yield return match.Groups[2].Value.Trim();
                yield break;
            }

            yield return line;
        }

        private static bool IsNewLogicalLine(string line)
        {
            if (TryParseClauseHeader(line, out _, out _))
                return true;

            if (Regex.IsMatch(line, @"^\s*([•·●▪◦■\-–—\*\+]|o)\s+", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(line, @"^\s*\d+(\.\d+)*[.)]\s+"))
                return true;

            var normalized = CanonicalMarker(line);

            string[] prefixes =
            {
                "ten san pham",
                "quy cach",
                "so luong",
                "tong gia tri don hang",
                "thue vat",
                "bang chu",
                "dia diem giao hang",
                "phuong thuc thanh toan",
                "tien do thanh toan",
                "dot 1",
                "dot 2",
                "chung tu",
                "bao goi",
                "thoi gian giao hang du kien",
                "giao nhan",
                "trong qua trinh thuc hien",
                "moi tranh chap phat sinh",
                "hop dong co hieu luc ke tu ngay ky",
                "hop dong nay duoc lap thanh",
            };

            return prefixes.Any(p => normalized.StartsWith(p));
        }

        private static bool TryParseClauseHeader(string line, out int clauseNo, out string clauseTitle)
        {
            clauseNo = 0;
            clauseTitle = "";

            var normalized = CanonicalMarker(line);
            var match = Regex.Match(normalized, @"^dieu\s*([1-7])\s*[:.\-]?\s*(.*)$", RegexOptions.IgnoreCase);

            if (!match.Success)
                return false;

            clauseNo = int.Parse(match.Groups[1].Value);

            var originalMatch = Regex.Match(line, @"^\s*(Điều\s*[1-7]\s*[:.\-]?\s*.*)$", RegexOptions.IgnoreCase);
            clauseTitle = originalMatch.Success
                ? NormalizeDisplayText(originalMatch.Groups[1].Value)
                : $"Điều {clauseNo}";

            return true;
        }

        private static bool IsSignatureMarker(string line)
        {
            var normalized = CanonicalMarker(line);
            return normalized.Contains("dai dien ben a");
        }

        private static string SanitizeForDisplay(string text)
        {
            var logicalLines = BuildLogicalLines(text);

            var cleaned = logicalLines
                .Select(NormalizeDisplayText)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            return string.Join(" ", cleaned);
        }

        private static string NormalizeForCompare(string text)
        {
            var logicalLines = BuildLogicalLines(text);

            var cleaned = logicalLines
                .Select(NormalizeDisplayText)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.ToLowerInvariant())
                .ToList();

            return string.Join("\n", cleaned);
        }

        private static string NormalizeDisplayText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var text = WebUtility.HtmlDecode(input);
            text = text.Normalize(NormalizationForm.FormKC);
            text = text.Replace('\u00A0', ' ');
            text = text.Replace("\t", " ");

            // bỏ bullet/numbering đầu dòng
            text = Regex.Replace(text, @"^\s*([•·●▪◦■\-–—\*\+]|o)\s+", "");
            text = Regex.Replace(text, @"^\s*\d+(\.\d+)*[.)]\s+", "");

            // chuẩn hóa khoảng trắng
            text = Regex.Replace(text, @"\s+", " ").Trim();

            // bỏ khoảng trắng thừa trước dấu câu
            text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");

            // chuẩn hóa khoảng trắng sau dấu câu
            text = Regex.Replace(text, @"([,.;:!?])(?=\S)", "$1 ");

            // chuẩn hóa lại lần cuối
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }

        private static string ExtractDocxStructuredText(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var doc = WordprocessingDocument.Open(ms, false);

            var lines = new List<string>();

            void ReadParagraphs(IEnumerable<Paragraph> paragraphs)
            {
                foreach (var p in paragraphs)
                {
                    var text = p.InnerText?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        lines.Add(text);
                }
            }

            if (doc.MainDocumentPart?.Document?.Body != null)
            {
                ReadParagraphs(doc.MainDocumentPart.Document.Body.Descendants<Paragraph>());
            }

            foreach (var headerPart in doc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
            {
                ReadParagraphs(headerPart.Header?.Descendants<Paragraph>() ?? Enumerable.Empty<Paragraph>());
            }

            foreach (var footerPart in doc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
            {
                ReadParagraphs(footerPart.Footer?.Descendants<Paragraph>() ?? Enumerable.Empty<Paragraph>());
            }

            return string.Join("\n", lines);
        }

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

                // Tinh chỉnh theo template hiện tại
                var customerPanel = ToRectangle(image.Width, image.Height, 0.04, 0.73, 0.42, 0.24);

                // Ô ký thật sự
                var allowedSignatureBox = ToRectangle(image.Width, image.Height, 0.09, 0.79, 0.24, 0.08);

                // Vùng quan sát lớn hơn nhẹ nhưng KHÔNG đè xuống vùng tên
                var observationBox = ToRectangle(image.Width, image.Height, 0.07, 0.78, 0.28, 0.09);

                // Dòng tên in sẵn bên dưới
                var nameBand = ToRectangle(image.Width, image.Height, 0.05, 0.89, 0.32, 0.05);

                var nameBandPath = Path.Combine(tempRoot, "signature-name-band.png");

                using (var nameCrop = image.Clone(ctx => ctx.Crop(nameBand)))
                {
                    await nameCrop.SaveAsPngAsync(nameBandPath, ct);
                }

                var nameBandText = await OcrImageAsync(nameBandPath, ct, pageSegMode: 7);

                var signatureNamePresent = NormalizeDisplayText(nameBandText).ToLowerInvariant()
                    .Contains(NormalizeDisplayText(expectedCustomerName).ToLowerInvariant(), StringComparison.Ordinal);

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

                    // nới nhẹ để tránh fail oan nếu ký hơi lệch nhưng vẫn nằm chủ yếu trong ô
                    insideAllowedBox = insideRatio >= 0.70d;
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

            // threshold thấp hơn một chút để bắt chữ ký mảnh
            if (darkPixelCount < 80 ||
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
                    var ocrText = await OcrImageAsync(page, ct, pageSegMode: 6);
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

        private async Task<string> OcrImageAsync(string imagePath, CancellationToken ct, int pageSegMode)
        {
            return await RunProcessCaptureAsync(
                "tesseract",
                $"\"{imagePath}\" stdout -l vie+eng --psm {pageSegMode}",
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
            var words = input.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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

        private sealed class ClauseContent
        {
            public int ClauseNumber { get; set; }
            public string Title { get; set; } = "";
            public string DisplayText { get; set; } = "";
            public string NormalizedText { get; set; } = "";
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