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
            var apiKey = _config["Resend:ApiKey"]?.Trim();
            var fromEmail = (_config["Resend:FromEmail"] ?? _config["EmailSender:FromEmail"] ?? _settings.FromEmail)?.Trim();
            var userAgent = _config["Resend:UserAgent"]?.Trim() ?? "MES/1.0";

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Thiếu Resend:ApiKey");

            if (string.IsNullOrWhiteSpace(fromEmail))
                throw new InvalidOperationException("Thiếu Resend:FromEmail");

            if (string.IsNullOrWhiteSpace(toEmail))
                throw new ArgumentException("toEmail is required");

            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("subject is required");

            if (string.IsNullOrWhiteSpace(htmlContent))
                throw new ArgumentException("htmlContent is required");

            var payload = new
            {
                from = fromEmail.Trim(),
                to = NormalizeEmailList(toEmail),
                subject = subject.Trim(),
                html = htmlContent.Trim(),
                text = HtmlToPlainText(htmlContent)
            };

            if (payload.to.Length == 0)
                throw new ArgumentException("No valid recipient email found");

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var http = _httpFactory.CreateClient("Resend");

            Exception? lastEx = null;

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, "emails");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    req.Headers.TryAddWithoutValidation("User-Agent", userAgent);

                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    _logger.LogInformation(
                        "Sending email via Resend. Attempt={Attempt}; From={From}; To={To}; Subject={Subject}",
                        attempt,
                        payload.from,
                        string.Join(",", payload.to),
                        payload.subject
                    );

                    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    var respText = await resp.Content.ReadAsStringAsync();

                    if (resp.IsSuccessStatusCode)
                    {
                        _logger.LogInformation(
                            "Resend send success. Attempt={Attempt}; Status={Status}; To={To}; Subject={Subject}; Response={Response}",
                            attempt,
                            (int)resp.StatusCode,
                            string.Join(",", payload.to),
                            payload.subject,
                            respText
                        );

                        return;
                    }

                    _logger.LogError(
                        "Resend send failed. Attempt={Attempt}; Status={Status}; Response={Response}; To={To}; Subject={Subject}",
                        attempt,
                        (int)resp.StatusCode,
                        respText,
                        string.Join(",", payload.to),
                        payload.subject
                    );

                    lastEx = new InvalidOperationException(
                        $"Resend API failed: {(int)resp.StatusCode} - {respText}");
                }
                catch (HttpRequestException ex)
                {
                    lastEx = ex;

                    _logger.LogError(
                        ex,
                        "Resend TLS/HTTP error. Attempt={Attempt}; To={To}; Subject={Subject}",
                        attempt,
                        string.Join(",", payload.to),
                        payload.subject
                    );
                }
                catch (TaskCanceledException ex)
                {
                    lastEx = ex;

                    _logger.LogError(
                        ex,
                        "Resend timeout. Attempt={Attempt}; To={To}; Subject={Subject}",
                        attempt,
                        string.Join(",", payload.to),
                        payload.subject
                    );
                }

                if (attempt < 2)
                    await Task.Delay(800);
            }

            throw new InvalidOperationException(
                "Dịch vụ gửi email tạm thời không khả dụng. Vui lòng thử lại sau.", lastEx);
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
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
</head>
<body style='margin:0;background-color:#f7fafc;padding:30px 0;'>
  <div style='max-width:700px;margin:0 auto;padding:0 12px;font-family:""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;'>

    <div style='background:linear-gradient(135deg,#fff7ed 0%,#eff6ff 100%);border:1px solid #dbe7f3;border-radius:16px;padding:28px 24px;box-shadow:0 10px 28px rgba(15,23,42,0.06);color:#334155;line-height:1.78;'>

      <div style='display:inline-block;background:linear-gradient(90deg,#f97316 0%,#2563eb 100%);color:#ffffff;font-size:11px;font-weight:800;letter-spacing:1px;text-transform:uppercase;padding:10px 12px;border-radius:999px;margin-bottom:14px;'>
        MES SECURITY
      </div>

      <div style='font-size:22px;font-weight:800;color:#1e3a8a;margin-bottom:10px;letter-spacing:0.2px;'>
        Mã OTP xác thực
      </div>

      <p style='margin:0 0 12px 0;font-size:14px;'>
        Kính gửi người dùng,
      </p>

      <p style='margin:0 0 18px 0;font-size:14px;'>
        Vui lòng sử dụng mã OTP bên dưới để hoàn tất bước xác thực trên hệ thống MES.
      </p>

      <div style='margin:20px 0 18px 0;background:#ffffff;border:2px solid #bae6fd;border-radius:16px;padding:24px 18px;text-align:center;box-shadow:0 6px 18px rgba(14,165,233,0.08);'>
        <div style='font-size:12px;color:#0369a1;font-weight:800;letter-spacing:1px;text-transform:uppercase;margin-bottom:10px;'>
          One-Time Password
        </div>
        <div style='font-size:36px;letter-spacing:8px;font-weight:900;color:#0f172a;'>
          {otp}
        </div>
      </div>

      <div style='margin-top:14px;background:#fff7ed;border:1px solid #fed7aa;border-radius:12px;padding:12px 14px;'>
        <p style='font-size:13px;color:#9a3412;font-weight:900;margin:0 0 6px 0;letter-spacing:0.2px;'>
          ⏳ Lưu ý quan trọng
        </p>
        <p style='margin:0;color:#7c2d12;font-size:12.5px;line-height:1.55;'>
          Mã OTP có hiệu lực trong <b>{ExpiryMinutes} phút</b>. Nếu quá thời gian này, bạn cần yêu cầu mã mới.
        </p>
        <p style='margin:8px 0 0 0;color:#9a3412;font-size:12px;line-height:1.4;font-weight:700;'>
          Không cung cấp mã này cho bất kỳ ai, kể cả người tự xưng là nhân viên hệ thống.
        </p>
      </div>

      <p style='margin:18px 0 0 0;font-size:13px;color:#64748b;line-height:1.7;'>
        Nếu bạn không yêu cầu mã này, vui lòng bỏ qua email.
      </p>
    </div>

    <div style='background:linear-gradient(180deg,#edf2f7 0%,#e2e8f0 100%);padding:15px;text-align:center;font-size:12px;color:#64748b;border-radius:12px;margin-top:14px;'>
      Email này được gửi tự động từ hệ thống MES.
    </div>
  </div>
