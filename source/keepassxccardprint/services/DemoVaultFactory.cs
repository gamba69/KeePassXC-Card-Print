using System.Security.Cryptography;
using KeePassXCCardPrint.Models;

namespace KeePassXCCardPrint.Services;

/// <summary>
/// Synthetic, offline demonstration corpus. Each reload produces fresh UUIDs,
/// credentials and OTP seeds. No KDBX, cloud service or real account is touched.
/// The cases cover layout/mapping combinations, not just different icon names.
/// </summary>
internal static class DemoVaultFactory
{
    // All host names use the reserved RFC 2606 .example domain.
    private static readonly (string Group, string Title, string Host, int Icon)[] Examples =
    [
        ("DEMO / Email", "Aurora Mail", "mail", 2),
        ("DEMO / Email", "Team Inbox", "inbox", 18),
        ("DEMO / Email", "Newsletter Account", "news", 2),
        ("DEMO / Email", "Family Mailbox", "family", 18),
        ("DEMO / Cloud", "Cloud Drive", "drive", 28),
        ("DEMO / Cloud", "Photo Storage", "photos", 42),
        ("DEMO / Cloud", "Document Archive", "archive", 42),
        ("DEMO / Cloud", "Backup Console", "backup", 28),
        ("DEMO / Work", "Office Portal", "office", 35),
        ("DEMO / Work", "Developer Console", "dev", 36),
        ("DEMO / Work", "Project Board", "projects", 35),
        ("DEMO / Work", "Source Control", "git", 36),
        ("DEMO / Devices", "Home Router", "router", 27),
        ("DEMO / Devices", "Home Server", "server", 30),
        ("DEMO / Devices", "Wireless Access Point", "wifi", 27),
        ("DEMO / Devices", "Workstation", "workstation", 30),
        ("DEMO / Security", "Authenticator Backup", "auth", 10),
        ("DEMO / Security", "Recovery Vault", "recovery", 10),
        ("DEMO / Security", "Disk Encryption", "disk", 10),
        ("DEMO / Security", "Security Dashboard", "security", 10),
        ("DEMO / Other", "Music Subscription", "music", 25),
        ("DEMO / Other", "Community Forum", "forum", 0),
        ("DEMO / Other", "Personal Notes", "notes", 4),
        ("DEMO / Other", "Travel Account", "travel", 9),
        ("DEMO / 2FA", "TOTP · QR Showcase", "totp", 10),
        ("DEMO / 2FA", "TOTP SHA-256", "totp256", 10),
        ("DEMO / 2FA", "HOTP Counter", "hotp", 10),
        ("DEMO / 2FA", "Steam Authenticator", "steam", 10),
        ("DEMO / Networking", "VPN · Secondary URL", "vpn", 27),
        ("DEMO / Networking", "Wi-Fi Guest Network", "guest", 27),
        ("DEMO / Networking", "SSH Access / Fingerprint", "ssh", 30),
        ("DEMO / Recovery", "16 Backup Codes", "codes", 10),
        ("DEMO / Recovery", "BitLocker Recovery Key", "bitlocker", 10),
        ("DEMO / Recovery", "Emergency Contacts", "contact", 18),
        ("DEMO / Content", "Long Multiline Notes", "longnotes", 4),
        ("DEMO / Content", "Long 64-Character Secret", "longsecret", 4),
        ("DEMO / Content", "Лицензия · русский", "license", 36),
        ("DEMO / Content", "Unicode · Cyrillic & Greek", "unicode", 35),
        ("DEMO / Content", "Missing Fields", "missing", 0),
        ("DEMO / Content", "Phone Login", "phone", 18)
    ];

    // First impression: ten balanced, intentional cards rather than every
    // edge case at once. The full stress-test corpus remains available below.
    private static readonly HashSet<string> InitiallySelected = new(StringComparer.Ordinal)
    {
        "Aurora Mail", "Cloud Drive", "Office Portal", "Home Router",
        "BitLocker Recovery Key", "TOTP · QR Showcase",
        "VPN · Secondary URL", "16 Backup Codes",
        "Unicode · Cyrillic & Greek", "Phone Login"
    };

