using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using DocumentFormat.OpenXml;
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
using UglyToad.PdfPig.Content;

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
            var consultantStructuredText = ExtractDocxStructuredText(consultantDocxBytes);
            var customerPdfText = ExtractPdfStructuredText(customerPdfBytes);

            var expectedClauses = ParseClausesFromDieu1ToDieu7(consultantStructuredText);

            if (expectedClauses.Count != 7)
            {
                throw new InvalidOperationException(
                    "Không đọc được đầy đủ Điều 1 đến Điều 7 từ file DOCX hợp đồng tư vấn.");
            }

            var actualClauses = ParseClausesFromDieu1ToDieu7(customerPdfText);

            bool usedOcrFallback = false;

            // Chỉ OCR khi text-layer PDF không đủ 7 điều.
            if (actualClauses.Count != 7)
            {
                var customerOcrText = await OcrWholePdfAsync(customerPdfBytes, ct);
                actualClauses = ParseClausesFromDieu1ToDieu7(customerOcrText);
                usedOcrFallback = true;
            }

            if (actualClauses.Count != 7)
            {
                throw new InvalidOperationException(
                    "Không trích xuất được đầy đủ nội dung hợp đồng từ Điều 1 đến Điều 7 từ file PDF khách hàng.");
            }

            var textDifferences = BuildClauseDifferences(expectedClauses, actualClauses);

            var expectedJoined = string.Join("\n", expectedClauses.Values.Select(x => NormalizeClauseForCompare(x.raw_text)));
            var actualJoined = string.Join("\n", actualClauses.Values.Select(x => NormalizeClauseForCompare(x.raw_text)));

            var bodyExactMatch = textDifferences.Count == 0;

            var similarityPercent = Math.Round(
                (decimal)(ComputeDiceSimilarity(expectedJoined, actualJoined, 2) * 100d), 2);

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
                signature_inside_allowed_box = signatureAnalysis.signature_inside_allowed_box,

                has_digital_signature = digitalSignature.has_signature,
                digital_signature_valid = digitalSignature.is_valid,

                used_ocr_fallback = usedOcrFallback,

                consultant_text_length = expectedJoined.Length,
                customer_text_length = actualJoined.Length,

                verification_mode = digitalSignature.is_valid
                    ? "ĐỐI CHIẾU ĐIỀU 1-7 + CHỮ KÝ SỐ"
                    : "ĐỐI CHIẾU ĐIỀU 1-7 + CHỮ KÝ TAY ĐÚNG Ô",

                reject_reason = BuildRejectReason(
                    bodyExactMatch,
                    textDifferences,
                    signatureAnalysis,
                    digitalSignature.is_valid),

                text_differences = textDifferences,

                debug_signature_box_rect = ToRectString(signatureAnalysis.allowed_signature_box),
                debug_signature_name_rect = ToRectString(signatureAnalysis.name_band)
            };
        }

        private static string? BuildRejectReason(
            bool bodyExactMatch,
            IReadOnlyList<TextDifferenceItemDto> diffs,
            SignatureAnalysisResult signatureAnalysis,
            bool digitalSignatureValid)
        {
            var reasons = new List<string>();

            if (!bodyExactMatch)
            {
                if (diffs.Count > 0)
                {
                    var clauseList = string.Join(", ", diffs.Select(x => $"Điều {x.clause_no}"));
                    reasons.Add($"Nội dung hợp đồng khác tại {clauseList}.");
                }
                else
                {
                    reasons.Add("Nội dung hợp đồng từ Điều 1 đến Điều 7 có khác biệt.");
                }
            }

            if (!digitalSignatureValid)
            {
                if (!signatureAnalysis.has_visible_signature)
                {
                    reasons.Add("Không phát hiện chữ ký tay của khách hàng trong vùng ký.");
                }
                else if (!signatureAnalysis.signature_inside_allowed_box)
                {
                    reasons.Add("Chữ ký của khách hàng không nằm trong ô ký hợp lệ.");
                }
            }

            if (!signatureAnalysis.signature_name_present)
            {
                reasons.Add("Không xác định đúng vùng họ tên của khách hàng ở block ký.");
            }

            return reasons.Count == 0 ? null : string.Join(" ", reasons);
        }

        // =========================
        // 1) ĐỌC DOCX / PDF
        // =========================

        private static string ExtractDocxStructuredText(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var doc = WordprocessingDocument.Open(ms, false);

            var lines = new List<string>();

            AppendOpenXmlInOrder(doc.MainDocumentPart?.Document?.Body, lines);

            foreach (var headerPart in doc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
            {
                AppendOpenXmlInOrder(headerPart.Header, lines);
            }

            foreach (var footerPart in doc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
            {
                AppendOpenXmlInOrder(footerPart.Footer, lines);
            }

            return string.Join("\n", lines);
        }

        private static void AppendOpenXmlInOrder(OpenXmlElement? root, List<string> lines)
        {
            if (root == null) return;

            foreach (var child in root.ChildElements)
            {
                switch (child)
                {
                    case Paragraph p:
                        AppendParagraph(p, lines);
                        break;

                    case Table t:
                        AppendTable(t, lines);
                        break;

                    default:
                        AppendOpenXmlInOrder(child, lines);
                        break;
                }
            }
        }

        private static void AppendParagraph(Paragraph paragraph, List<string> lines)
        {
            var text = string.Concat(paragraph.Descendants<Text>().Select(x => x.Text));
            text = NormalizeExtractedLine(text);

            if (!string.IsNullOrWhiteSpace(text))
                lines.Add(text);
        }

        private static void AppendTable(Table table, List<string> lines)
        {
            foreach (var row in table.Elements<TableRow>())
            {
                var cellTexts = new List<string>();

                foreach (var cell in row.Elements<TableCell>())
                {
                    var paragraphTexts = cell
                        .Descendants<Paragraph>()
                        .Select(p => NormalizeExtractedLine(string.Concat(p.Descendants<Text>().Select(x => x.Text))))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();

                    if (paragraphTexts.Count > 0)
                    {
                        cellTexts.Add(string.Join(" ", paragraphTexts));
                    }
                }

                if (cellTexts.Count > 0)
                {
                    // Để DOCX table và PDF text cùng kiểu hiển thị hơn
                    lines.Add(string.Join(" | ", cellTexts));
                }
            }
        }

        private static string NormalizeExtractedLine(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var text = WebUtility.HtmlDecode(input);
            text = text.Normalize(NormalizationForm.FormKC);
            text = text.Replace('\u00A0', ' ');
            text = Regex.Replace(text, @"[ \t]+", " ").Trim();

            return text;
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

        // =========================
        // 2) PARSE THEO ĐIỀU 1 -> 7
        // =========================

        private static Dictionary<int, ContractClauseBlock> ParseClausesFromDieu1ToDieu7(string input)
        {
            var result = new Dictionary<int, ContractClauseBlock>();

            if (string.IsNullOrWhiteSpace(input))
                return result;

            var text = NormalizeLineBreaks(input);

            var signIndex = IndexOfIgnoreCaseNoDiacritics(text, "ĐẠI DIỆN BÊN A");
            if (signIndex > 0)
            {
                text = text[..signIndex];
            }

            var matches = Regex.Matches(
                text,
                @"(?im)^\s*Điều\s*(?<no>[1-7])\s*:\s*(?<title>.+?)\s*$");

            if (matches.Count != 7)
                return result;

            for (int i = 0; i < matches.Count; i++)
            {
                var current = matches[i];
                var start = current.Index;
                var end = i + 1 < matches.Count
                    ? matches[i + 1].Index
                    : text.Length;

                var clauseNo = int.Parse(current.Groups["no"].Value);
                var title = current.Groups["title"].Value.Trim();
                var raw = text[start..end].Trim();

                result[clauseNo] = new ContractClauseBlock
                {
                    clause_no = clauseNo,
                    clause_title = title,
                    raw_text = raw
                };
            }

            return result;
        }

        private static string NormalizeLineBreaks(string input)
            => input.Replace("\r\n", "\n").Replace('\r', '\n');

        private static int IndexOfIgnoreCaseNoDiacritics(string source, string value)
        {
            var sourceNorm = RemoveDiacritics(source).ToLowerInvariant();
            var valueNorm = RemoveDiacritics(value).ToLowerInvariant();
            return sourceNorm.IndexOf(valueNorm, StringComparison.Ordinal);
        }

        private static List<TextDifferenceItemDto> BuildClauseDifferences(
            IReadOnlyDictionary<int, ContractClauseBlock> expectedClauses,
            IReadOnlyDictionary<int, ContractClauseBlock> actualClauses)
        {
            var diffs = new List<TextDifferenceItemDto>();

            for (int i = 1; i <= 7; i++)
            {
                if (!expectedClauses.TryGetValue(i, out var expected))
                {
                    diffs.Add(new TextDifferenceItemDto
                    {
                        clause_no = i,
                        clause_title = $"Điều {i}",
                        similarity_percent = 0m,
                        message = $"Không tìm thấy Điều {i} trong hợp đồng DOCX gốc.",
                        expected_text = "",
                        actual_text = actualClauses.TryGetValue(i, out var onlyActual)
                            ? NormalizeClauseForDisplay(onlyActual.raw_text)
                            : ""
                    });

                    continue;
                }

                if (!actualClauses.TryGetValue(i, out var actual))
                {
                    diffs.Add(new TextDifferenceItemDto
                    {
                        clause_no = i,
                        clause_title = expected.clause_title,
                        similarity_percent = 0m,
                        message = $"Không đọc được Điều {i} trong file PDF khách hàng.",
                        expected_text = NormalizeClauseForDisplay(expected.raw_text),
                        actual_text = ""
                    });

                    continue;
                }

                var expectedDisplay = NormalizeClauseForDisplay(expected.raw_text);
                var actualDisplay = NormalizeClauseForDisplay(actual.raw_text);

                var expectedCompare = NormalizeClauseForCompare(expected.raw_text);
                var actualCompare = NormalizeClauseForCompare(actual.raw_text);

                if (string.Equals(expectedCompare, actualCompare, StringComparison.Ordinal))
                    continue;

                var similarity = Math.Round(
                    (decimal)(ComputeDiceSimilarity(expectedCompare, actualCompare, 2) * 100d), 2);

                diffs.Add(new TextDifferenceItemDto
                {
                    type = "changed",
                    clause_no = i,
                    clause_title = expected.clause_title,
                    similarity_percent = similarity,
                    message = $"Nội dung khác tại Điều {i}: {expected.clause_title}.",
                    expected_text = expectedDisplay,
                    actual_text = actualDisplay
                });
            }

            return diffs;
        }

        private static string NormalizeClauseForDisplay(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var text = WebUtility.HtmlDecode(input);
            text = text.Normalize(NormalizationForm.FormKC);
            text = text.Replace('\u00A0', ' ');
            text = NormalizeLineBreaks(text);

            // bỏ bullet đầu dòng: • · o - * ...
            text = Regex.Replace(
                text,
                @"(?m)^\s*([•·●▪◦■\-–—\*\+]|o)\s+",
                "");

            // bỏ khoảng trắng thừa trước dấu câu
            text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");

            // chuẩn hóa khoảng trắng quanh |
            text = Regex.Replace(text, @"\s*\|\s*", " | ");

            // gom từng dòng thành chuỗi gọn để expected_text và actual_text cùng format
            var lines = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => Regex.Replace(x, @"[ \t]+", " ").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            return string.Join(" ", lines).Trim();
        }

        private static string NormalizeClauseForCompare(string input)
        {
            var text = NormalizeClauseForDisplay(input);

            text = RemoveDiacritics(text).ToLowerInvariant();

            // bỏ khác biệt nhỏ về khoảng trắng
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

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

        // =========================
        // 3) CHỮ KÝ: LẤY VÙNG KÝ ĐỘNG THEO ANCHOR TEXT
        // =========================

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

                Rectangle customerPanel;
                Rectangle allowedSignatureBox;
                Rectangle observationBox;
                Rectangle nameBand;
                bool signatureNamePresentFromPdf = false;

                if (TryBuildSignatureLayoutFromPdfText(pdfBytes, expectedCustomerName, out var layout))
                {
                    customerPanel = ToImageRectangle(layout.customer_panel, image.Width, image.Height, layout.page_width, layout.page_height);
                    allowedSignatureBox = ToImageRectangle(layout.allowed_signature_box, image.Width, image.Height, layout.page_width, layout.page_height);
                    observationBox = ToImageRectangle(layout.observation_box, image.Width, image.Height, layout.page_width, layout.page_height);
                    nameBand = ToImageRectangle(layout.name_band, image.Width, image.Height, layout.page_width, layout.page_height);
                    signatureNamePresentFromPdf = layout.signature_name_present;
                }
                else
                {
                    // fallback mềm nếu không lấy được anchor text
                    customerPanel = ToRectangle(image.Width, image.Height, 0.04, 0.56, 0.44, 0.26);
                    allowedSignatureBox = ToRectangle(image.Width, image.Height, 0.09, 0.60, 0.30, 0.12);
                    observationBox = ToRectangle(image.Width, image.Height, 0.06, 0.59, 0.36, 0.15);
                    nameBand = ToRectangle(image.Width, image.Height, 0.12, 0.75, 0.24, 0.05);
                }

                var signatureNamePresent = signatureNamePresentFromPdf;

                if (!signatureNamePresent)
                {
                    var nameBandPath = Path.Combine(tempRoot, "signature-name-band.png");
                    using (var nameCrop = image.Clone(ctx => ctx.Crop(nameBand)))
                    {
                        await nameCrop.SaveAsPngAsync(nameBandPath, ct);
                    }

                    var nameBandText = await OcrImageAsync(nameBandPath, ct);
                    signatureNamePresent = NormalizeWordToken(nameBandText)
                        .Contains(NormalizeWordToken(expectedCustomerName), StringComparison.Ordinal);
                }

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

                    // Nới ngưỡng để tránh false reject khi nét ký chạm mép ô
                    insideAllowedBox = insideRatio >= 0.55d;
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

        private static bool TryBuildSignatureLayoutFromPdfText(
            byte[] pdfBytes,
            string expectedCustomerName,
            out SignatureLayoutFromPdf layout)
        {
            using var ms = new MemoryStream(pdfBytes);
            using var pdf = PdfDocument.Open(ms);
            var page = pdf.GetPage(pdf.NumberOfPages);

            var words = page.GetWords().ToList();
            if (words.Count == 0)
            {
                layout = default;
                return false;
            }

            if (!TryFindPhraseBox(words, new[] { "ĐẠI", "DIỆN", "BÊN", "A" }, out var headerA))
            {
                layout = default;
                return false;
            }

            if (!TryFindPhraseBox(words, new[] { "Ký", "ghi", "rõ", "họ", "tên" }, out var signGuideA))
            {
                layout = default;
                return false;
            }

            var customerNameTokens = expectedCustomerName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (!TryFindPhraseBox(words, customerNameTokens, out var customerNameBox))
            {
                layout = default;
                return false;
            }

            var pageWidth = page.Width;
            var pageHeight = page.Height;

            // ô bên trái = nửa trái trang cuối
            var cellLeft = Math.Max(0d, headerA.Left - 20d);
            var cellRight = pageWidth / 2d - 6d;

            // Vì PDF dùng trục y từ dưới lên:
            // - signGuideA ở phía trên
            // - customerNameBox ở phía dưới
            var observationTop = signGuideA.Bottom - 8d;
            var observationBottom = customerNameBox.Top + 8d;

            if (observationTop <= observationBottom)
            {
                layout = default;
                return false;
            }

            var customerPanel = new PdfTextBox(
                cellLeft,
                customerNameBox.Bottom - 30d,
                cellRight,
                headerA.Top + 10d);

            var allowedSignatureBox = new PdfTextBox(
                cellLeft + 16d,
                observationBottom + 4d,
                cellRight - 16d,
                observationTop - 4d);

            var observationBox = new PdfTextBox(
                cellLeft + 8d,
                observationBottom + 2d,
                cellRight - 8d,
                observationTop - 2d);

            var nameBand = ExpandBox(customerNameBox, 18d, 8d, pageWidth, pageHeight);

            layout = new SignatureLayoutFromPdf
            {
                page_width = pageWidth,
                page_height = pageHeight,
                signature_name_present = true,
                customer_panel = customerPanel,
                allowed_signature_box = allowedSignatureBox,
                observation_box = observationBox,
                name_band = nameBand
            };

            return true;
        }

        private static bool TryFindPhraseBox(
            IReadOnlyList<Word> words,
            IReadOnlyList<string> phraseTokens,
            out PdfTextBox result)
        {
            var expected = phraseTokens
                .Select(NormalizeWordToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            var actual = words
                .Select(w => new
                {
                    Word = w,
                    Text = NormalizeWordToken(w.Text)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            for (int i = 0; i <= actual.Count - expected.Length; i++)
            {
                bool matched = true;

                for (int j = 0; j < expected.Length; j++)
                {
                    if (!string.Equals(actual[i + j].Text, expected[j], StringComparison.Ordinal))
                    {
                        matched = false;
                        break;
                    }
                }

                if (!matched)
                    continue;

                var box = ToPdfTextBox(actual[i].Word.BoundingBox);

                for (int j = 1; j < expected.Length; j++)
                {
                    box = Union(box, ToPdfTextBox(actual[i + j].Word.BoundingBox));
                }

                result = box;
                return true;
            }

            result = default;
            return false;
        }

        private static string NormalizeWordToken(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var text = WebUtility.HtmlDecode(input);
            text = text.Normalize(NormalizationForm.FormKC);
            text = RemoveDiacritics(text).ToLowerInvariant();
            text = Regex.Replace(text, @"[^\p{L}\p{N}]+", "");

            return text;
        }

        private static PdfTextBox ToPdfTextBox(UglyToad.PdfPig.Core.PdfRectangle rect)
            => new(rect.Left, rect.Bottom, rect.Right, rect.Top);

        private static PdfTextBox Union(PdfTextBox a, PdfTextBox b)
            => new(
                Math.Min(a.Left, b.Left),
                Math.Min(a.Bottom, b.Bottom),
                Math.Max(a.Right, b.Right),
                Math.Max(a.Top, b.Top));

        private static PdfTextBox ExpandBox(PdfTextBox input, double padX, double padY, double pageWidth, double pageHeight)
            => new(
                Math.Max(0d, input.Left - padX),
                Math.Max(0d, input.Bottom - padY),
                Math.Min(pageWidth, input.Right + padX),
                Math.Min(pageHeight, input.Top + padY));

        private static Rectangle ToImageRectangle(
            PdfTextBox box,
            int imageWidth,
            int imageHeight,
            double pageWidth,
            double pageHeight)
        {
            var x = (int)Math.Round(box.Left / pageWidth * imageWidth);
            var y = (int)Math.Round((pageHeight - box.Top) / pageHeight * imageHeight);
            var w = (int)Math.Round(box.Width / pageWidth * imageWidth);
            var h = (int)Math.Round(box.Height / pageHeight * imageHeight);

            return new Rectangle(
                Math.Max(0, x),
                Math.Max(0, y),
                Math.Max(1, w),
                Math.Max(1, h));
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

            // inset để tránh ăn vào line border của khung
            const int inset = 8;

            int startX = Math.Max(0, area.X + inset);
            int startY = Math.Max(0, area.Y + inset);
            int endX = Math.Min(image.Width, area.X + area.Width - inset);
            int endY = Math.Min(image.Height, area.Y + area.Height - inset);

            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    var pixel = image[x, y];

                    var isDark =
                        pixel.A > 0 &&
                        (pixel.R < 215 || pixel.G < 215 || pixel.B < 215);

                    if (!isDark)
                        continue;

                    darkPixelCount++;

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

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

        // =========================
        // 4) OCR
        // =========================

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
        private sealed class ContractClauseBlock
        {
            public int clause_no { get; set; }
            public string clause_title { get; set; } = string.Empty;
            public string raw_text { get; set; } = string.Empty;
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

        private readonly record struct PdfTextBox(double Left, double Bottom, double Right, double Top)
        {
            public double Width => Math.Max(0d, Right - Left);
            public double Height => Math.Max(0d, Top - Bottom);
        }

        private sealed class SignatureLayoutFromPdf
        {
            public double page_width { get; set; }
            public double page_height { get; set; }

            public bool signature_name_present { get; set; }

            public PdfTextBox customer_panel { get; set; }
            public PdfTextBox allowed_signature_box { get; set; }
            public PdfTextBox observation_box { get; set; }
            public PdfTextBox name_band { get; set; }
        }
    }
}