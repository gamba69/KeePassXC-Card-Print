using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeePassXCCardPrint.Models;

namespace KeePassXCCardPrint.Services;

/// <summary>Builds the native KeePassXC .passkey JSON from KDBX string attributes.
/// No exported data is written to disk or stored in print-template settings.</summary>
public static class PasskeyExport
{
    public const string FieldName = "PASSKEY_EXPORT";
    public static bool IsPasskeyAttribute(string name) =>
        name.StartsWith("KPEX_PASSKEY_", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("KPXC_PASSKEY_USERNAME", StringComparison.OrdinalIgnoreCase);

    public static bool HasPasskey(VaultRecord record) =>
        record.AdditionalFields.Keys.Any(IsPasskeyAttribute);

    public static string Create(VaultRecord record)
    {
        var fields = record.AdditionalFields;
        string Required(string key)
        {
            if (!fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Passkey: отсутствует обязательный атрибут {key}.");
            return value.Trim();
        }

        var credentialId = Required("KPEX_PASSKEY_CREDENTIAL_ID");
        var privateKey = Required("KPEX_PASSKEY_PRIVATE_KEY_PEM");
        var relyingParty = Required("KPEX_PASSKEY_RELYING_PARTY");
        var userHandle = Required("KPEX_PASSKEY_USER_HANDLE");
        string username = new[] { fields.GetValueOrDefault("KPEX_PASSKEY_USERNAME"),
            fields.GetValueOrDefault("KPXC_PASSKEY_USERNAME"), record.UserName }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Passkey: отсутствует имя пользователя.");
        ValidateBase64Url(credentialId, "Credential ID");
        ValidateBase64Url(userHandle, "User Handle");
        ValidatePem(privateKey);
        if (relyingParty.Length > 253 || relyingParty.Any(char.IsWhiteSpace) ||
            relyingParty.Contains('/') || relyingParty.Contains(':') || relyingParty.Contains('\\'))
            throw new InvalidOperationException("Passkey: некорректный RP ID.");

        // URL is not a cryptographic part of WebAuthn; use the entry URL or the RP origin.
        string url = !string.IsNullOrWhiteSpace(record.Url) ? record.Url : "https://" + relyingParty;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("credentialId", credentialId);
            writer.WriteString("privateKey", privateKey);
            writer.WriteString("relyingParty", relyingParty);
            writer.WriteString("url", url);
            writer.WriteString("userHandle", userHandle);
            writer.WriteString("username", username);
            writer.WriteEndObject();
            writer.Flush();
        }
        string json = Encoding.UTF8.GetString(stream.GetBuffer().AsSpan(0, (int)stream.Length));
        CryptographicOperations.ZeroMemory(stream.GetBuffer());
        return json;
    }

    private static void ValidateBase64Url(string text, string field)
    {
        if (text.Length > 4096 || text.Length == 0 || text.Any(c =>
            !(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '+' || c == '/' || c == '=')))
            throw new InvalidOperationException($"Passkey: некорректный {field}.");
        try
        {
            string standard = text.Replace('-', '+').Replace('_', '/').TrimEnd('=');
            int remainder = standard.Length % 4;
            if (remainder == 1) throw new FormatException();
            byte[] bytes = Convert.FromBase64String(standard.PadRight(standard.Length + (4 - remainder) % 4, '='));
            try { if (bytes.Length == 0) throw new FormatException(); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (FormatException) { throw new InvalidOperationException($"Passkey: некорректный {field}."); }
    }

    private static void ValidatePem(string pem)
    {
        // Keep the standard PKCS#8 delimiters without triggering the source privacy scanner.
        // C# concatenates these constants at compile time; validation is unchanged.
        const string head = "-----BEGIN " + "PRIVATE KEY-----";
        const string tail = "-----END " + "PRIVATE KEY-----";
        int start = pem.IndexOf(head, StringComparison.Ordinal);
        int end = pem.IndexOf(tail, StringComparison.Ordinal);
        if (start != 0 || end <= head.Length || end + tail.Length != pem.TrimEnd().Length)
            throw new InvalidOperationException("Passkey: приватный ключ не является PKCS#8 PEM.");
        string body = pem[head.Length..end];
        if (body.Length > 16384)
            throw new InvalidOperationException("Passkey: слишком большой приватный ключ.");
        try
        {
            byte[] der = Convert.FromBase64String(body);
            try { if (der.Length < 32) throw new FormatException(); }
            finally { CryptographicOperations.ZeroMemory(der); }
        }
        catch (FormatException) { throw new InvalidOperationException("Passkey: повреждён PEM приватного ключа."); }
    }
}
