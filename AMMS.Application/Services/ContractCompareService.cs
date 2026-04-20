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

        // Bật = giữ lại ảnh debug OCR/signature crop để tự mở xem
        private static readonly bool KeepDebugFiles =
            string.Equals(
                Environment.GetEnvironmentVariable("CONTRACT_COMPARE_KEEP_DEBUG_FILES"),
                "true",
                StringComparison.OrdinalIgnoreCase);

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
            var consultantBytes = await DownloadBytesAsync(consultantDocxUrl, "consultant contract", ct);
            var customerPdfBytes = await DownloadBytesAsync(customerPdfUrl, "customer signed contract", ct);

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
            var consultantBytes = await DownloadBytesAsync(consultantDocxUrl, "consultant contract", ct);

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

            // Cắt bỏ vùng ký cuối hợp đồng để chữ ký không làm sai body
            var expectedBodyRaw = ExtractBodyWithoutSignatureZone(consultantText);
            var actualBodyRaw = ExtractBodyWithoutSignatureZone(customerText);

            // Canonicalize để bỏ numbering, khoảng trắng, dấu câu render khác
            var expectedBodyCanonical = CanonicalizeContractBody(expectedBodyRaw);
            var actualBodyCanonical = CanonicalizeContractBody(actualBodyRaw);

            var bodyExactMatch = string.Equals(
                expectedBodyCanonical,
                actualBodyCanonical,
                StringComparison.Ordinal);

            var similarity = ComputeDiceSimilarity(
                expectedBodyCanonical,
                actualBodyCanonical,
                2);

            var similarityPercent = Math.Round((decimal)(similarity * 100d), 2);

            var textDifferences = bodyExactMatch
                ? new List<TextDifferenceItemDto>()
                : BuildTextDifferences(expectedBodyRaw, actualBodyRaw, 10);

            var signatureRegion = await AnalyzeCustomerSignatureRegionAsync(
                customerPdfBytes,
                expectedCustomerName,
                ct);

            var digitalSignature = await _pdfDigitalSignatureValidator.ValidateAsync(customerPdfBytes, ct);

            // Rule mới:
            // 1) Body phải exact sau canonicalization
            // 2) Có chữ ký nhìn thấy trong ô HOẶC digital signature valid
            // 3) Không được ký lệch quá nhiều ra ngoài vùng cho phép
            var signatureAccepted =
                (signatureRegion.signature_mark_present || digitalSignature.is_valid)
                && signatureRegion.signature_inside_allowed_box
                && !signatureRegion.signature_outside_allowed_box;

            var isMatch = bodyExactMatch && signatureAccepted;

            return new CompareContractResponse
            {
                request_id = requestId,
                estimate_id = estimateId,

                is_match = isMatch,

                body_text_exact_match = bodyExactMatch,
                similarity_percent = similarityPercent,

                signature_name_present = signatureRegion.signature_name_present,
                signature_mark_present = signatureRegion.signature_mark_present,
                signature_inside_allowed_box = signatureRegion.signature_inside_allowed_box,
                signature_outside_allowed_box = signatureRegion.signature_outside_allowed_box,

                has_digital_signature = digitalSignature.has_signature,
                digital_signature_valid = digitalSignature.is_valid,

                used_ocr_fallback = usedOcrFallback,

                consultant_text_length = expectedBodyCanonical.Length,
                customer_text_length = actualBodyCanonical.Length,

                verification_mode = digitalSignature.is_valid
                    ? "BODY_CANONICAL_EXACT + DIGITAL_SIGNATURE_OR_VISIBLE_SIGNATURE_IN_BOX"
                    : "BODY_CANONICAL_EXACT + VISIBLE_SIGNATURE_IN_BOX_ONLY",

                reject_reason = BuildRejectReason(
                    bodyExactMatch,
                    similarityPercent,
                    signatureRegion.signature_name_present,
                    signatureRegion.signature_mark_present,
                    signatureRegion.signature_inside_allowed_box,
                    signatureRegion.signature_outside_allowed_box,
                    digitalSignature.is_valid),

                ocr_name_band_text = signatureRegion.ocr_name_band_text,
                signature_debug = signatureRegion.debug_info,

                text_differences = textDifferences
            };
        }

        private static string? BuildRejectReason(
            bool bodyExactMatch,
            decimal similarityPercent,
            bool signatureNamePresent,
            bool signatureMarkPresent,
            bool signatureInsideAllowedBox,
            bool signatureOutsideAllowedBox,
            bool digitalSignatureValid)
        {
            if (bodyExactMatch &&
                ((signatureMarkPresent && signatureInsideAllowedBox && !signatureOutsideAllowedBox) || digitalSignatureValid))
            {
                return null;
            }

            var reasons = new List<string>();

            if (!bodyExactMatch)
            {
                reasons.Add($"Contract body text was changed. Similarity reference = {similarityPercent:0.##}%.");
            }

            if (!signatureMarkPresent && !digitalSignatureValid)
            {
                reasons.Add("No visible signature mark or valid digital signature was found.");
            }

            if (signatureMarkPresent && !signatureInsideAllowedBox)
            {
                reasons.Add("Signature was not placed inside the allowed signature box.");
            }

            if (signatureOutsideAllowedBox)
            {
                reasons.Add("Signature contains marks outside the allowed signature box.");
            }

            // Chỉ warning, không chặn nếu body đúng và ký đúng box
            if (!signatureNamePresent)
            {
                reasons.Add("Warning: customer name was not clearly detected in the expected name band.");
            }

            return string.Join(" ", reasons);
        }

        private static bool ShouldUseOcrFallback(string consultantText, string customerPdfText)
        {
            var a = CanonicalizeContractBody(consultantText);
            var b = CanonicalizeContractBody(customerPdfText);

            if (string.IsNullOrWhiteSpace(b))
                return true;

            if (b.Length < Math.Max(200, a.Length / 3))
                return true;

            return false;
        }

        private async Task<byte[]> DownloadBytesAsync(string url, string label, CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Cannot download {label}: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            return await response.Content.ReadAsByteArrayAsync(ct);
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

        private static string ExtractBodyWithoutSignatureZone(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var marker = "ĐẠI DIỆN BÊN A";
            var idx = input.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

            if (idx < 0)
                return input;

            return input.Substring(0, idx);
        }

        // Đây là chỗ quan trọng nhất để tránh fail oan vì numbering của Word/PDF
        private static string CanonicalizeContractBody(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            input = WebUtility.HtmlDecode(input);
            input = input.Normalize(NormalizationForm.FormKC);

            // chuẩn newline trước
            input = input.Replace("\r\n", "\n").Replace('\r', '\n');

            // bỏ numbering đầu dòng: 1. , 2.1. , 3)
            input = Regex.Replace(
                input,
                @"(?m)^\s*(\d+(\.\d+)*[\.\)]\s+)",
                "",
                RegexOptions.CultureInvariant);

            // bỏ numbering giữa các đoạn do PDF render thành 1 dòng dài
            input = Regex.Replace(
                input,
                @"(?<=\s)(\d+(\.\d+)*[\.\)])\s+(?=\p{L})",
                "",
                RegexOptions.CultureInvariant);

            input = input.ToLowerInvariant();

            // thay punctuation thành space để không fail vì "thi hành ." và "thi hành."
            input = Regex.Replace(input, @"[^\p{L}\p{Nd}]+", " ");

            input = Regex.Replace(input, @"\s+", " ").Trim();

            return input;
        }

        private static List<TextDifferenceItemDto> BuildTextDifferences(
            string expectedRaw,
            string actualRaw,
            int maxItems)
        {
            var expectedUnits = SplitComparableUnits(expectedRaw);
            var actualUnits = SplitComparableUnits(actualRaw);

            var result = new List<TextDifferenceItemDto>();
            var max = Math.Max(expectedUnits.Count, actualUnits.Count);

            for (int i = 0; i < max && result.Count < maxItems; i++)
            {
                var expected = i < expectedUnits.Count ? expectedUnits[i] : "";
                var actual = i < actualUnits.Count ? actualUnits[i] : "";

                var expectedCanonical = CanonicalizeContractBody(expected);
                var actualCanonical = CanonicalizeContractBody(actual);

                if (string.Equals(expectedCanonical, actualCanonical, StringComparison.Ordinal))
                    continue;

                result.Add(new TextDifferenceItemDto
                {
                    type = string.IsNullOrWhiteSpace(expected)
                        ? "added"
                        : string.IsNullOrWhiteSpace(actual)
                            ? "removed"
                            : "changed",
                    expected_line = i + 1,
                    actual_line = i + 1,
                    expected_text = expected,
                    actual_text = actual
                });
            }

            return result;
        }

        private static List<string> SplitComparableUnits(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            var text = input.Replace("\r\n", "\n").Replace('\r', '\n');

            // ưu tiên tách theo Điều
            text = Regex.Replace(text, @"(?i)\b(điều\s+\d+[:])", "\n$1");

            // tách theo xuống dòng
            var rawLines = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var result = new List<string>();

            foreach (var line in rawLines)
            {
                // nếu line quá dài, tách thêm theo câu
                if (line.Length > 250)
                {
                    var sentences = Regex.Split(line, @"(?<=[\.\!\?;:])\s+")
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x));

                    result.AddRange(sentences);
                }
                else
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private async Task<SignatureAnalysisResult> AnalyzeCustomerSignatureRegionAsync(
            byte[] pdfBytes,
            string expectedCustomerName,
            CancellationToken ct)
        {
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "contract-sign-check",
                Guid.NewGuid().ToString("N"));

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
                    return SignatureAnalysisResult.Empty();

                using var image = Image.Load<Rgba32>(renderedFile);

                // ===== Bạn chỉnh 4 vùng này khi debug =====
                // customerPanel: vùng tổng bên A cuối trang
                var customerPanel = ToRectangle(image.Width, image.Height, 0.04, 0.70, 0.42, 0.25);

                // allowedSignatureBox: ô ký chuẩn khách hàng
                var allowedSignatureBox = ToRectangle(image.Width, image.Height, 0.07, 0.75, 0.34, 0.11);

                // observationBox: box lớn hơn 1 chút để xem ký có tràn ra ngoài không
                var observationBox = ToRectangle(image.Width, image.Height, 0.05, 0.73, 0.38, 0.15);

                // nameBand: dòng họ tên in sẵn dưới chỗ ký
                var nameBand = ToRectangle(image.Width, image.Height, 0.07, 0.87, 0.34, 0.05);

                var observationCropPath = Path.Combine(tempRoot, "observation-box.png");
                var nameBandPath = Path.Combine(tempRoot, "name-band.png");
                var renderedPagePath = Path.Combine(tempRoot, "full-last-page.png");

                image.Save(renderedPagePath);

                using (var observationCrop = image.Clone(x => x.Crop(observationBox)))
                {
                    observationCrop.Save(observationCropPath);
                }

                using (var nameCrop = image.Clone(x => x.Crop(nameBand)))
                {
                    nameCrop.Save(nameBandPath);
                }

                var nameBandText = await OcrImageAsync(nameBandPath, ct);
                var signatureNamePresent =
                    CanonicalizeContractBody(nameBandText)
                        .Contains(CanonicalizeContractBody(expectedCustomerName), StringComparison.Ordinal);

                var mark = DetectSignatureMark(image, observationBox, allowedSignatureBox);

                var debugInfo = new SignatureDebugInfoDto
                {
                    customer_panel = ToRectDto(customerPanel),
                    allowed_signature_box = ToRectDto(allowedSignatureBox),
                    observation_box = ToRectDto(observationBox),
                    name_band = ToRectDto(nameBand),
                    detected_signature_bounds = ToRectDto(mark.detectedBounds),
                    inside_ratio = mark.insideRatio,
                    outside_ratio = mark.outsideRatio,
                    debug_root = tempRoot,
                    rendered_page_path = renderedPagePath,
                    observation_crop_path = observationCropPath,
                    name_band_crop_path = nameBandPath
                };

                if (!KeepDebugFiles)
                {
                    debugInfo.debug_root = null;
                    debugInfo.rendered_page_path = null;
                    debugInfo.observation_crop_path = null;
                    debugInfo.name_band_crop_path = null;
                }

                return new SignatureAnalysisResult
                {
                    signature_name_present = signatureNamePresent,
                    signature_mark_present = mark.signatureMarkPresent,
                    signature_inside_allowed_box = mark.signatureInsideAllowedBox,
                    signature_outside_allowed_box = mark.signatureOutsideAllowedBox,
                    ocr_name_band_text = nameBandText,
                    debug_info = debugInfo
                };
            }
            finally
            {
                if (!KeepDebugFiles)
                {
                    try
                    {
                        if (Directory.Exists(tempRoot))
                            Directory.Delete(tempRoot, true);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static SignatureMarkDetectionResult DetectSignatureMark(
            Image<Rgba32> image,
            Rectangle observationBox,
            Rectangle allowedSignatureBox)
        {
            var darkPoints = new List<Point>();

            for (int y = observationBox.Top; y < observationBox.Bottom; y++)
            {
                for (int x = observationBox.Left; x < observationBox.Right; x++)
                {
                    var p = image[x, y];

                    // threshold đơn giản, đủ dùng cho scan/PDF ký tay
                    var gray = (p.R + p.G + p.B) / 3.0;
                    var isDark = p.A > 0 && gray < 210;

                    if (isDark)
                        darkPoints.Add(new Point(x, y));
                }
            }

            if (darkPoints.Count == 0)
            {
                return SignatureMarkDetectionResult.Empty();
            }

            // bbox của toàn bộ mark
            var minX = darkPoints.Min(p => p.X);
            var minY = darkPoints.Min(p => p.Y);
            var maxX = darkPoints.Max(p => p.X);
            var maxY = darkPoints.Max(p => p.Y);

            var detectedBounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);

            var insideCount = 0;
            foreach (var pt in darkPoints)
            {
                if (allowedSignatureBox.Contains(pt))
                    insideCount++;
            }

            var total = darkPoints.Count;
            var outsideCount = total - insideCount;

            var insideRatio = total == 0 ? 0d : (double)insideCount / total;
            var outsideRatio = total == 0 ? 0d : (double)outsideCount / total;

            // Tâm bbox phải nằm trong allowed box
            var centerX = detectedBounds.Left + detectedBounds.Width / 2;
            var centerY = detectedBounds.Top + detectedBounds.Height / 2;
            var centerInside = allowedSignatureBox.Contains(centerX, centerY);

            // signatureMarkPresent: chỉ cần có lượng pixel tối đủ lớn
            var boxArea = observationBox.Width * observationBox.Height;
            var darkRatio = boxArea == 0 ? 0d : (double)total / boxArea;
            var signatureMarkPresent = darkRatio >= 0.002d;

            // Cho phép tràn nhẹ, nhưng không cho lệch nhiều
            var signatureInsideAllowedBox =
                signatureMarkPresent &&
                centerInside &&
                insideRatio >= 0.60d;

            var signatureOutsideAllowedBox =
                signatureMarkPresent &&
                outsideRatio > 0.30d;

            return new SignatureMarkDetectionResult
            {
                signatureMarkPresent = signatureMarkPresent,
                signatureInsideAllowedBox = signatureInsideAllowedBox,
                signatureOutsideAllowedBox = signatureOutsideAllowedBox,
                insideRatio = insideRatio,
                outsideRatio = outsideRatio,
                detectedBounds = detectedBounds
            };
        }

        private static RectDto ToRectDto(Rectangle rect)
        {
            return new RectDto
            {
                x = rect.X,
                y = rect.Y,
                width = rect.Width,
                height = rect.Height
            };
        }

        private static Rectangle ToRectangle(int width, int height, double xRatio, double yRatio, double wRatio, double hRatio)
        {
            return new Rectangle(
                x: (int)Math.Round(width * xRatio),
                y: (int)Math.Round(height * yRatio),
                width: Math.Max(1, (int)Math.Round(width * wRatio)),
                height: Math.Max(1, (int)Math.Round(height * hRatio)));
        }

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
                        Directory.Delete(tempRoot, true);
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

            var stdErrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdErr = await stdErrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{fileName} failed with exit code {process.ExitCode}. Error: {stdErr}");
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
                    $"{fileName} failed with exit code {process.ExitCode}. Error: {error}");
            }

            return output;
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

        private sealed class SignatureAnalysisResult
        {
            public bool signature_name_present { get; set; }
            public bool signature_mark_present { get; set; }
            public bool signature_inside_allowed_box { get; set; }
            public bool signature_outside_allowed_box { get; set; }
            public string? ocr_name_band_text { get; set; }
            public SignatureDebugInfoDto? debug_info { get; set; }

            public static SignatureAnalysisResult Empty()
            {
                return new SignatureAnalysisResult
                {
                    signature_name_present = false,
                    signature_mark_present = false,
                    signature_inside_allowed_box = false,
                    signature_outside_allowed_box = false
                };
            }
        }

        private sealed class SignatureMarkDetectionResult
        {
            public bool signatureMarkPresent { get; set; }
            public bool signatureInsideAllowedBox { get; set; }
            public bool signatureOutsideAllowedBox { get; set; }
            public double insideRatio { get; set; }
            public double outsideRatio { get; set; }
            public Rectangle detectedBounds { get; set; }

            public static SignatureMarkDetectionResult Empty()
            {
                return new SignatureMarkDetectionResult
                {
                    signatureMarkPresent = false,
                    signatureInsideAllowedBox = false,
                    signatureOutsideAllowedBox = false,
                    insideRatio = 0,
                    outsideRatio = 0,
                    detectedBounds = new Rectangle(0, 0, 0, 0)
                };
            }
        }
    }
}