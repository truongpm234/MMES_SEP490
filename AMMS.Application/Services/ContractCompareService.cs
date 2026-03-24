using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AMMS.Application.Services
{
    public class ContractCompareService : IContractCompareService
    {
        private readonly HttpClient _httpClient;

        public ContractCompareService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<CompareContractResponse> CompareAsync(
            int requestId,
            int estimateId,
            string consultantDocxUrl,
            string customerPdfUrl,
            CancellationToken ct = default)
        {
            var consultantBytes = await DownloadBytesAsync(consultantDocxUrl, "consultant contract", ct);
            var customerPdfBytes = await DownloadBytesAsync(customerPdfUrl, "customer signed contract", ct);

            return CompareCore(requestId, estimateId, consultantBytes, customerPdfBytes);
        }

        public async Task<CompareContractResponse> CompareAsync(
            int requestId,
            int estimateId,
            string consultantDocxUrl,
            byte[] customerPdfBytes,
            CancellationToken ct = default)
        {
            var consultantBytes = await DownloadBytesAsync(consultantDocxUrl, "consultant contract", ct);

            return CompareCore(requestId, estimateId, consultantBytes, customerPdfBytes);
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

        private CompareContractResponse CompareCore(
            int requestId,
            int estimateId,
            byte[] consultantDocxBytes,
            byte[] customerPdfBytes)
        {
            var consultantText = ExtractDocxText(consultantDocxBytes);
            var customerText = ExtractPdfText(customerPdfBytes);

            var normalizedA = Normalize(consultantText);
            var normalizedB = Normalize(customerText);

            var similarity = ComputeDiceSimilarity(normalizedA, normalizedB, 5);

            return new CompareContractResponse
            {
                request_id = requestId,
                estimate_id = estimateId,
                similarity_percent = Math.Round((decimal)(similarity * 100d), 2),
                is_match_90 = similarity >= 0.90d,
                consultant_text_length = normalizedA.Length,
                customer_text_length = normalizedB.Length
            };
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

        private static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            input = WebUtility.HtmlDecode(input);
            input = input.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            input = Regex.Replace(input, @"\s+", " ");
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
    }
}