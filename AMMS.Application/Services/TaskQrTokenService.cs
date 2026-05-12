using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Productions;
using Microsoft.Extensions.Configuration;

namespace AMMS.Application.Services
{
    public class TaskQrTokenService : ITaskQrTokenService
    {
        private const byte TokenVersion = 2;
        private const int SignatureLength = 10;

        // chỉ gồm số + chữ, không ký tự đặc biệt
        private const string Base62Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        private readonly byte[] _secretBytes;
        private static readonly Dictionary<char, int> _base62Map = Base62Alphabet
            .Select((ch, idx) => new { ch, idx })
            .ToDictionary(x => x.ch, x => x.idx);

        public TaskQrTokenService(IConfiguration config)
        {
            var secret = config["TaskQr:Secret"];
            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("Missing config: TaskQr:Secret");

            _secretBytes = Encoding.UTF8.GetBytes(secret);
        }

        public string CreateToken(int taskId, int qtyGood, TimeSpan ttl)
    => CreateToken(
        taskId,
        qtyGood,
        null,
        ttl,
        useManualInput: false,
        reason: null,
        reportImageUrl: null,
        referenceInputs: null,
        outputs: null);

        public string CreateToken(
            int taskId,
            int qtyGood,
            IReadOnlyList<TaskMaterialUsageInputDto>? materials,
            TimeSpan ttl)
            => CreateToken(
                taskId,
                qtyGood,
                materials,
                ttl,
                useManualInput: false,
                reason: null,
                reportImageUrl: null,
                referenceInputs: null,
                outputs: null);

        public string CreateToken(
            int taskId,
            int qtyGood,
            IReadOnlyList<TaskMaterialUsageInputDto>? materials,
            TimeSpan ttl,
            bool useManualInput,
            string? reason,
            string? reportImageUrl,
            IReadOnlyList<TaskReferenceUsageInputDto>? referenceInputs,
            IReadOnlyList<TaskOutputReportDto>? outputs)
        {
            if (taskId <= 0)
                throw new ArgumentException("taskId must be > 0");

            if (qtyGood <= 0)
                throw new ArgumentException("qtyGood must be > 0");

            var expUnix = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();

            var normalizedMaterials = NormalizeMaterials(materials);
            var normalizedRefs = NormalizeReferenceInputs(referenceInputs);
            var normalizedOutputs = NormalizeOutputs(outputs);

            var body = BuildBody(
                taskId,
                qtyGood,
                expUnix,
                normalizedMaterials,
                useManualInput,
                NormalizeTokenText(reason, 1000),
                NormalizeTokenText(reportImageUrl, 8000),
                normalizedRefs,
                normalizedOutputs);

            var sig = ComputeSignature(body);

            var raw = new byte[body.Length + sig.Length];
            Buffer.BlockCopy(body, 0, raw, 0, body.Length);
            Buffer.BlockCopy(sig, 0, raw, body.Length, sig.Length);

            return Base62Encode(raw);
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

            // chống ký tự lạ
            for (var i = 0; i < token.Length; i++)
            {
                if (!_base62Map.ContainsKey(token[i]))
                {
                    reason = "Token contains invalid characters";
                    return false;
                }
            }

            byte[] raw;
            try
            {
                raw = Base62Decode(token);
            }
            catch
            {
                reason = "Cannot decode token";
                return false;
            }

            if (raw.Length <= SignatureLength + 1)
            {
                reason = "Token too short";
                return false;
            }

            var bodyLength = raw.Length - SignatureLength;
            var body = new byte[bodyLength];
            var givenSig = new byte[SignatureLength];

            Buffer.BlockCopy(raw, 0, body, 0, bodyLength);
            Buffer.BlockCopy(raw, bodyLength, givenSig, 0, SignatureLength);

            var expectedSig = ComputeSignature(body);
            if (!CryptographicOperations.FixedTimeEquals(givenSig, expectedSig))
            {
                reason = "Token signature is invalid";
                return false;
            }

            try
            {
                payload = ParseBody(body);
            }
            catch
            {
                reason = "Token payload is invalid";
                return false;
            }

            if (payload.exp_unix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                reason = "QR token has expired";
                return false;
            }

            if (payload.task_id <= 0)
            {
                reason = "task_id is invalid";
                return false;
            }

            if (payload.qty_good <= 0)
            {
                reason = "qty_good is invalid";
                return false;
            }

            payload.materials ??= new List<TaskMaterialUsageInputDto>();
            reason = "";
            return true;
        }

