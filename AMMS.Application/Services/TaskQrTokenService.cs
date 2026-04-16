using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Productions;
using Microsoft.Extensions.Configuration;

namespace AMMS.Application.Services
{
    public class TaskQrTokenService : ITaskQrTokenService
    {
        private readonly byte[] _secretBytes;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public TaskQrTokenService(IConfiguration config)
        {
            var secret = config["TaskQr:Secret"];
            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("Missing config: TaskQr:Secret");

            _secretBytes = Encoding.UTF8.GetBytes(secret);
        }

        public string CreateToken(int taskId, int qtyGood, TimeSpan ttl)
            => CreateToken(taskId, qtyGood, null, ttl);

        public string CreateToken(
            int taskId,
            int qtyGood,
            IReadOnlyList<TaskMaterialUsageInputDto>? materials,
            TimeSpan ttl)
        {
            var payload = new TaskQrTokenPayloadDto
            {
                task_id = taskId,
                qty_good = qtyGood,
                exp_unix = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds(),
                materials = materials?
                    .Select(x => new TaskMaterialUsageInputDto
                    {
                        material_id = x.material_id,
                        quantity_used = Math.Round(x.quantity_used, 4),
                        quantity_left = Math.Round(x.quantity_left, 4),
                        is_stock = x.is_stock
                    })
                    .ToList() ?? new List<TaskMaterialUsageInputDto>()
            };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
            var payloadBase64 = Base64UrlEncode(jsonBytes);
            var sigBytes = Sign(payloadBase64);
            var sigBase64 = Base64UrlEncode(sigBytes);

            return $"{payloadBase64}.{sigBase64}";
        }

        public bool TryValidate(string token, out int taskId, out int qtyGood, out string reason)
        {
            taskId = 0;
            qtyGood = 0;

            if (!TryValidate(token, out TaskQrTokenPayloadDto payload, out reason))
                return false;

            taskId = payload.task_id;
            qtyGood = payload.qty_good;
            return true;
        }

        public bool TryValidate(string token, out TaskQrTokenPayloadDto payload, out string reason)
        {
            payload = new TaskQrTokenPayloadDto();
            reason = "Invalid token";

            if (string.IsNullOrWhiteSpace(token))
            {
                reason = "Token is empty";
                return false;
            }

            var parts = token.Split('.', 2);
            if (parts.Length != 2)
            {
                reason = "Token format is invalid";
                return false;
            }

            try
            {
                var payloadBase64 = parts[0];
                var providedSig = Base64UrlDecode(parts[1]);
                var expectedSig = Sign(payloadBase64);

                if (!CryptographicOperations.FixedTimeEquals(providedSig, expectedSig))
                {
                    reason = "Token signature is invalid";
                    return false;
                }

                var jsonBytes = Base64UrlDecode(payloadBase64);
                var model = JsonSerializer.Deserialize<TaskQrTokenPayloadDto>(jsonBytes, _jsonOptions);

                if (model == null)
                {
                    reason = "Token payload is invalid";
                    return false;
                }

                if (model.exp_unix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    reason = "QR token has expired";
                    return false;
                }

                if (model.task_id <= 0)
                {
                    reason = "task_id is invalid";
                    return false;
                }

                if (model.qty_good <= 0)
                {
                    reason = "qty_good is invalid";
                    return false;
                }

                model.materials ??= new List<TaskMaterialUsageInputDto>();

                payload = model;
                reason = "";
                return true;
            }
            catch
            {
                reason = "Cannot parse token";
                return false;
            }
        }

        private byte[] Sign(string payloadBase64)
        {
            using var hmac = new HMACSHA256(_secretBytes);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadBase64));
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string input)
        {
            var s = input.Replace('-', '+').Replace('_', '/');

            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }

            return Convert.FromBase64String(s);
        }
    }
}