    internal static VaultSnapshot Create()
    {
        var records = new List<VaultRecord>(Examples.Length);
        for (int i = 0; i < Examples.Length; i++)
        {
            var (group, title, host, iconId) = Examples[i];
            var extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // No arbitrary recovery codes or OTP fields on regular accounts:
            // these turn an attractive login card into an accidental multipage
            // stress test. Dedicated security records showcase those features.
            if (title is "Authenticator Backup" or "Security Dashboard")
            {
                extras["Recovery Codes"] = Codes(8);
                extras["OTP"] = TotpUri(title, "Demo", 6, 30);
            }

            // Known categories exercise all field roles and mapping sections.
            switch (title)
            {
                case "Disk Encryption":
                case "BitLocker Recovery Key":
                    extras["Device"] = "DEMO-Notebook · Volume C:";
                    extras["Recovery Key ID"] = Guid.NewGuid().ToString("D");
                    extras["Recovery Key"] = string.Join("-", Enumerable.Range(0, 8)
                        .Select(_ => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6")));
                    extras["Emergency Contact"] = "Demo help desk · +380 00 000 00 00";
                    break;
                case "Home Router":
                case "Wi-Fi Guest Network":
                    extras["Device"] = "Fictional Router AX";
                    extras["Wi-Fi SSID"] = "DEMO-GUEST";
                    extras["Wi-Fi Password"] = RandomText(18, SafeAlphabet);
                    extras["Serial Number"] = "DEMO-" + RandomText(12, SafeAlphabet);
                    break;
                case "Home Server":
                case "Workstation":
                    extras["Device"] = "Sample Workstation";
                    extras["Startup PIN"] = RandomText(8, "0123456789");
                    extras["Serial Number"] = "DEMO-" + RandomText(10, SafeAlphabet);
                    break;
                case "Developer Console":
                case "Source Control":
                    extras["API Token"] = RandomText(28, SafeAlphabet);
                    break;
                case "SSH Access / Fingerprint":
                    extras["SSH Fingerprint"] = "SHA256:" + RandomText(43, "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/");
                    break;
                case "Recovery Vault":
                case "16 Backup Codes":
                    extras["Backup Codes"] = Codes(16);
                    extras["Passphrase"] = "demo silver river telescope autumn orbit compass";
                    extras["Extra Password"] = RandomText(20, SafeAlphabet);
                    break;
                case "TOTP · QR Showcase":
                    extras["OTP"] = TotpUri(title, "Demo Showcase", 6, 30);
                    break;
                case "TOTP SHA-256":
                    extras["OTP"] = "otpauth://totp/Demo:SHA256?secret=" +
                        RandomText(32, Base32Alphabet) + "&issuer=Demo&algorithm=SHA256&digits=8&period=45";
                    break;
                case "HOTP Counter":
                    extras["HOTP Secret"] = RandomText(32, Base32Alphabet);
                    extras["HOTP Counter"] = "42";
                    extras["HOTP Issuer"] = "Demo HOTP";
                    break;
                case "Steam Authenticator":
                    extras["TOTP Secret"] = RandomText(32, Base32Alphabet);
                    extras["TOTP Encoder"] = "Steam";
                    extras["TOTP Period"] = "30";
                    extras["TOTP Digits"] = "5";
                    break;
                case "Лицензия · русский":
                    extras["License Key"] = RandomText(25, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789");
                    extras["Account Number"] = "DEMO-0001-2026";
                    break;
                case "Unicode · Cyrillic & Greek":
                    // Use scripts confirmed by the bundled printing typography.
                    // Unsupported Japanese/Arabic glyphs rendered as empty boxes;
                    // a beautiful showcase must never rely on missing fonts.
                    extras["Long Description"] = "Русский: короткий пример.\n" +
                        "English: clean and readable.\n" +
                        "Ελληνικά: Καλημέρα.";
                    break;
                case "Emergency Contacts":
                    extras["Emergency Contact"] = "Demo Support · +380 00 000 00 00";
                    extras["OTP Scratch"] = RandomText(12, SafeAlphabet);
                    break;
                case "Long 64-Character Secret":
                    extras["API Token"] = RandomText(64, SafeAlphabet);
                    extras["Passphrase"] = "demo apple sunset notebook castle river window compass";
                    break;
            }

            if (title == "Long Multiline Notes")
                extras["Long Description"] = string.Join('\n', Enumerable.Range(1, 8)
                    .Select(n => $"{n:00}. DEMO ONLY: printable multi-line information, " +
                        "very long text, wrapping and continuation onto additional cards."));
            if (title == "Missing Fields") extras["Long Description"] = "Only optional fields remain.";

            string[] alternativeUrls = title switch
            {
                "VPN · Secondary URL" => ["https://vpn.example/remote/login", "https://vpn-backup.example/"],
                "Wi-Fi Guest Network" => ["https://guest.example/portal"],
                "Backup Console" => ["https://backup.example/dashboard"],
                _ => []
            };
            var userName = title switch
            {
                "Phone Login" => "+380000000000",
                "Missing Fields" => "",
                "Unicode · Cyrillic & Greek" => "пользователь@example.com",
                _ => $"demo{i + 1:00}@example.com"
            };
            var password = title switch
            {
                "Missing Fields" => "",
                "Long 64-Character Secret" => RandomText(64, SafeAlphabet),
                "Home Router" => RandomText(8, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"),
                _ => RandomText(22, SafeAlphabet)
            };
            var url = title switch
            {
                "VPN · Secondary URL" => "", // tests fallback to the secondary URL
                "Missing Fields" => "",
                _ => $"https://{host}.example/login"
            };
            var notes = title switch
            {
                "Long Multiline Notes" => string.Join('\n', Enumerable.Range(1, 6)
                    .Select(n => $"Paragraph {n}: demo note for line wrapping, page continuation, " +
                        "text fitting and printing on both sides. This is not an account.")),
                "Missing Fields" => "",
                "Unicode · Cyrillic & Greek" => "Смешанные алфавиты: Русский, English, Ελληνικά.",
                "Personal Notes" => "Личная памятка — демонстрационная запись, без настоящих секретов.",
                "Document Archive" => "Документы хранятся в вымышленном облачном архиве.",
                _ => ""
            };
            var image = RecordIconImages.Get(iconId, null, out var iconOwned);
            int groupIconId = group.Contains("2FA", StringComparison.Ordinal) ||
                group.Contains("Security", StringComparison.Ordinal) ? 10 :
                group.Contains("Devices", StringComparison.Ordinal) ||
                group.Contains("Networking", StringComparison.Ordinal) ? 27 : 35;
            var groupImage = RecordIconImages.Get(groupIconId, null, out var groupIconOwned);
            records.Add(new VaultRecord
            {
                Uuid = Guid.NewGuid(), Group = group, Title = title,
                UserName = userName, Password = password, Url = url,
                AdditionalUrls = alternativeUrls,
                AdditionalUrlCandidateCount = alternativeUrls.Length,
                Notes = notes, IconId = iconId, IconImage = image,
                IconImageOwned = iconOwned, GroupIconId = groupIconId,
                GroupIconImage = groupImage, GroupIconImageOwned = groupIconOwned,
                AdditionalFields = OtpFieldParser.Normalize(extras),
                HasAttachments = title is "Document Archive" or "Long Multiline Notes",
                Selected = InitiallySelected.Contains(title)
            });
        }
        return new VaultSnapshot
        {
            RootUuid = Guid.NewGuid(), DisplayName = "DEMO · Printing showcase",
            CipherName = "Not a KDBX file", KdfName = "None",
            GroupCount = Examples.Select(item => item.Group).Distinct(StringComparer.Ordinal).Count(),
            Records = records
        };
    }

    private const string SafeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$";
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static string Codes(int count) => string.Join('\n',
        Enumerable.Range(0, count).Select(_ => RandomText(10, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789")));

    private static string TotpUri(string title, string issuer, int digits, int period) =>
        "otpauth://totp/" + Uri.EscapeDataString(issuer + ":" + title) +
        "?secret=" + RandomText(32, Base32Alphabet) +
        "&issuer=" + Uri.EscapeDataString(issuer) +
        "&digits=" + digits + "&period=" + period;

    private static string RandomText(int length, string alphabet)
    {
        var chars = new char[length];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }
}
