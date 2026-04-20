using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
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

            var normalizedExpectedBody = NormalizeStrict(expectedBody);
            var normalizedActualBody = NormalizeStrict(actualBody);

            var bodyExactMatch = string.Equals(
                normalizedExpectedBody,
                normalizedActualBody,
                StringComparison.Ordinal);

            var similarity = ComputeDiceSimilarity(
                normalizedExpectedBody,
                normalizedActualBody,
                2);

            var signatureRegion = await AnalyzeCustomerSignatureRegionAsync(
                customerPdfBytes,
                expectedCustomerName,
                ct);

            var digitalSignature = await _pdfDigitalSignatureValidator.ValidateAsync(customerPdfBytes, ct);

            var bodyAccepted = bodyExactMatch || similarity >= 0.95d;

            var signatureAccepted =
                signatureRegion.signature_mark_present || digitalSignature.is_valid;

            var isMatch = bodyAccepted && signatureAccepted;

            var similarityPercent = Math.Round((decimal)(similarity * 100d), 2);

            return new CompareContractResponse
            {
                request_id = requestId,
                estimate_id = estimateId,

                is_match = isMatch,

                body_text_exact_match = bodyExactMatch,
                similarity_percent = similarityPercent,

                signature_name_present = signatureRegion.signature_name_present,
                signature_mark_present = signatureRegion.signature_mark_present,

                has_digital_signature = digitalSignature.has_signature,
                digital_signature_valid = digitalSignature.is_valid,

                used_ocr_fallback = usedOcrFallback,

                consultant_text_length = normalizedExpectedBody.Length,
                customer_text_length = normalizedActualBody.Length,

                verification_mode = digitalSignature.is_valid
                    ? "BODY_SIMILARITY>=95 + SIGNATURE_AREA + DIGITAL_SIGNATURE"
                    : "BODY_SIMILARITY>=95 + SIGNATURE_AREA + VISIBLE_SIGNATURE",

                reject_reason = BuildRejectReason(
                    bodyAccepted,
                    bodyExactMatch,
                    similarityPercent,
                    signatureRegion.signature_name_present,
                    signatureRegion.signature_mark_present,
                    digitalSignature.is_valid)
            };
        }

        private static string? BuildRejectReason(
    bool bodyAccepted,
    bool bodyExactMatch,
    decimal similarityPercent,
    bool signatureNamePresent,
    bool signatureMarkPresent,
    bool digitalSignatureValid)
        {
            if (bodyAccepted && (signatureMarkPresent || digitalSignatureValid))
                return null;

            var reasons = new List<string>();

            if (!bodyAccepted)
            {
                reasons.Add(
                    $"Contract body text is not similar enough. Current similarity = {similarityPercent:0.##}%.");
            }

            if (!signatureMarkPresent && !digitalSignatureValid)
            {
                reasons.Add("No visible signature mark or valid digital signature was found.");
            }

            if (!signatureNamePresent)
            {
                reasons.Add("Customer name was not detected in the expected signature-name area.");
            }

            return string.Join(" ", reasons);
        }

        private static bool ShouldUseOcrFallback(string consultantText, string customerPdfText)
        {
            var a = NormalizeStrict(consultantText);
            var b = NormalizeStrict(customerPdfText);

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

            // Cắt từ phần tiêu đề chữ ký cuối hợp đồng
            var marker = "ĐẠI DIỆN BÊN A";
            var idx = input.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

            if (idx < 0)
                return input;

            return input.Substring(0, idx);
        }

        private static string NormalizeStrict(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            input = WebUtility.HtmlDecode(input);
            input = input.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

            // bỏ xuống dòng, khoảng trắng thừa
            input = Regex.Replace(input, @"\s+", " ").Trim();

            // bỏ ký tự trang trí, giữ chữ + số + khoảng trắng
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

        private async Task<(bool signature_name_present, bool signature_mark_present)> AnalyzeCustomerSignatureRegionAsync(
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
                    return (false, false);

                using var image = await Image.LoadAsync<Rgba32>(renderedFile, ct);


                var signatureBox = ToRectangle(image.Width, image.Height, 0.05, 0.74, 0.40, 0.16);
                var nameBand = ToRectangle(image.Width, image.Height, 0.05, 0.88, 0.40, 0.07);

                var signatureCropPath = Path.Combine(tempRoot, "signature-box.png");
                var nameBandPath = Path.Combine(tempRoot, "signature-name-band.png");

                using (var signatureCrop = image.Clone(x => x.Crop(signatureBox)))
                {
                    await signatureCrop.SaveAsPngAsync(signatureCropPath, ct);
                }

                using (var nameCrop = image.Clone(x => x.Crop(nameBand)))
                {
                    await nameCrop.SaveAsPngAsync(nameBandPath, ct);
                }

                var nameBandText = await OcrImageAsync(nameBandPath, ct);

                var signatureNamePresent = NormalizeStrict(nameBandText)
                    .Contains(NormalizeStrict(expectedCustomerName), StringComparison.Ordinal);

                var signatureMarkPresent = await HasVisibleSignatureMarkAsync(signatureCropPath, ct);

                return (signatureNamePresent, signatureMarkPresent);
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
                        Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                }
            }
        }

        private async Task<string> OcrImageAsync(string imagePath, CancellationToken ct)
        {
            var result = await RunProcessCaptureAsync(
                "tesseract",
                $"\"{imagePath}\" stdout -l vie+eng --psm 6",
                ct);

            return result;
        }

        private static Task<bool> HasVisibleSignatureMarkAsync(string imagePath, CancellationToken ct)
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

            var darkRatio = totalPixelCount == 0
                ? 0d
                : (double)darkPixelCount / totalPixelCount;

            return Task.FromResult(darkRatio >= 0.002d);
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

            var stdOut = process.StandardOutput.ReadToEndAsync(ct);
            var stdErr = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var error = await stdErr;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{fileName} failed with exit code {process.ExitCode}. Error: {error}");
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
    }
}