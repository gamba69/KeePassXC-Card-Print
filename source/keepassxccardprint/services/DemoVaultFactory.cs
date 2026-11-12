using System.Security.Cryptography;
using KeePassXCCardPrint.Models;

namespace KeePassXCCardPrint.Services;

/// <summary>Fresh, synthetic vault data for demonstrations; never accesses a KDBX file.</summary>
internal static class DemoVaultFactory
{
    // All site names are fictitious and use the RFC 2606 reserved .example domain.
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
        ("DEMO / Other", "Travel Account", "travel", 9)
    ];

    internal static VaultSnapshot Create()
    {
        var records = new List<VaultRecord>(Examples.Length);
        for (int i = 0; i < Examples.Length; i++)
        {
            var (group, title, host, iconId) = Examples[i];
            var extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (i % 3 == 0)
                extras["Recovery Codes"] = string.Join('\n',
                    Enumerable.Range(0, 8).Select(_ => RandomText(10, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789")));
            if (i % 4 == 0)
                extras["OTP"] = "otpauth://totp/Demo:" + Uri.EscapeDataString(title) +
                    "?secret=" + RandomText(32, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567") +
                    "&issuer=Demo&digits=6&period=30";
            if (i == 18)
            {
                extras["Recovery Key ID"] = Guid.NewGuid().ToString("D");
                extras["Recovery Key"] = string.Join("-",
                    Enumerable.Range(0, 8).Select(_ => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6")));
            }
            if (i == 12) extras["Device"] = "Example Wi-Fi Router";
            if (i == 13) extras["Serial Number"] = "DEMO-" + RandomText(10, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789");
            if (i == 15) extras["Startup PIN"] = RandomText(8, "0123456789");
            if (i == 17) extras["Extra Password"] = RandomText(20, "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789");

            var alternativeUrls = i % 5 == 0
                ? new[] { $"https://signin-{host}.example/account" }
                : Array.Empty<string>();
            var image = RecordIconImages.Get(iconId, null, out var iconOwned);
            var groupImage = RecordIconImages.Get(27, null, out var groupIconOwned);
            records.Add(new VaultRecord
            {
                Uuid = Guid.NewGuid(),
                Group = group,
                Title = title,
                UserName = $"demo{i + 1:00}@example.com",
                Password = RandomText(22, "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$"),
                Url = $"https://{host}.example/login",
                AdditionalUrls = alternativeUrls,
                AdditionalUrlCandidateCount = alternativeUrls.Length,
                Notes = i % 4 == 1
                    ? "Synthetic sample record.\nThis content is safe for screenshots and does not belong to a real account."
                    : "DEMO ONLY — randomly generated example credentials.",
                IconId = iconId,
                IconImage = image,
                IconImageOwned = iconOwned,
                GroupIconId = 27,
                GroupIconImage = groupImage,
                GroupIconImageOwned = groupIconOwned,
                AdditionalFields = OtpFieldParser.Normalize(extras),
                Selected = i < 9
            });
        }
        return new VaultSnapshot
        {
            RootUuid = Guid.NewGuid(),
            DisplayName = "DEMO · Sample vault",
            CipherName = "Not a KDBX file",
            KdfName = "None",
            GroupCount = Examples.Select(item => item.Group).Distinct(StringComparer.Ordinal).Count(),
            Records = records
        };
    }

    private static string RandomText(int length, string alphabet)
    {
        var chars = new char[length];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }
}