        private byte[] BuildBody(
    int taskId,
    int qtyGood,
    long expUnix,
    List<TaskMaterialUsageInputDto> materials,
    bool useManualInput,
    string? reason,
    string? reportImageUrl,
    List<TaskReferenceUsageInputDto> referenceInputs,
    List<TaskOutputReportDto> outputs)
        {
            using var ms = new MemoryStream();

            ms.WriteByte(TokenVersion);

            WriteVarUInt(ms, (ulong)taskId);
            WriteVarUInt(ms, (ulong)qtyGood);
            WriteVarUInt(ms, (ulong)expUnix);

            byte flags = 0;
            if (useManualInput) flags |= 0b0000_0001;
            ms.WriteByte(flags);

            WriteNullableString(ms, reason);
            WriteNullableString(ms, reportImageUrl);

            WriteVarUInt(ms, (ulong)materials.Count);
            foreach (var m in materials)
            {
                if (m.material_id <= 0)
                    throw new ArgumentException("material_id must be > 0");

                WriteVarUInt(ms, (ulong)m.material_id);
                WriteVarUInt(ms, ToScaledUInt64(m.quantity_used));
                WriteVarUInt(ms, ToScaledUInt64(m.quantity_left));

                byte materialFlags = 0;
                if (m.is_stock) materialFlags |= 0b0000_0001;
                ms.WriteByte(materialFlags);
            }

            WriteVarUInt(ms, (ulong)referenceInputs.Count);
            foreach (var input in referenceInputs)
            {
                WriteNullableString(ms, input.input_code);
                WriteNullableString(ms, input.input_name);
                WriteNullableString(ms, input.unit);
                WriteVarUInt(ms, ToScaledUInt64(input.quantity_used));
                WriteVarUInt(ms, ToScaledUInt64(input.quantity_left));
            }

            WriteVarUInt(ms, (ulong)outputs.Count);
            foreach (var output in outputs)
            {
                WriteNullableString(ms, output.output_code);
                WriteNullableString(ms, output.output_name);
                WriteNullableString(ms, output.unit);
                WriteVarUInt(ms, ToScaledUInt64(output.quantity_good));
                WriteVarUInt(ms, ToScaledUInt64(output.quantity_bad));
            }

            return ms.ToArray();
        }

        private TaskQrTokenPayloadDto ParseBody(byte[] body)
        {
            using var ms = new MemoryStream(body);

            var version = ms.ReadByte();

            if (version == 1)
                return ParseBodyV1(ms);

            if (version == TokenVersion)
                return ParseBodyV2(ms);

            throw new InvalidOperationException($"Unsupported token version: {version}");
        }

        private TaskQrTokenPayloadDto ParseBodyV1(MemoryStream ms)
        {
            var taskId = (int)ReadVarUInt(ms);
            var qtyGood = (int)ReadVarUInt(ms);
            var expUnix = (long)ReadVarUInt(ms);
            var materialCount = (int)ReadVarUInt(ms);

            var materials = new List<TaskMaterialUsageInputDto>(materialCount);

            for (var i = 0; i < materialCount; i++)
            {
                var materialId = (int)ReadVarUInt(ms);
                var quantityUsed = FromScaledUInt64(ReadVarUInt(ms));
                var quantityLeft = FromScaledUInt64(ReadVarUInt(ms));

                var flag = ms.ReadByte();
                if (flag < 0)
                    throw new EndOfStreamException("Unexpected end of token");

                materials.Add(new TaskMaterialUsageInputDto
                {
                    material_id = materialId,
                    quantity_used = quantityUsed,
                    quantity_left = quantityLeft,
                    is_stock = (flag & 0b0000_0001) != 0
                });
            }

            if (ms.Position != ms.Length)
                throw new InvalidOperationException("Token has unexpected trailing bytes");

            return new TaskQrTokenPayloadDto
            {
                task_id = taskId,
                qty_good = qtyGood,
                exp_unix = expUnix,
                use_manual_input = false,
                reason = null,
                report_image_url = null,
                materials = materials,
                reference_inputs = new List<TaskReferenceUsageInputDto>(),
                outputs = new List<TaskOutputReportDto>()
            };
        }

