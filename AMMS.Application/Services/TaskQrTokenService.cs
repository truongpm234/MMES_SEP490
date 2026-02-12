using AMMS.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

public class TaskQrTokenService : ITaskQrTokenService
{
    private readonly byte[] _secret;
    private const int PayloadLen = 8;

    // signature truncated
    private const int SigLen = 4;
    private const int TotalLen = PayloadLen + SigLen;

    private static readonly DateTimeOffset Epoch2020 =
        new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public TaskQrTokenService(IConfiguration config)
    {
        var s = config["Qr:Secret"] ?? throw new Exception("Missing Qr:Secret");
        _secret = Encoding.UTF8.GetBytes(s);
    }

    public string CreateToken(int taskId, int qtyGood, TimeSpan ttl)
    {
        // 2 bytes
        if (taskId < 0 || taskId > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(taskId), "taskId must be <= 65535");

        // 2 bytes
        if (qtyGood < 0 || qtyGood > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(qtyGood), "qtyGood must be <= 65535");

        // ttl stored in minutes (1 byte)
        var ttlMin = (int)Math.Ceiling(ttl.TotalMinutes);
        if (ttlMin <= 0) ttlMin = 1;
        if (ttlMin > 255)
            throw new ArgumentOutOfRangeException(nameof(ttl), "ttl must be <= 255 minutes for 20-char token");

        var issuedMin = (int)((DateTimeOffset.UtcNow - Epoch2020).TotalMinutes);
        if (issuedMin < 0 || issuedMin > 0xFFFFFF)
            throw new InvalidOperationException("issuedMin overflow - epoch too old/new");

        Span<byte> payload = stackalloc byte[PayloadLen];

        // [0..1] taskId
        WriteUInt16BE(payload, 0, (ushort)taskId);

        // [2..4] issuedMin
        WriteUInt24BE(payload, 2, issuedMin);

        // [5] ttlMin
        payload[5] = (byte)ttlMin;

        // [6..7] qtyGood
        WriteUInt16BE(payload, 6, (ushort)qtyGood);

        var sigFull = HmacSha256(payload);
        Span<byte> sig = stackalloc byte[SigLen];
        sigFull.AsSpan(0, SigLen).CopyTo(sig);

        Span<byte> tokenBytes = stackalloc byte[TotalLen];
        payload.CopyTo(tokenBytes);
        sig.CopyTo(tokenBytes.Slice(PayloadLen));

        return Base32Crockford.Encode(tokenBytes.ToArray());
    }

    public bool TryValidate(string token, out int taskId, out int qtyGood, out string reason)
    {
        taskId = 0;
        qtyGood = 0;
        reason = "";

        byte[] raw;
        try { raw = Base32Crockford.Decode(token); }
        catch { reason = "Invalid token encoding"; return false; }

        if (raw.Length != TotalLen)
        {
            reason = "Invalid token length";
            return false;
        }

        var payload = raw.AsSpan(0, PayloadLen);
        var sig = raw.AsSpan(PayloadLen, SigLen);

        // verify signature
        var expectedFull = HmacSha256(payload);
        Span<byte> expected = stackalloc byte[SigLen];
        expectedFull.AsSpan(0, SigLen).CopyTo(expected);

        if (!CryptographicOperations.FixedTimeEquals(sig, expected))
        {
            reason = "Bad signature";
            return false;
        }

        taskId = ReadUInt16BE(payload, 0);
        var issuedMin = ReadUInt24BE(payload, 2);
        var ttlMin = payload[5];
        qtyGood = ReadUInt16BE(payload, 6);

        // expiry
        var issuedAt = Epoch2020.AddMinutes(issuedMin);
        var expiresAt = issuedAt.AddMinutes(ttlMin);

        if (DateTimeOffset.UtcNow > expiresAt)
        {
            reason = "Token expired";
            return false;
        }

        return true;
    }

    private byte[] HmacSha256(ReadOnlySpan<byte> data)
    {
        using var hmac = new HMACSHA256(_secret);
        return hmac.ComputeHash(data.ToArray());
    }

    private static void WriteUInt16BE(Span<byte> buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value >> 8);
        buf[offset + 1] = (byte)value;
    }

    private static int ReadUInt16BE(ReadOnlySpan<byte> buf, int offset)
        => (buf[offset] << 8) | buf[offset + 1];

    private static void WriteUInt24BE(Span<byte> buf, int offset, int value)
    {
        buf[offset + 0] = (byte)(value >> 16);
        buf[offset + 1] = (byte)(value >> 8);
        buf[offset + 2] = (byte)value;
    }

    private static int ReadUInt24BE(ReadOnlySpan<byte> buf, int offset)
        => (buf[offset] << 16) | (buf[offset + 1] << 8) | buf[offset + 2];
}

public static class Base32Crockford
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Encode(byte[] data)
    {
        if (data == null || data.Length == 0) return "";

        int bits = 0;
        int value = 0;
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);

        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;

            while (bits >= 5)
            {
                int idx = (value >> (bits - 5)) & 31;
                bits -= 5;
                sb.Append(Alphabet[idx]);
            }
        }

        if (bits > 0)
        {
            int idx = (value << (5 - bits)) & 31;
            sb.Append(Alphabet[idx]);
        }

        return sb.ToString();
    }

    public static byte[] Decode(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<byte>();

        s = s.Trim().ToUpperInvariant();
        int bits = 0;
        int value = 0;
        var bytes = new List<byte>();

        foreach (var ch0 in s)
        {
            var ch = ch0;
            if (ch == 'O') ch = '0';
            if (ch == 'I' || ch == 'L') ch = '1';

            int idx = Alphabet.IndexOf(ch);
            if (idx < 0) throw new FormatException("Invalid Base32 character");

            value = (value << 5) | idx;
            bits += 5;

            if (bits >= 8)
            {
                bytes.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return bytes.ToArray();
    }
}
