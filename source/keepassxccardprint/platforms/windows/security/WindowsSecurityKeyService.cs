using System.Security.Cryptography;
using Yubico.YubiKey;
using Yubico.YubiKey.Otp;

namespace KeePassXCCardPrint.Platforms.Windows.Security;

// In-memory HMAC challenge-response only; never writes secret material to files.
internal sealed class WindowsSecurityKeyService : ISecurityKeyService
{
    public IReadOnlyList<string> AvailableSerials()
    {
        try { return YubiKeyDevice.FindAll().Select(k => k.SerialNumber?.ToString() ?? "без номера").ToList(); }
        catch { return []; }
    }

    public byte[] ComputeChallengeResponse(byte[] challenge, int slot, bool shortChallenge, string? serial)
    {
        if (slot is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(slot));
        var devices = YubiKeyDevice.FindAll().ToArray();
        var device = devices.FirstOrDefault(d => string.IsNullOrWhiteSpace(serial) ||
                                               d.SerialNumber?.ToString() == serial);
        if (device is null) throw new InvalidOperationException("YubiKey не обнаружен. Проверьте подключение USB.");
        // Default YubiKey OTP HMAC slots accept a 64-byte challenge. KeePassXC's
        // HID implementation zero-pads a 32-byte KDF salt into the report. The
        // LT64 option sends the actual length for slots configured with the
        // Supports the HMAC-SHA1 short-challenge option.
        var payload = shortChallenge ? (byte[])challenge.Clone() : new byte[64];
        if (!shortChallenge) Array.Copy(challenge, payload, Math.Min(64, challenge.Length));
        try
        {
            using var otp = new OtpSession(device);
            var result = otp.CalculateChallengeResponse(slot == 1 ? Slot.ShortPress : Slot.LongPress)
                .UseChallenge(payload)
                .UseYubiOtp(false)
                .GetDataBytes();
            if (result.Length != 20) throw new InvalidOperationException("YubiKey вернул ответ неожиданной длины.");
            return result.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }
}


