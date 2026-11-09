using System.IO;
using System.Security.Cryptography;

namespace KeePassXCCardPrint.Services;

/// <summary>Tests only synthetic exception messages; never accesses a user vault.</summary>
internal static class VaultErrorSelfTests
{
    internal static string Report { get; private set; } = "NOT RUN";

    internal static int Run()
    {
        int checks = 0;
        var log = new System.Text.StringBuilder();
        try
        {
            void Check(bool condition, string name)
            {
                log.AppendLine($"[{(condition ? "PASS" : "FAIL")}] vault/{++checks:D3}: {name}");
                if (!condition) throw new InvalidOperationException("VAULT_ERROR_TEST_FAILED: " + name);
            }

            foreach (var message in new[]
            {
                "Header HMAC verification failed.", // KDBX 4: wrong composite key
                "StreamStartBytes verification failed." // KDBX 3: wrong composite key
            })
            {
                var failure = KdbxOpenErrorClassifier.FromInvalidData(new InvalidDataException(message));
                Check(failure.Code == "INVALID_CREDENTIALS", "incorrect-key classification: " + message.Split(' ')[0]);
                Check(failure.Message.Contains("мастер-пароль", StringComparison.Ordinal), "actionable key feedback: " + message.Split(' ')[0]);

            }
            foreach (var message in new[]
            {
                "Header SHA256 verification failed.",
                "Block hash verification failed.",
                "Block 0 HMAC verification failed.",
                "Block 17 HMAC verification failed."
            })
            {
                var failure = KdbxOpenErrorClassifier.FromInvalidData(new InvalidDataException(message));
                Check(failure.Code == "KDBX_INTEGRITY", "integrity failure classification");

            }
            var unknown = KdbxOpenErrorClassifier.FromInvalidData(
                new InvalidDataException("Arbitrary malformed KDBX inner header"));
            Check(unknown.Code == "KDBX_FORMAT", "unknown invalid-data classification");
            Check(!unknown.Message.Contains("Arbitrary", StringComparison.Ordinal), "do not leak exception text");

            var crypto = KdbxOpenErrorClassifier.CredentialsRejected();
            Check(crypto.Code == "INVALID_CREDENTIALS", "cryptographic exception classification");
            log.AppendLine($"[PASS] vault/summary: {checks} assertions");
            Report = log.ToString();
            return 0;
        }
        catch (Exception e)
        {
            if (!e.Message.StartsWith("VAULT_ERROR_TEST_FAILED:", StringComparison.Ordinal))
                log.AppendLine("[FAIL] vault/runtime: " + e.GetType().Name);
            Report = log.ToString();
            return 9;
        }
    }
}
