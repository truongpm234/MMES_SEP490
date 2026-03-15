using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Email;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AMMS.Application.Services
{
    public class EmailService : IEmailService
    {
        private readonly SendGridSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<EmailService> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public EmailService(
            IOptions<SendGridSettings> options,
            IConfiguration config,
            IMemoryCache cache,
            IHttpClientFactory httpClient,
            ILogger<EmailService> logger)
        {
            _settings = options.Value;
            _config = config;
            _cache = cache;
            _httpFactory = httpClient;
            _logger = logger;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlContent)
        {
            var apiKey = _config["Unosend:ApiKey"]?.Trim();
            var fromEmail = (_config["EmailSender:FromEmail"] ?? _settings.FromEmail)?.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Thiếu Unosend:ApiKey");

            if (string.IsNullOrWhiteSpace(fromEmail))
                throw new InvalidOperationException("Thiếu EmailSender:FromEmail");

            if (string.IsNullOrWhiteSpace(toEmail))
                throw new ArgumentException("toEmail is required");

            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("subject is required");

            if (string.IsNullOrWhiteSpace(htmlContent))
                throw new ArgumentException("htmlContent is required");

            var payload = new UnosendSendEmailRequest
            {
                from = NormalizeSingleEmail(fromEmail),
                to = NormalizeEmailList(toEmail),
                subject = subject.Trim(),
                html = htmlContent.Trim(),
                text = HtmlToPlainText(htmlContent)
            };

            if (payload.to.Length == 0)
                throw new ArgumentException("No valid recipient email found");

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var http = _httpFactory.CreateClient("Unosend");

            using var req = new HttpRequestMessage(HttpMethod.Post, "emails");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "Sending email directly via Unosend. From={From}; To={To}; Subject={Subject}",
                payload.from,
                string.Join(",", payload.to),
                payload.subject
            );

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            var respText = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
                return;

            _logger.LogError(
                "Unosend send failed. Status={Status}; Response={Response}; To={To}; Subject={Subject}",
                (int)resp.StatusCode,
                respText,
                string.Join(",", payload.to),
                payload.subject
            );

            throw new InvalidOperationException(
                $"Unosend API failed: {(int)resp.StatusCode} - {respText}");
        }

        private sealed class UnosendSendEmailRequest
        {
            public string from { get; set; } = null!;
            public string[] to { get; set; } = Array.Empty<string>();
            public string subject { get; set; } = null!;
            public string html { get; set; } = null!;
            public string text { get; set; } = null!;
        }

        private static string NormalizeSingleEmail(string email)
            => new MailAddress(email.Trim()).Address;

        private static string[] NormalizeEmailList(string input)
        {
            return input
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => new MailAddress(x).Address)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string HtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            var text = html;
            text = Regex.Replace(text, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</p>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }

        private string CacheKey(string email) => $"OTP::{NormalizeEmail(email)}";

        private static string NormalizeEmail(string email)
            => email.Trim().ToLowerInvariant();

        private int ExpiryMinutes =>
            int.TryParse(_config["Otp:ExpiryMinutes"], out var m) ? m : 10;

        private int MaxAttempts =>
            int.TryParse(_config["Otp:MaxAttempts"], out var a) ? a : 5;

        private static string GenerateOtp6()
        {
            var b = RandomNumberGenerator.GetBytes(4);
            var v = BitConverter.ToUInt32(b, 0) % 1_000_000;
            return v.ToString("D6");
        }

        private static string Sha256(string input)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash);
        }

        private sealed class OtpCacheModel
        {
            public string OtpHash { get; set; } = null!;
            public DateTime ExpiresAtUtc { get; set; }
            public int Attempts { get; set; }
        }

        public async Task SendOtpAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("email is required");

            email = NormalizeEmail(email);

            var otp = GenerateOtp6();
            var expiresAt = DateTime.UtcNow.AddMinutes(ExpiryMinutes);

            var model = new OtpCacheModel
            {
                OtpHash = Sha256($"{email}:{otp}"),
                ExpiresAtUtc = expiresAt,
                Attempts = 0
            };

            _cache.Set(
                CacheKey(email),
                model,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ExpiryMinutes)
                });

            var html = $@"
<div style='font-family:Arial,Helvetica,sans-serif;max-width:520px;margin:auto;color:#333'>
  <h2 style='margin:0'>Mã OTP xác thực</h2>
  <div style='margin:18px 0;padding:14px;border:1px solid #eee;border-radius:8px;text-align:center'>
    <div style='font-size:28px;letter-spacing:6px;font-weight:700'>{otp}</div>
  </div>
  <p style='font-size:12px;color:#888'>Nếu bạn không yêu cầu mã này, hãy bỏ qua email.</p>
</div>";

            var subject = $"OTP xác thực AMMS - {DateTime.Now:ddMMyyyy-HHmmss}";
            await SendAsync(email, subject, html);
        }

        public Task<bool> VerifyOtpAsync(string email, string otp)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(otp))
                return Task.FromResult(false);

            email = NormalizeEmail(email);
            otp = otp.Trim();

            if (!_cache.TryGetValue(CacheKey(email), out OtpCacheModel? model) || model == null)
                return Task.FromResult(false);

            if (DateTime.UtcNow > model.ExpiresAtUtc)
            {
                _cache.Remove(CacheKey(email));
                return Task.FromResult(false);
            }

            model.Attempts++;
            if (model.Attempts > MaxAttempts)
            {
                _cache.Remove(CacheKey(email));
                return Task.FromResult(false);
            }

            var hash = Sha256($"{email}:{otp}");
            var ok = string.Equals(hash, model.OtpHash, StringComparison.OrdinalIgnoreCase);

            if (!ok)
            {
                _cache.Set(CacheKey(email), model, model.ExpiresAtUtc);
                return Task.FromResult(false);
            }

            _cache.Remove(CacheKey(email));
            return Task.FromResult(true);
        }

        public async Task SendMailResetPassword(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email không hợp lệ");

            email = NormalizeEmail(email);

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiresAt = DateTime.UtcNow.AddMinutes(ExpiryMinutes);

            var cacheKey = $"RESET_PASSWORD::{email}";

            _cache.Set(
                cacheKey,
                new
                {
                    TokenHash = Sha256($"{email}:{token}"),
                    ExpiresAtUtc = expiresAt
                },
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ExpiryMinutes)
                }
            );

            var resetBaseUrl = _config["App:ResetPasswordUrl"];
            if (string.IsNullOrWhiteSpace(resetBaseUrl))
                throw new Exception("Thiếu cấu hình App:ResetPasswordUrl");

            var resetLink = $"{resetBaseUrl}?token={token}&email={Uri.EscapeDataString(email)}";

            var html = ResetPasswordMail.GetHtmlBody(
                fullName: email,
                resetLink: resetLink,
                expiredMinutes: ExpiryMinutes
            );

            await SendAsync(
                toEmail: email,
                subject: ResetPasswordMail.Subject,
                htmlContent: html
            );
        }
    }
}