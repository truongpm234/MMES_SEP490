using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly ILogger<ContractCompareService> _logger;

        public ContractCompareService(
            HttpClient httpClient,
            IPdfDigitalSignatureValidator pdfDigitalSignatureValidator,
            IWebHostEnvironment env,
            IConfiguration config,
            ILogger<ContractCompareService> logger)
        {
            _httpClient = httpClient;
            _pdfDigitalSignatureValidator = pdfDigitalSignatureValidator;
            _env = env;
            _config = config;
            _logger = logger;
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
                requestId,
                estimateId,
                customerPdfBytes,
                expectedCustomerName,
                ct);

            var digitalSignature = await _pdfDigitalSignatureValidator.ValidateAsync(customerPdfBytes, ct);

            var bodyAccepted = bodyExactMatch || similarity >= 0.95d;
            var signatureAccepted =
                signatureRegion.signature_mark_present || digitalSignature.is_valid;

            var isMatch = bodyAccepted && signatureAccepted;
            var similarityPercent = Math.Round((decimal)(similarity * 100d), 2);

            var debugUrls = await SavePublicDebugFilesAsync(
                requestId,
                estimateId,
                expectedBody,
                actualBody,
                signatureRegion,
                ct);

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
                    similarityPercent,
                    signatureRegion.signature_mark_present,
                    digitalSignature.is_valid,
                    signatureRegion.signature_name_present),

                debug_folder_url = debugUrls.debug_folder_url,
                debug_last_page_url = debugUrls.debug_last_page_url,
                debug_signature_box_url = debugUrls.debug_signature_box_url,
                debug_signature_name_band_url = debugUrls.debug_signature_name_band_url,
                debug_signature_name_ocr_url = debugUrls.debug_signature_name_ocr_url,
                debug_body_expected_url = debugUrls.debug_body_expected_url,
                debug_body_actual_url = debugUrls.debug_body_actual_url,

                debug_signature_box_rect = signatureRegion.signature_box_rect,
                debug_signature_name_rect = signatureRegion.signature_name_rect
            };
        }

        private static string? BuildRejectReason(
            bool bodyAccepted,
            decimal similarityPercent,
            bool signatureMarkPresent,
            bool digitalSignatureValid,
            bool signatureNamePresent)
        {
            if (bodyAccepted && (signatureMarkPresent || digitalSignatureValid))
                return null;

            var reasons = new List<string>();

            if (!bodyAccepted)
            {
                reasons.Add($"Contract body text is not similar enough. Current similarity = {similarityPercent:0.##}%.");
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

        private async Task<SignatureRegionDebugResult> AnalyzeCustomerSignatureRegionAsync(
            int requestId,
            int estimateId,
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
                {
                    return new SignatureRegionDebugResult();
                }

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

                return new SignatureRegionDebugResult
                {
                    signature_name_present = signatureNamePresent,
                    signature_mark_present = signatureMarkPresent,
                    rendered_last_page_local_path = renderedFile,
                    signature_box_local_path = signatureCropPath,
                    signature_name_band_local_path = nameBandPath,
                    signature_name_ocr_text = nameBandText,
                    signature_box_rect = $"{signatureBox.X},{signatureBox.Y},{signatureBox.Width},{signatureBox.Height}",
                    signature_name_rect = $"{nameBand.X},{nameBand.Y},{nameBand.Width},{nameBand.Height}"
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

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);

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
            });

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

            _ = process.StandardOutput.ReadToEndAsync(ct);
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

        private async Task<PublicDebugUrls> SavePublicDebugFilesAsync(
            int requestId,
            int estimateId,
            string expectedBody,
            string actualBody,
            SignatureRegionDebugResult signatureRegion,
            CancellationToken ct)
        {
            var keep = _config.GetValue<bool>("DebugFiles:KeepPublicDebugFiles");
            if (!keep)
                return new PublicDebugUrls();

            var publicBaseUrl = (_config["DebugFiles:PublicBaseUrl"] ?? "").TrimEnd('/');
            var publicFolder = (_config["DebugFiles:PublicFolder"] ?? "debug/contracts").Trim('/');

            var webRoot = _env.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            Directory.CreateDirectory(webRoot);

            var runId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var relativeFolder = $"{publicFolder}/{requestId}/{estimateId}/{runId}";
            var absoluteFolder = Path.Combine(webRoot, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(absoluteFolder);

            string? lastPageUrl = null;
            string? signatureBoxUrl = null;
            string? signatureNameBandUrl = null;
            string? signatureNameOcrUrl = null;
            string? expectedBodyUrl = null;
            string? actualBodyUrl = null;

            if (!string.IsNullOrWhiteSpace(signatureRegion.rendered_last_page_local_path) &&
                File.Exists(signatureRegion.rendered_last_page_local_path))
            {
                var dest = Path.Combine(absoluteFolder, "last-page.png");
                File.Copy(signatureRegion.rendered_last_page_local_path, dest, true);
                lastPageUrl = BuildPublicUrl(publicBaseUrl, relativeFolder, "last-page.png");
            }

            if (!string.IsNullOrWhiteSpace(signatureRegion.signature_box_local_path) &&
                File.Exists(signatureRegion.signature_box_local_path))
            {
                var dest = Path.Combine(absoluteFolder, "signature-box.png");
                File.Copy(signatureRegion.signature_box_local_path, dest, true);
                signatureBoxUrl = BuildPublicUrl(publicBaseUrl, relativeFolder, "signature-box.png");
            }

            if (!string.IsNullOrWhiteSpace(signatureRegion.signature_name_band_local_path) &&
                File.Exists(signatureRegion.signature_name_band_local_path))
            {
                var dest = Path.Combine(absoluteFolder, "signature-name-band.png");
                File.Copy(signatureRegion.signature_name_band_local_path, dest, true);
                signatureNameBandUrl = BuildPublicUrl(publicBaseUrl, relativeFolder, "signature-name-band.png");
            }

            var ocrPath = Path.Combine(absoluteFolder, "signature-name-ocr.txt");
            await File.WriteAllTextAsync(ocrPath, signatureRegion.signature_name_ocr_text ?? "", ct);
            signatureNameOcrUrl = BuildPublicUrl(publicBaseUrl, relativeFolder, "signature-name-ocr.txt");

            var expectedBodyPath = Path.Combine(absoluteFolder, "expected-body.txt");
            await File.WriteAllTextAsync(expectedBodyPath, expectedBody ?? "", ct);
            expectedBodyUrl = BuildPublicUrl(publicBaseUrl, relativeFolder, "expected-body.txt");

            var actualBodyPath = Path.Combine(absoluteFolder, "actual-body.txt");
            await File.WriteAllTextAsync(actualBodyPath, actualBody ?? "", ct);
            actualBodyUrl = BuildPublicUrl(publicBaseUrl, relativeFolder, "actual-body.txt");

            return new PublicDebugUrls
            {
                debug_folder_url = BuildFolderUrl(publicBaseUrl, relativeFolder),
                debug_last_page_url = lastPageUrl,
                debug_signature_box_url = signatureBoxUrl,
                debug_signature_name_band_url = signatureNameBandUrl,
                debug_signature_name_ocr_url = signatureNameOcrUrl,
                debug_body_expected_url = expectedBodyUrl,
                debug_body_actual_url = actualBodyUrl
            };
        }

        private static string BuildPublicUrl(string publicBaseUrl, string relativeFolder, string fileName)
        {
            return $"{publicBaseUrl}/{relativeFolder.Trim('/')}/{fileName}";
        }

        private static string BuildFolderUrl(string publicBaseUrl, string relativeFolder)
        {
            return $"{publicBaseUrl}/{relativeFolder.Trim('/')}/";
        }

        private sealed class SignatureRegionDebugResult
        {
            public bool signature_name_present { get; set; }
            public bool signature_mark_present { get; set; }

            public string? rendered_last_page_local_path { get; set; }
            public string? signature_box_local_path { get; set; }
            public string? signature_name_band_local_path { get; set; }
            public string? signature_name_ocr_text { get; set; }

            public string? signature_box_rect { get; set; }
            public string? signature_name_rect { get; set; }
        }

        private sealed class PublicDebugUrls
        {
            public string? debug_folder_url { get; set; }
            public string? debug_last_page_url { get; set; }
            public string? debug_signature_box_url { get; set; }
            public string? debug_signature_name_band_url { get; set; }
            public string? debug_signature_name_ocr_url { get; set; }
            public string? debug_body_expected_url { get; set; }
            public string? debug_body_actual_url { get; set; }
        }
    }
}