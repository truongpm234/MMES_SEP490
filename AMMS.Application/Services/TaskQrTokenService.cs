using System.IO.Compression;
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
        /*
         * V3:
         * - Token output chỉ gồm SỐ + CHỮ HOA.
         * - Dùng Crockford Base32 để tránh ký tự dễ nhầm: I, L, O, U.
         * - Payload compact hơn V2.
         * - Payload dài sẽ được Brotli compress.
         * - Không cần DB, token vẫn tự chứa đủ dữ liệu để máy khác quét finish.
         */
        private const byte TokenVersion = 3;

        /*
         * 8 bytes = 64-bit HMAC truncated.
         * Ngắn hơn bản cũ 10 bytes.
         * Nếu muốn bảo mật mạnh hơn nhưng token dài hơn, đổi lại 10.
         */
        private const int SignatureLength = 8;

        private const byte EnvelopeFlagCompressed = 0b0000_0001;

        private const byte PayloadFlagManualInput = 0b0000_0001;
        private const byte PayloadFlagHasReason = 0b0000_0010;
        private const byte PayloadFlagHasReportImageUrl = 0b0000_0100;
        private const byte PayloadFlagHasMaterials = 0b0000_1000;
        private const byte PayloadFlagHasReferenceInputs = 0b0001_0000;
        private const byte PayloadFlagHasOutputs = 0b0010_0000;

        /*
         * Crockford Base32:
         * - Toàn chữ hoa + số.
         * - Không có I, L, O, U để hạn chế máy quét/người nhập nhầm.
         */
        private const string Base32Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        /*
         * Legacy V1/V2 giữ để token cũ chưa hết hạn vẫn finish được.
         * Token mới sẽ không dùng Base62 nữa.
         */
        private const byte LegacyTokenVersionV2 = 2;
        private const int LegacySignatureLength = 10;
        private const string LegacyBase62Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        private readonly byte[] _secretBytes;

        private static readonly Dictionary<char, int> _base32Map = BuildBase32Map();

        private static readonly Dictionary<char, int> _legacyBase62Map = LegacyBase62Alphabet
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

            if (ttl <= TimeSpan.Zero)
                throw new ArgumentException("ttl must be > 0");

            var normalizedMaterials = NormalizeMaterials(materials);
            var normalizedRefs = NormalizeReferenceInputs(referenceInputs);
            var normalizedOutputs = NormalizeOutputs(outputs);

            var expUnixMinutes = DateTimeOffset.UtcNow
                .Add(ttl)
                .ToUnixTimeSeconds() / 60;

            var payloadBody = BuildPayloadV3(
                taskId: taskId,
                qtyGood: qtyGood,
                expUnixMinutes: expUnixMinutes,
                materials: normalizedMaterials,
                useManualInput: useManualInput,
                reason: NormalizeTokenText(reason, 1000),
                reportImageUrl: NormalizeTokenText(reportImageUrl, 8000),
                referenceInputs: normalizedRefs,
                outputs: normalizedOutputs);

            var envelopeFlags = (byte)0;
            var envelopeContent = payloadBody;

            /*
             * Chỉ compress khi thật sự ngắn hơn.
             * Với token rất nhỏ, Brotli header có thể làm dài hơn.
             */
            if (payloadBody.Length >= 48)
            {
                var compressed = BrotliCompress(payloadBody);

                if (compressed.Length + 2 < payloadBody.Length)
                {
                    envelopeFlags |= EnvelopeFlagCompressed;
                    envelopeContent = compressed;
                }
            }

            var envelope = new byte[2 + envelopeContent.Length];
            envelope[0] = TokenVersion;
            envelope[1] = envelopeFlags;

            Buffer.BlockCopy(
                envelopeContent,
                0,
                envelope,
                2,
                envelopeContent.Length);

            var sig = ComputeSignature(envelope, SignatureLength);

            var raw = new byte[envelope.Length + sig.Length];

            Buffer.BlockCopy(envelope, 0, raw, 0, envelope.Length);
            Buffer.BlockCopy(sig, 0, raw, envelope.Length, sig.Length);

            /*
             * Kết quả chỉ có:
             * 0123456789ABCDEFGHJKMNPQRSTVWXYZ
             */
            return Base32Encode(raw);
        }

        public bool TryValidate(
            string token,
            out int taskId,
            out int qtyGood,
            out string reason)
        {
            taskId = 0;
            qtyGood = 0;

            if (!TryValidate(token, out TaskQrTokenPayloadDto payload, out reason))
                return false;

            taskId = payload.task_id;
            qtyGood = payload.qty_good;

            return true;
        }

        public bool TryValidate(
            string token,
            out TaskQrTokenPayloadDto payload,
            out string reason)
        {
            payload = new TaskQrTokenPayloadDto();
            reason = "Invalid token";

            if (string.IsNullOrWhiteSpace(token))
            {
                reason = "Token is empty";
                return false;
            }

            /*
             * 1. Ưu tiên validate token V3 mới:
             * - Cho phép FE/máy quét input có khoảng trắng hoặc dấu -,
             *   backend sẽ bỏ đi.
             * - Output token vẫn không có dấu -.
             */
            var normalizedV3 = NormalizeScannedV3Token(token);

            if (TryValidateV3(
                    normalizedV3,
                    out payload,
                    out reason,
                    out var recognizedV3))
            {
                EnsurePayloadCollections(payload);
                return true;
            }

            /*
             * Nếu token đúng format V3 và chữ ký hợp lệ nhưng hết hạn/payload lỗi,
             * không fallback sang legacy nữa.
             */
            if (recognizedV3)
            {
                EnsurePayloadCollections(payload);
                return false;
            }

            /*
             * 2. Fallback legacy để token V1/V2 cũ chưa hết hạn vẫn dùng được.
             */
            if (TryValidateLegacyBase62(
                    token.Trim(),
                    out payload,
                    out reason))
            {
                EnsurePayloadCollections(payload);
                return true;
            }

            EnsurePayloadCollections(payload);
            return false;
        }

        private bool TryValidateV3(
            string token,
            out TaskQrTokenPayloadDto payload,
            out string reason,
            out bool recognizedV3)
        {
            payload = new TaskQrTokenPayloadDto();
            reason = "Invalid V3 token";
            recognizedV3 = false;

            if (string.IsNullOrWhiteSpace(token))
            {
                reason = "Token is empty";
                return false;
            }

            byte[] raw;

            try
            {
                raw = Base32Decode(token);
            }
            catch
            {
                reason = "Cannot decode V3 token";
                return false;
            }

            if (raw.Length <= 2 + SignatureLength)
            {
                reason = "V3 token too short";
                return false;
            }

            var envelopeLength = raw.Length - SignatureLength;

            var envelope = new byte[envelopeLength];
            var givenSig = new byte[SignatureLength];

            Buffer.BlockCopy(raw, 0, envelope, 0, envelopeLength);
            Buffer.BlockCopy(raw, envelopeLength, givenSig, 0, SignatureLength);

            if (envelope[0] != TokenVersion)
            {
                reason = "Not V3 token";
                return false;
            }

            var expectedSig = ComputeSignature(envelope, SignatureLength);

            if (!CryptographicOperations.FixedTimeEquals(givenSig, expectedSig))
            {
                reason = "V3 token signature is invalid";
                return false;
            }

            recognizedV3 = true;

            try
            {
                var envelopeFlags = envelope[1];

                var content = new byte[envelope.Length - 2];

                Buffer.BlockCopy(
                    envelope,
                    2,
                    content,
                    0,
                    content.Length);

                var payloadBytes = (envelopeFlags & EnvelopeFlagCompressed) != 0
                    ? BrotliDecompress(content)
                    : content;

                payload = ParsePayloadV3(payloadBytes);
            }
            catch
            {
                reason = "V3 token payload is invalid";
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

            reason = "";
            return true;
        }

        private bool TryValidateLegacyBase62(
            string token,
            out TaskQrTokenPayloadDto payload,
            out string reason)
        {
            payload = new TaskQrTokenPayloadDto();
            reason = "Invalid legacy token";

            if (string.IsNullOrWhiteSpace(token))
            {
                reason = "Token is empty";
                return false;
            }

            for (var i = 0; i < token.Length; i++)
            {
                if (!_legacyBase62Map.ContainsKey(token[i]))
                {
                    reason = "Legacy token contains invalid characters";
                    return false;
                }
            }

            byte[] raw;

            try
            {
                raw = LegacyBase62Decode(token);
            }
            catch
            {
                reason = "Cannot decode legacy token";
                return false;
            }

            if (raw.Length <= LegacySignatureLength + 1)
            {
                reason = "Legacy token too short";
                return false;
            }

            var bodyLength = raw.Length - LegacySignatureLength;

            var body = new byte[bodyLength];
            var givenSig = new byte[LegacySignatureLength];

            Buffer.BlockCopy(raw, 0, body, 0, bodyLength);
            Buffer.BlockCopy(raw, bodyLength, givenSig, 0, LegacySignatureLength);

            var expectedSig = ComputeSignature(body, LegacySignatureLength);

            if (!CryptographicOperations.FixedTimeEquals(givenSig, expectedSig))
            {
                reason = "Legacy token signature is invalid";
                return false;
            }

            try
            {
                payload = ParseLegacyBody(body);
            }
            catch
            {
                reason = "Legacy token payload is invalid";
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

            reason = "";
            return true;
        }

        private byte[] BuildPayloadV3(
            int taskId,
            int qtyGood,
            long expUnixMinutes,
            List<TaskMaterialUsageInputDto> materials,
            bool useManualInput,
            string? reason,
            string? reportImageUrl,
            List<TaskReferenceUsageInputDto> referenceInputs,
            List<TaskOutputReportDto> outputs)
        {
            using var ms = new MemoryStream();

            WriteVarUInt(ms, (ulong)taskId);
            WriteVarUInt(ms, (ulong)qtyGood);
            WriteVarUInt(ms, (ulong)expUnixMinutes);

            byte flags = 0;

            if (useManualInput)
                flags |= PayloadFlagManualInput;

            if (!string.IsNullOrWhiteSpace(reason))
                flags |= PayloadFlagHasReason;

            if (!string.IsNullOrWhiteSpace(reportImageUrl))
                flags |= PayloadFlagHasReportImageUrl;

            if (materials.Count > 0)
                flags |= PayloadFlagHasMaterials;

            if (referenceInputs.Count > 0)
                flags |= PayloadFlagHasReferenceInputs;

            if (outputs.Count > 0)
                flags |= PayloadFlagHasOutputs;

            ms.WriteByte(flags);

            if ((flags & PayloadFlagHasReason) != 0)
                WriteNullableString(ms, reason);

            if ((flags & PayloadFlagHasReportImageUrl) != 0)
                WriteNullableString(ms, reportImageUrl);

            if ((flags & PayloadFlagHasMaterials) != 0)
            {
                WriteVarUInt(ms, (ulong)materials.Count);

                foreach (var m in materials)
                {
                    if (m.material_id <= 0)
                        throw new ArgumentException("material_id must be > 0");

                    WriteVarUInt(ms, (ulong)m.material_id);
                    WriteVarUInt(ms, ToScaledUInt64(m.quantity_used));
                    WriteVarUInt(ms, ToScaledUInt64(m.quantity_left));

                    byte materialFlags = 0;

                    if (m.is_stock)
                        materialFlags |= 0b0000_0001;

                    ms.WriteByte(materialFlags);
                }
            }

            if ((flags & PayloadFlagHasReferenceInputs) != 0)
            {
                WriteVarUInt(ms, (ulong)referenceInputs.Count);

                foreach (var input in referenceInputs)
                {
                    WriteNullableString(ms, input.input_code);
                    WriteNullableString(ms, input.input_name);
                    WriteNullableString(ms, input.unit);
                    WriteVarUInt(ms, ToScaledUInt64(input.quantity_used));
                    WriteVarUInt(ms, ToScaledUInt64(input.quantity_left));
                }
            }

            if ((flags & PayloadFlagHasOutputs) != 0)
            {
                WriteVarUInt(ms, (ulong)outputs.Count);

                foreach (var output in outputs)
                {
                    WriteNullableString(ms, output.output_code);
                    WriteNullableString(ms, output.output_name);
                    WriteNullableString(ms, output.unit);
                    WriteVarUInt(ms, ToScaledUInt64(output.quantity_good));
                    WriteVarUInt(ms, ToScaledUInt64(output.quantity_bad));
                }
            }

            return ms.ToArray();
        }

        private TaskQrTokenPayloadDto ParsePayloadV3(byte[] body)
        {
            using var ms = new MemoryStream(body);

            var taskId = (int)ReadVarUInt(ms);
            var qtyGood = (int)ReadVarUInt(ms);
            var expUnixMinutes = (long)ReadVarUInt(ms);

            var flags = ms.ReadByte();

            if (flags < 0)
                throw new EndOfStreamException("Unexpected end of token");

            var reason = (flags & PayloadFlagHasReason) != 0
                ? ReadNullableString(ms)
                : null;

            var reportImageUrl = (flags & PayloadFlagHasReportImageUrl) != 0
                ? ReadNullableString(ms)
                : null;

            var materials = new List<TaskMaterialUsageInputDto>();

            if ((flags & PayloadFlagHasMaterials) != 0)
            {
                var materialCount = (int)ReadVarUInt(ms);

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
            }

            var referenceInputs = new List<TaskReferenceUsageInputDto>();

            if ((flags & PayloadFlagHasReferenceInputs) != 0)
            {
                var referenceInputCount = (int)ReadVarUInt(ms);

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
            }

            var outputs = new List<TaskOutputReportDto>();

            if ((flags & PayloadFlagHasOutputs) != 0)
            {
                var outputCount = (int)ReadVarUInt(ms);

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
            }

            if (ms.Position != ms.Length)
                throw new InvalidOperationException("Token has unexpected trailing bytes");

            return new TaskQrTokenPayloadDto
            {
                task_id = taskId,
                qty_good = qtyGood,
                exp_unix = expUnixMinutes * 60,
                use_manual_input = (flags & PayloadFlagManualInput) != 0,
                reason = reason,
                report_image_url = reportImageUrl,
                materials = materials,
                reference_inputs = referenceInputs,
                outputs = outputs
            };
        }

        private TaskQrTokenPayloadDto ParseLegacyBody(byte[] body)
        {
            using var ms = new MemoryStream(body);

            var version = ms.ReadByte();

            if (version == 1)
                return ParseLegacyBodyV1(ms);

            if (version == LegacyTokenVersionV2)
                return ParseLegacyBodyV2(ms);

            throw new InvalidOperationException($"Unsupported legacy token version: {version}");
        }

        private TaskQrTokenPayloadDto ParseLegacyBodyV1(MemoryStream ms)
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

        private TaskQrTokenPayloadDto ParseLegacyBodyV2(MemoryStream ms)
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

        private byte[] ComputeSignature(byte[] body, int signatureLength)
        {
            using var hmac = new HMACSHA256(_secretBytes);
            var full = hmac.ComputeHash(body);

            var shortSig = new byte[signatureLength];

            Buffer.BlockCopy(full, 0, shortSig, 0, signatureLength);

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

        private static void EnsurePayloadCollections(TaskQrTokenPayloadDto payload)
        {
            payload.materials ??= new List<TaskMaterialUsageInputDto>();
            payload.reference_inputs ??= new List<TaskReferenceUsageInputDto>();
            payload.outputs ??= new List<TaskOutputReportDto>();
        }

        private static string NormalizeScannedV3Token(string token)
        {
            return new string(
                token
                    .Where(ch => !char.IsWhiteSpace(ch) && ch != '-')
                    .Select(char.ToUpperInvariant)
                    .ToArray());
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

        private static ulong ToScaledUInt64(decimal value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Value must be >= 0");

            var scaled = decimal.Round(
                value * 10000m,
                0,
                MidpointRounding.AwayFromZero);

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

            /*
             * length + 1 để phân biệt null và chuỗi rỗng.
             */
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

        private static byte[] BrotliCompress(byte[] input)
        {
            using var output = new MemoryStream();

            using (var brotli = new BrotliStream(
                       output,
                       CompressionLevel.Optimal,
                       leaveOpen: true))
            {
                brotli.Write(input, 0, input.Length);
            }

            return output.ToArray();
        }

        private static byte[] BrotliDecompress(byte[] input)
        {
            using var source = new MemoryStream(input);
            using var brotli = new BrotliStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();

            brotli.CopyTo(output);

            return output.ToArray();
        }

        private static Dictionary<char, int> BuildBase32Map()
        {
            var map = Base32Alphabet
                .Select((ch, idx) => new { ch, idx })
                .ToDictionary(x => x.ch, x => x.idx);

            /*
             * Decode tolerant:
             * - Máy/người nhập O thì hiểu là 0.
             * - I/L thì hiểu là 1.
             * Token generate ra sẽ không bao giờ dùng O/I/L.
             */
            map['O'] = map['0'];
            map['I'] = map['1'];
            map['L'] = map['1'];

            return map;
        }

        private static string Base32Encode(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "";

            var sb = new StringBuilder();
            var bitBuffer = 0;
            var bitCount = 0;

            foreach (var b in data)
            {
                bitBuffer = (bitBuffer << 8) | b;
                bitCount += 8;

                while (bitCount >= 5)
                {
                    var index = (bitBuffer >> (bitCount - 5)) & 31;
                    sb.Append(Base32Alphabet[index]);
                    bitCount -= 5;
                }
            }

            if (bitCount > 0)
            {
                var index = (bitBuffer << (5 - bitCount)) & 31;
                sb.Append(Base32Alphabet[index]);
            }

            return sb.ToString();
        }

        private static byte[] Base32Decode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("Empty token");

            var bytes = new List<byte>();
            var bitBuffer = 0;
            var bitCount = 0;

            foreach (var raw in text)
            {
                var ch = char.ToUpperInvariant(raw);

                if (!_base32Map.TryGetValue(ch, out var value))
                    throw new FormatException($"Invalid Base32 char: {raw}");

                bitBuffer = (bitBuffer << 5) | value;
                bitCount += 5;

                if (bitCount >= 8)
                {
                    var b = (byte)((bitBuffer >> (bitCount - 8)) & 0xFF);
                    bytes.Add(b);
                    bitCount -= 8;
                }
            }

            return bytes.ToArray();
        }

        private static byte[] LegacyBase62Decode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("Empty token");

            BigInteger value = BigInteger.Zero;

            foreach (var ch in text)
            {
                if (!_legacyBase62Map.TryGetValue(ch, out var digit))
                    throw new FormatException($"Invalid Base62 char: {ch}");

                value = (value * 62) + digit;
            }

            return value.ToByteArray(
                isUnsigned: true,
                isBigEndian: true);
        }
    }
}