        private TaskQrTokenPayloadDto ParseBodyV2(MemoryStream ms)
        {
            var taskId = (int)ReadVarUInt(ms);
            var qtyGood = (int)ReadVarUInt(ms);
            var expUnix = (long)ReadVarUInt(ms);

            var flags = ms.ReadByte();
            if (flags < 0)
                throw new EndOfStreamException("Unexpected end of token");

            var useManualInput = (flags & 0b0000_0001) != 0;

            var reason = ReadNullableString(ms);
            var reportImageUrl = ReadNullableString(ms);

            var materialCount = (int)ReadVarUInt(ms);
            var materials = new List<TaskMaterialUsageInputDto>(materialCount);

            for (var i = 0; i < materialCount; i++)
            {
                var materialId = (int)ReadVarUInt(ms);
                var quantityUsed = FromScaledUInt64(ReadVarUInt(ms));
                var quantityLeft = FromScaledUInt64(ReadVarUInt(ms));

                var materialFlags = ms.ReadByte();
                if (materialFlags < 0)
                    throw new EndOfStreamException("Unexpected end of token");

                materials.Add(new TaskMaterialUsageInputDto
                {
                    material_id = materialId,
                    quantity_used = quantityUsed,
                    quantity_left = quantityLeft,
                    is_stock = (materialFlags & 0b0000_0001) != 0
                });
            }

            var referenceInputCount = (int)ReadVarUInt(ms);
            var referenceInputs = new List<TaskReferenceUsageInputDto>(referenceInputCount);

            for (var i = 0; i < referenceInputCount; i++)
            {
                referenceInputs.Add(new TaskReferenceUsageInputDto
                {
                    input_code = ReadNullableString(ms) ?? "",
                    input_name = ReadNullableString(ms),
                    unit = ReadNullableString(ms),
                    quantity_used = FromScaledUInt64(ReadVarUInt(ms)),
                    quantity_left = FromScaledUInt64(ReadVarUInt(ms))
                });
            }

            var outputCount = (int)ReadVarUInt(ms);
            var outputs = new List<TaskOutputReportDto>(outputCount);

            for (var i = 0; i < outputCount; i++)
            {
                outputs.Add(new TaskOutputReportDto
                {
                    output_code = ReadNullableString(ms) ?? "",
                    output_name = ReadNullableString(ms),
                    unit = ReadNullableString(ms),
                    quantity_good = FromScaledUInt64(ReadVarUInt(ms)),
                    quantity_bad = FromScaledUInt64(ReadVarUInt(ms))
                });
            }

            if (ms.Position != ms.Length)
                throw new InvalidOperationException("Token has unexpected trailing bytes");

            return new TaskQrTokenPayloadDto
            {
                task_id = taskId,
                qty_good = qtyGood,
                exp_unix = expUnix,
                use_manual_input = useManualInput,
                reason = reason,
                report_image_url = reportImageUrl,
                materials = materials,
                reference_inputs = referenceInputs,
                outputs = outputs
            };
        }


        private byte[] ComputeSignature(byte[] body)
        {
            using var hmac = new HMACSHA256(_secretBytes);
            var full = hmac.ComputeHash(body);

            var shortSig = new byte[SignatureLength];
            Buffer.BlockCopy(full, 0, shortSig, 0, SignatureLength);
            return shortSig;
        }

        private static List<TaskMaterialUsageInputDto> NormalizeMaterials(
            IReadOnlyList<TaskMaterialUsageInputDto>? materials)
        {
            if (materials == null || materials.Count == 0)
                return new List<TaskMaterialUsageInputDto>();

            return materials
                .Select(x => new TaskMaterialUsageInputDto
                {
                    material_id = x.material_id,
                    quantity_used = Math.Round(x.quantity_used, 4),
                    quantity_left = Math.Round(x.quantity_left, 4),
                    is_stock = x.is_stock
                })
                .ToList();
        }