</body>
</html>";

            var subject = $"OTP xác thực MES - {DateTime.Now:ddMMyyyy-HHmmss}";
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

        //public async Task SendAsync(string toEmail, string subject, string htmlContent)
        //{
        //    var apiKey = _config["Unosend:ApiKey"]?.Trim();
        //    var fromEmail = (_config["EmailSender:FromEmail"] ?? _settings.FromEmail)?.Trim();

        //    if (string.IsNullOrWhiteSpace(apiKey))
        //        throw new InvalidOperationException("Thiếu Unosend:ApiKey");

        //    if (string.IsNullOrWhiteSpace(fromEmail))
        //        throw new InvalidOperationException("Thiếu EmailSender:FromEmail");

        //    if (string.IsNullOrWhiteSpace(toEmail))
        //        throw new ArgumentException("toEmail is required");

        //    if (string.IsNullOrWhiteSpace(subject))
        //        throw new ArgumentException("subject is required");

        //    if (string.IsNullOrWhiteSpace(htmlContent))
        //        throw new ArgumentException("htmlContent is required");

        //    var payload = new UnosendSendEmailRequest
        //    {
        //        from = NormalizeSingleEmail(fromEmail),
        //        to = NormalizeEmailList(toEmail),
        //        subject = subject.Trim(),
        //        html = htmlContent.Trim(),
        //        text = HtmlToPlainText(htmlContent)
        //    };

        //    if (payload.to.Length == 0)
        //        throw new ArgumentException("No valid recipient email found");

        //    var json = JsonSerializer.Serialize(payload, JsonOptions);
        //    var http = _httpFactory.CreateClient("Unosend");

        //    Exception? lastEx = null;

        //    for (int attempt = 1; attempt <= 2; attempt++)
        //    {
        //        try
        //        {
        //            using var req = new HttpRequestMessage(HttpMethod.Post, "emails");
        //            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        //            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        //            _logger.LogInformation(
        //                "Sending email directly via Unosend. Attempt={Attempt}; From={From}; To={To}; Subject={Subject}",
        //                attempt,
        //                payload.from,
        //                string.Join(",", payload.to),
        //                payload.subject
        //            );

        //            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        //            var respText = await resp.Content.ReadAsStringAsync();

        //            if (resp.IsSuccessStatusCode)
        //            {
        //                _logger.LogInformation(
        //                    "Unosend send success. Attempt={Attempt}; Status={Status}; To={To}; Subject={Subject}",
        //                    attempt,
        //                    (int)resp.StatusCode,
        //                    string.Join(",", payload.to),
        //                    payload.subject
        //                );

        //                return;
        //            }

        //            _logger.LogError(
        //                "Unosend send failed. Attempt={Attempt}; Status={Status}; Response={Response}; To={To}; Subject={Subject}",
        //                attempt,
        //                (int)resp.StatusCode,
        //                respText,
        //                string.Join(",", payload.to),
        //                payload.subject
        //            );

        //            lastEx = new InvalidOperationException(
        //                $"Unosend API failed: {(int)resp.StatusCode} - {respText}");
        //        }
        //        catch (HttpRequestException ex)
        //        {
        //            lastEx = ex;

        //            _logger.LogError(
        //                ex,
        //                "Unosend TLS/HTTP error. Attempt={Attempt}; To={To}; Subject={Subject}",
        //                attempt,
        //                string.Join(",", payload.to),
        //                payload.subject
        //            );
        //        }
        //        catch (TaskCanceledException ex)
        //        {
        //            lastEx = ex;

        //            _logger.LogError(
        //                ex,
        //                "Unosend timeout. Attempt={Attempt}; To={To}; Subject={Subject}",
        //                attempt,
        //                string.Join(",", payload.to),
        //                payload.subject
        //            );
        //        }

        //        if (attempt < 2)
        //            await Task.Delay(800);
        //    }

        //    throw new InvalidOperationException(
        //        "Dịch vụ gửi email tạm thời không khả dụng. Vui lòng thử lại sau.", lastEx);
        //}
    }
}