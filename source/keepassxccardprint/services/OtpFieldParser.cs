namespace KeePassXCCardPrint.Services;

// Preserves the original secret: do not normalize or base32-decode it here.
public static class OtpFieldParser
{
    public static Dictionary<string, string> Normalize(IDictionary<string, string> fields)
    {
        // Preserve every original KeePassXC field. Derived TOTP fields are additive:
        // one-way normalization must never discard a protected secret or override its source.
        var result = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if ((field.Key.Contains("otp", StringComparison.OrdinalIgnoreCase) ||
                 field.Key.Contains("auth", StringComparison.OrdinalIgnoreCase)) &&
                (field.Value.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase) ||
                 field.Value.StartsWith("key=", StringComparison.OrdinalIgnoreCase)))
                ReadOtpUri(field.Value, result,
                    field.Key.Equals("otp", StringComparison.OrdinalIgnoreCase) ? "TOTP" :
                    "TOTP (" + field.Key + ")");
        }
        if (fields.TryGetValue("TOTP Seed", out var secret) && !string.IsNullOrWhiteSpace(secret))
            result["TOTP Secret"] = secret;
        if (fields.TryGetValue("TOTP Settings", out var settings))
        {
            var parts = settings.Split(';');
            if (parts.Length == 2 && int.TryParse(parts[0], out _) &&
                (int.TryParse(parts[1], out _) || parts[1] == "S"))
            {
                result["TOTP Period"] = parts[0];
                result["TOTP Digits"] = parts[1] == "S" ? "5" : parts[1];
                if (parts[1] == "S") result["TOTP Encoder"] = "Steam";
            }
            else if (settings.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
                ReadOtpUri(settings, result, "TOTP (Settings)");
            else result["TOTP Settings"] = settings;
        }
        foreach (var (key, value) in fields)
        {
            if (key.Equals("TimeOtp-Secret-Base32", StringComparison.OrdinalIgnoreCase))
                result["TOTP Secret"] = value;
            else if (key.StartsWith("TimeOtp-Secret-", StringComparison.OrdinalIgnoreCase))
                result["TOTP Secret (" + key[15..] + ")"] = value;
            else if (key.StartsWith("TimeOtp-", StringComparison.OrdinalIgnoreCase) &&
                     !key.StartsWith("TimeOtp-Secret", StringComparison.OrdinalIgnoreCase))
                result["TOTP " + key[8..]] = value;
        }
        return result;
    }

    private static void ReadOtpUri(string input, Dictionary<string, string> result, string prefix)
    {
        // Both otpauth://...?... and KeeOtp key=...&... are accepted.
        if (!input.StartsWith("key=", StringComparison.OrdinalIgnoreCase) &&
            !Uri.TryCreate(input, UriKind.Absolute, out _)) { result[prefix + " Source"] = input; return; }
        var query = input.StartsWith("key=", StringComparison.OrdinalIgnoreCase)
            ? input : new Uri(input).Query.TrimStart('?');
        if (query.Length > 16_384) { result[prefix + " Source"] = input; return; }
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fragment in query.Split('&'))
        {
            var equals = fragment.IndexOf('=');
            if (equals <= 0) continue;
            var key = Uri.UnescapeDataString(fragment[..equals].Replace('+', ' '));
            var value = Uri.UnescapeDataString(fragment[(equals + 1)..].Replace('+', ' '));
            if (!parameters.ContainsKey(key)) parameters[key] = value;
        }
        if (!parameters.TryGetValue("secret", out var secret) && !parameters.TryGetValue("key", out secret))
        { result[prefix + " Source"] = input; return; }
        result[prefix + " Secret"] = secret;
        foreach (var pair in new [] { ("issuer", "Issuer"), ("algorithm", "Algorithm"), ("digits", "Digits"),
                                      ("period", "Period"), ("counter", "Counter"), ("encoder", "Encoder") })
            if (parameters.TryGetValue(pair.Item1, out var value)) result[prefix + " " + pair.Item2] = value;
    }
}