        private static ulong ToScaledUInt64(decimal value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Value must be >= 0");

            var scaled = decimal.Round(value * 10000m, 0, MidpointRounding.AwayFromZero);

            if (scaled > ulong.MaxValue)
                throw new OverflowException("Scaled value too large");

            return (ulong)scaled;
        }

        private static decimal FromScaledUInt64(ulong value)
            => value / 10000m;

        private static void WriteVarUInt(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        private static ulong ReadVarUInt(Stream stream)
        {
            ulong result = 0;
            var shift = 0;

            while (true)
            {
                var b = stream.ReadByte();
                if (b < 0)
                    throw new EndOfStreamException("Unexpected end of stream");

                result |= ((ulong)(b & 0x7F)) << shift;

                if ((b & 0x80) == 0)
                    return result;

                shift += 7;
                if (shift > 63)
                    throw new FormatException("VarUInt too large");
            }
        }
        private static void WriteNullableString(Stream stream, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                WriteVarUInt(stream, 0);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value.Trim());

            // length + 1 để phân biệt null và chuỗi rỗng.
            WriteVarUInt(stream, (ulong)bytes.Length + 1);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string? ReadNullableString(Stream stream)
        {
            var encodedLength = ReadVarUInt(stream);

            if (encodedLength == 0)
                return null;

            var length = checked((int)(encodedLength - 1));

            if (length == 0)
                return "";

            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, length);

            if (read != length)
                throw new EndOfStreamException("Unexpected end of token string");

            return Encoding.UTF8.GetString(buffer);
        }

        private static string? NormalizeTokenText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var text = value.Trim();

            if (maxLength > 0 && text.Length > maxLength)
                text = text[..maxLength];

            return text;
        }

        private static List<TaskReferenceUsageInputDto> NormalizeReferenceInputs(
            IReadOnlyList<TaskReferenceUsageInputDto>? inputs)
        {
            return (inputs ?? Array.Empty<TaskReferenceUsageInputDto>())
                .Where(x => !string.IsNullOrWhiteSpace(x.input_code))
                .Select(x => new TaskReferenceUsageInputDto
                {
                    input_code = x.input_code.Trim(),
                    input_name = NormalizeTokenText(x.input_name, 300),
                    unit = NormalizeTokenText(x.unit, 50),
                    quantity_used = Math.Round(x.quantity_used, 4),
                    quantity_left = Math.Round(x.quantity_left, 4)
                })
                .ToList();
        }

        private static List<TaskOutputReportDto> NormalizeOutputs(
            IReadOnlyList<TaskOutputReportDto>? outputs)
        {
            return (outputs ?? Array.Empty<TaskOutputReportDto>())
                .Where(x => !string.IsNullOrWhiteSpace(x.output_code))
                .Select(x => new TaskOutputReportDto
                {
                    output_code = x.output_code.Trim(),
                    output_name = NormalizeTokenText(x.output_name, 300),
                    unit = NormalizeTokenText(x.unit, 50),
                    quantity_good = Math.Round(x.quantity_good, 4),
                    quantity_bad = Math.Round(x.quantity_bad, 4)
                })
                .ToList();
        }

        private static string Base62Encode(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "0";

            var value = new BigInteger(data, isUnsigned: true, isBigEndian: true);
            if (value == 0)
                return "0";

            var sb = new StringBuilder();

            while (value > 0)
            {
                value = BigInteger.DivRem(value, 62, out var remainder);
                sb.Insert(0, Base62Alphabet[(int)remainder]);
            }

            return sb.ToString();
        }

        private static byte[] Base62Decode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("Empty token");

            BigInteger value = BigInteger.Zero;

            foreach (var ch in text)
            {
                if (!_base62Map.TryGetValue(ch, out var digit))
                    throw new FormatException($"Invalid Base62 char: {ch}");

                value = (value * 62) + digit;
            }

            return value.ToByteArray(isUnsigned: true, isBigEndian: true);
        }
    }
}