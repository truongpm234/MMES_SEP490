using AMMS.Application.Helpers;
using AMMS.Application.Interfaces;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MimeKit;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AMMS.Application.Services
{
    public class EmailService : IEmailService
    {
        private readonly SendGridSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;
        private readonly IQuoteRepository _quoteRepo;
        public EmailService(IOptions<SendGridSettings> options, IConfiguration config, IMemoryCache cache, IQuoteRepository quoteRepository)
        {
            _settings = options.Value;
            _config = config;
            _cache = cache;
            _quoteRepo = quoteRepository;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlContent)
        {
            // Config
            var baseUrl = _config["Unosend:BaseUrl"] ?? "https://www.unosend.co/api/v1";
            var apiKey = _config["Unosend:ApiKey"] ?? _config["EmailSmtp:Password"];

            var fromEmail = _config["EmailSender:FromEmail"] ?? _settings.FromEmail;
            var fromName = _config["EmailSender:FromName"] ?? _settings.FromName ?? "";

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new Exception("Thiếu Unosend:ApiKey (API key dạng un_...)");

            if (string.IsNullOrWhiteSpace(fromEmail))
                throw new Exception("Thiếu EmailSender:FromEmail");

            if (string.IsNullOrWhiteSpace(toEmail))
                throw new ArgumentException("toEmail is required");

            var url = $"{baseUrl.TrimEnd('/')}/emails";

            var payload = new
            {
                from = fromEmail,
                to = new[] { toEmail },
                subject = subject,
                html = htmlContent
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content);
            var respText = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Unosend API failed: {(int)resp.StatusCode} - {respText}");
        }

        private string CacheKey(string email) => $"OTP::{NormalizeEmail(email)}";

        private static string NormalizeEmail(string email)
            => email.Trim().ToLowerInvariant();

        private int ExpiryMinutes =>
            int.TryParse(_config["Otp:ExpiryMinutes"], out var m) ? m : 500000;

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

            await SendAsync(email, "OTP xác thực AMMS", html);
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
