using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeePassXCCardPrint.Services;

internal enum ScannerKind { Totp, Passkey, Url, Text, Binary, Invalid }

internal sealed class ScannerPayload : IDisposable
{
    internal ScannerKind Kind { get; private init; }
    internal string Text { get; private init; } = "";
    internal byte[] Bytes { get; private init; } = [];
    internal string Account { get; private init; } = "";
    internal string Issuer { get; private init; } = "";
    internal string Algorithm { get; private init; } = "SHA1";
    internal int Digits { get; private init; } = 6;
    internal int Period { get; private init; } = 30;
    internal bool Steam { get; private init; }
    internal byte[] Secret { get; private init; } = [];
    internal string Rp { get; private init; } = "";
    internal string CredentialId { get; private init; } = "";
    internal string UserHandle { get; private init; } = "";
    internal string PrivateKey { get; private init; } = "";
    internal bool Allowed(bool all) => Kind is ScannerKind.Totp or ScannerKind.Passkey || all && Kind != ScannerKind.Invalid;
    internal string Label => Kind switch { ScannerKind.Totp => "TOTP", ScannerKind.Passkey => "Passkey", ScannerKind.Url => "URL", ScannerKind.Text => "Text", ScannerKind.Binary => "Binary", _ => "Invalid" };
    internal string Fingerprint => Convert.ToHexString(SHA256.HashData(Bytes));

    internal static ScannerPayload Parse(string text, byte[]? binary = null)
    {
        if (binary is not null) return new() { Kind = ScannerKind.Binary, Bytes = binary };
        var bytes = Encoding.UTF8.GetBytes(text);
        if (text.StartsWith("otpauth:", StringComparison.OrdinalIgnoreCase))
        {
            try { return ParseTotp(text, bytes); }
            catch (Exception e) when (e is FormatException or ArgumentException or OverflowException) { return new() { Kind = ScannerKind.Invalid, Bytes = bytes }; }
        }
        if (text.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && (root.TryGetProperty("credentialId", out _) || root.TryGetProperty("privateKey", out _) && root.TryGetProperty("relyingParty", out _)))
                {
                    string Required(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()) ? p.GetString()! : throw new FormatException();
                    string id = Required("credentialId"), key = Required("privateKey"), rp = Required("relyingParty"), handle = Required("userHandle"), user = Required("username");
                    _ = Required("url");
                    PasskeyExport.ValidateBase64Url(id, "Credential ID");
                    PasskeyExport.ValidateBase64Url(handle, "User Handle");
                    PasskeyExport.ValidatePem(key);
                    if (rp.Length > 253 || rp.Any(char.IsWhiteSpace) || rp.IndexOfAny(['/', ':', '\\']) >= 0) throw new FormatException();
                    return new() { Kind = ScannerKind.Passkey, Text = text, Bytes = bytes, Account = user, Rp = rp, CredentialId = id, UserHandle = handle, PrivateKey = key };
                }
            }
            catch (JsonException) { /* ordinary text, unless it claims to be a passkey */
                if (text.Contains("\"credentialId\"", StringComparison.Ordinal) && text.Contains("\"privateKey\"", StringComparison.Ordinal)) return new() { Kind = ScannerKind.Invalid, Bytes = bytes };
            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException) { return new() { Kind = ScannerKind.Invalid, Bytes = bytes }; }
        }
        var kind = Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host) ? ScannerKind.Url : ScannerKind.Text;
        return new() { Kind = kind, Text = text, Bytes = bytes };
    }

    private static ScannerPayload ParseTotp(string text, byte[] bytes)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "otpauth" || uri.Host != "totp") throw new FormatException();
        var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in uri.Query.TrimStart('?').Split('&'))
        {
            int i = part.IndexOf('=');
            if (i > 0 && !q.TryAdd(Uri.UnescapeDataString(part[..i]), Uri.UnescapeDataString(part[(i+1)..].Replace('+', ' ')))) throw new FormatException();
        }
        if (!q.TryGetValue("secret", out var seed)) throw new FormatException();
        string algorithm = q.GetValueOrDefault("algorithm", "SHA1").ToUpperInvariant().Replace("-", "");
        if (algorithm is not ("SHA1" or "SHA256" or "SHA512")) throw new FormatException();
        if (!int.TryParse(q.GetValueOrDefault("digits", "6"), out int digits) || digits is < 5 or > 10 || !int.TryParse(q.GetValueOrDefault("period", "30"), out int period) || period is < 1 or > 86400) throw new FormatException();
        bool steam = q.GetValueOrDefault("encoder", "").Equals("steam", StringComparison.OrdinalIgnoreCase);
        if (steam && (digits != 5 || algorithm != "SHA1")) throw new FormatException();
        string account = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        string issuer = q.GetValueOrDefault("issuer", "");
        int colon = account.IndexOf(':');
        if (colon >= 0) { if (issuer.Length == 0) issuer = account[..colon]; account = account[(colon+1)..]; }
        return new() { Kind = ScannerKind.Totp, Text = text, Bytes = bytes, Account = account, Issuer = issuer, Algorithm = algorithm, Digits = digits, Period = period, Steam = steam, Secret = DecodeBase32(seed) };
    }

    private static byte[] DecodeBase32(string input)
    {
        string s = input.Trim().TrimEnd('=').ToUpperInvariant();
        if (s.Length is < 2 or > 4096 || s.Length % 8 is 1 or 3 or 6) throw new FormatException();
        byte[] bytes = new byte[s.Length * 5 / 8];
        int accumulator = 0, bits = 0, offset = 0;
        try
        {
            foreach (char c in s)
            {
                int n = c is >= 'A' and <= 'Z' ? c - 'A' : c is >= '2' and <= '7' ? c - '2' + 26 : -1;
                if (n < 0) throw new FormatException();
                accumulator = (accumulator << 5) | n; bits += 5;
                if (bits >= 8) { bits -= 8; bytes[offset++] = (byte)(accumulator >> bits); accumulator &= (1 << bits) - 1; }
            }
            if (accumulator != 0) throw new FormatException();
            return bytes;
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }

    internal string Code(long unixSeconds)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, unixSeconds / Period);
        byte[] hash = Algorithm switch { "SHA256" => HMACSHA256.HashData(Secret, counter), "SHA512" => HMACSHA512.HashData(Secret, counter), _ => HMACSHA1.HashData(Secret, counter) };
        try
        {
            uint n = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(hash[^1] & 15, 4)) & 0x7fffffff;
            if (Steam)
            {
                const string alphabet = "23456789BCDFGHJKMNPQRTVWXY";
                Span<char> code = stackalloc char[5];
                for (int i = 0; i < code.Length; i++) { code[i] = alphabet[(int)(n % alphabet.Length)]; n /= (uint)alphabet.Length; }
                return new string(code);
            }
            ulong modulus = 1;
            for (int i = 0; i < Digits; i++) modulus *= 10;
            return (n % modulus).ToString("D" + Digits, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }
    public void Dispose() { CryptographicOperations.ZeroMemory(Bytes); CryptographicOperations.ZeroMemory(Secret); }
}

internal sealed record ScannerHit(ScannerPayload Payload, float X, float Y, float Width, float Height) : IDisposable
{
    public void Dispose() => Payload.Dispose();
}
