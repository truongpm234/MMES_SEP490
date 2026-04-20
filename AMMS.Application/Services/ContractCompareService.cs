using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
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

            var expectedBody = ExtractBodyWithoutSignatureZone(consultantText);
            var actualBody = ExtractBodyWithoutSignatureZone(customerText);

            // strict exact match: chỉ normalize nhẹ, không bỏ chữ/dấu câu quá mạnh
            var normalizedExpectedExact = NormalizeForExactBodyMatch(expectedBody);
            var normalizedActualExact = NormalizeForExactBodyMatch(actualBody);

            var bodyExactMatch = string.Equals(
                normalizedExpectedExact,
                normalizedActualExact,
                StringComparison.Ordinal);

            // similarity chỉ để debug/reference, không dùng để pass
            var normalizedExpectedSoft = NormalizeForSimilarity(expectedBody);
            var normalizedActualSoft = NormalizeForSimilarity(actualBody);

            var similarity = ComputeDiceSimilarity(
                normalizedExpectedSoft,
                normalizedActualSoft,
                2);

            var similarityPercent = Math.Round((decimal)(similarity * 100d), 2);

            var textDiffs = bodyExactMatch
                ? new List<ContractTextDiffItemDto>()
                : BuildLineDiff(normalizedExpectedExact, normalizedActualExact, 20);

            var signatureRegion = await AnalyzeCustomerSignatureRegionAsync(
                customerPdfBytes,
                expectedCustomerName,
                ct);

            var digitalSignature = await _pdfDigitalSignatureValidator.ValidateAsync(customerPdfBytes, ct);

            // strict:
            // - body phải exact match
            // - visible signature: phải nằm trong ô cho phép và không tràn ra ngoài
            // - hoặc có digital signature hợp lệ
            var visibleSignatureAccepted =
                signatureRegion.signature_mark_present &&
                signatureRegion.signature_inside_allowed_box &&
                !signatureRegion.signature_outside_allowed_box;

            var digitalSignatureAccepted = digitalSignature.is_valid;

            var isMatch = bodyExactMatch && (visibleSignatureAccepted || digitalSignatureAccepted);

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

                consultant_text_length = normalizedExpectedExact.Length,
                customer_text_length = normalizedActualExact.Length,

                verification_mode = digitalSignature.is_valid
                    ? "BODY_EXACT + VALID_DIGITAL_SIGNATURE"
                    : "BODY_EXACT + VISIBLE_SIGNATURE_IN_ALLOWED_BOX_ONLY",

                reject_reason = BuildRejectReason(
                    bodyExactMatch,
                    similarityPercent,
                    signatureRegion.signature_name_present,
                    signatureRegion.signature_mark_present,
                    signatureRegion.signature_inside_allowed_box,
                    signatureRegion.signature_outside_allowed_box,
                    digitalSignature.is_valid),

                text_differences = textDiffs
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
            if (bodyExactMatch && ((signatureMarkPresent && signatureInsideAllowedBox && !signatureOutsideAllowedBox) || digitalSignatureValid))
                return null;

            var reasons = new List<string>();

            if (!bodyExactMatch)
            {
                reasons.Add(
                    $"Contract body text was changed. Similarity reference = {similarityPercent:0.##}%.");
            }

            if (!digitalSignatureValid)
            {
                if (!signatureMarkPresent)
                {
                    reasons.Add("No visible signature mark was found in the customer signature area.");
                }
                else
                {
                    if (!signatureInsideAllowedBox)
                        reasons.Add("Signature was not found inside the allowed signature box.");

                    if (signatureOutsideAllowedBox)
                        reasons.Add("Signature contains marks outside the allowed signature box.");
                }
            }

            // Chỉ để debug, không dùng làm điều kiện fail cứng
            if (!signatureNamePresent)
            {
                reasons.Add("Warning: customer name was not clearly detected in the expected name band.");
            }

            return string.Join(" ", reasons);
        }

        private static bool ShouldUseOcrFallback(string consultantText, string customerPdfText)
        {
            var a = NormalizeForSimilarity(consultantText);
            var b = NormalizeForSimilarity(customerPdfText);

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

        // exact match pháp lý: normalize nhẹ
        private static string NormalizeForExactBodyMatch(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            input = WebUtility.HtmlDecode(input);
            input = input.Normalize(NormalizationForm.FormKC);

            input = input.Replace("\r\n", "\n").Replace("\r", "\n");

            var lines = input
                .Split('\n')
                .Select(x => Regex.Replace(x, @"[ \t]+", " ").TrimEnd())
                .ToList();

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);

            return string.Join("\n", lines).Trim();
        }

        // soft similarity: chỉ để debug
        private static string NormalizeForSimilarity(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            input = WebUtility.HtmlDecode(input);
            input = input.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            input = Regex.Replace(input, @"\s+", " ").Trim();
            input = Regex.Replace(input, @"[^\p{L}\p{Nd}\s]", " ");
            input = Regex.Replace(input, @"\s+", " ").Trim();

            return input;
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

        private static List<ContractTextDiffItemDto> BuildLineDiff(string expected, string actual, int maxItems = 20)
        {
            var expectedLines = SplitNormalizedLines(expected);
            var actualLines = SplitNormalizedLines(actual);

            var result = new List<ContractTextDiffItemDto>();
            var max = Math.Max(expectedLines.Count, actualLines.Count);

            for (int i = 0; i < max; i++)
            {
                var e = i < expectedLines.Count ? expectedLines[i] : null;
                var a = i < actualLines.Count ? actualLines[i] : null;

                if (e == a)
                    continue;

                if (e == null && a != null)
                {
                    result.Add(new ContractTextDiffItemDto
                    {
                        type = "added",
                        expected_line = 0,
                        actual_line = i + 1,
                        expected_text = "",
                        actual_text = a
                    });
                }
                else if (e != null && a == null)
                {
                    result.Add(new ContractTextDiffItemDto
                    {
                        type = "removed",
                        expected_line = i + 1,
                        actual_line = 0,
                        expected_text = e,
                        actual_text = ""
                    });
                }
                else
                {
                    result.Add(new ContractTextDiffItemDto
                    {
                        type = "changed",
                        expected_line = i + 1,
                        actual_line = i + 1,
                        expected_text = e ?? "",
                        actual_text = a ?? ""
                    });
                }

                if (result.Count >= maxItems)
                    break;
            }

            return result;
        }

        private static List<string> SplitNormalizedLines(string input)
        {
            return input
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private async Task<(bool signature_name_present, bool signature_mark_present, bool signature_inside_allowed_box, bool signature_outside_allowed_box)>
            AnalyzeCustomerSignatureRegionAsync(
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
                    fileName: "pdftoppm",
                    arguments: $"-f {pageCount} -l {pageCount} -png \"{pdfPath}\" \"{imagePrefix}\"",
                    ct: ct);

                var renderedFile = Directory.GetFiles(tempRoot, "page-*.png").OrderBy(x => x).LastOrDefault();
                if (renderedFile == null)
                    return (false, false, false, false);

                using var image = Image.Load<Rgba32>(renderedFile);

                // Các box này đang căn theo template hiện tại của bạn
                // Nếu template đổi layout thì chỉnh lại ratio
                var allowedSignatureBox = ToRectangle(image.Width, image.Height, 0.10, 0.79, 0.22, 0.08);
                var nameBand = ToRectangle(image.Width, image.Height, 0.08, 0.90, 0.26, 0.04);

                // Các vùng cấm để phát hiện ký sai chỗ
                var forbiddenLeft = ToRectangle(image.Width, image.Height, 0.03, 0.79, 0.05, 0.08);
                var forbiddenRight = ToRectangle(image.Width, image.Height, 0.34, 0.79, 0.10, 0.08);
                var forbiddenAbove = ToRectangle(image.Width, image.Height, 0.10, 0.76, 0.22, 0.02);
                var forbiddenBelow = ToRectangle(image.Width, image.Height, 0.10, 0.88, 0.22, 0.02);

                var allowedSignaturePath = Path.Combine(tempRoot, "allowed-signature-box.png");
                var nameBandPath = Path.Combine(tempRoot, "signature-name-band.png");
                var forbiddenLeftPath = Path.Combine(tempRoot, "forbidden-left.png");
                var forbiddenRightPath = Path.Combine(tempRoot, "forbidden-right.png");
                var forbiddenAbovePath = Path.Combine(tempRoot, "forbidden-above.png");
                var forbiddenBelowPath = Path.Combine(tempRoot, "forbidden-below.png");

                await SaveCropAsync(image, allowedSignatureBox, allowedSignaturePath, ct);
                await SaveCropAsync(image, nameBand, nameBandPath, ct);
                await SaveCropAsync(image, forbiddenLeft, forbiddenLeftPath, ct);
                await SaveCropAsync(image, forbiddenRight, forbiddenRightPath, ct);
                await SaveCropAsync(image, forbiddenAbove, forbiddenAbovePath, ct);
                await SaveCropAsync(image, forbiddenBelow, forbiddenBelowPath, ct);

                string nameBandText = "";
                try
                {
                    nameBandText = await OcrImageAsync(nameBandPath, ct);
                }
                catch
                {
                    nameBandText = "";
                }

                var signatureNamePresent = NormalizeForSimilarity(nameBandText)
                    .Contains(NormalizeForSimilarity(expectedCustomerName), StringComparison.Ordinal);

                var insideRatio = GetDarkRatio(allowedSignaturePath);
                var leftRatio = GetDarkRatio(forbiddenLeftPath);
                var rightRatio = GetDarkRatio(forbiddenRightPath);
                var aboveRatio = GetDarkRatio(forbiddenAbovePath);
                var belowRatio = GetDarkRatio(forbiddenBelowPath);

                var signatureMarkPresent = insideRatio >= 0.0030d;
                var signatureInsideAllowedBox = signatureMarkPresent;

                var signatureOutsideAllowedBox =
                    leftRatio >= 0.0020d ||
                    rightRatio >= 0.0020d ||
                    aboveRatio >= 0.0020d ||
                    belowRatio >= 0.0020d;

                return (
                    signature_name_present: signatureNamePresent,
                    signature_mark_present: signatureMarkPresent,
                    signature_inside_allowed_box: signatureInsideAllowedBox,
                    signature_outside_allowed_box: signatureOutsideAllowedBox
                );
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

        private static Rectangle ToRectangle(int width, int height, double xRatio, double yRatio, double wRatio, double hRatio)
        {
            return new Rectangle(
                x: (int)Math.Round(width * xRatio),
                y: (int)Math.Round(height * yRatio),
                width: Math.Max(1, (int)Math.Round(width * wRatio)),
                height: Math.Max(1, (int)Math.Round(height * hRatio)));
        }

        private static async Task SaveCropAsync(
            Image<Rgba32> source,
            Rectangle rect,
            string outputPath,
            CancellationToken ct)
        {
            using var crop = source.Clone(x => x.Crop(rect));
            await crop.SaveAsPngAsync(outputPath, ct);
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

        private static double GetDarkRatio(string imagePath)
        {
            using var image = Image.Load<Rgba32>(imagePath);

            long darkPixelCount = 0;
            long totalPixelCount = (long)image.Width * image.Height;

            for (int y = 0; y < image.Height; y++)
            {
                var row = image.DangerousGetPixelRowMemory(y).Span;
                for (int x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];

                    var isDark =
                        pixel.A > 0 &&
                        (pixel.R < 220 || pixel.G < 220 || pixel.B < 220);

                    if (isDark)
                        darkPixelCount++;
                }
            }

            if (totalPixelCount <= 0)
                return 0d;

            return (double)darkPixelCount / totalPixelCount;
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

            var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stdErrTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var error = await stdErrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{fileName} failed with exit code {process.ExitCode}. Error: {error}");
            }

            await stdOutTask;
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
    }
}