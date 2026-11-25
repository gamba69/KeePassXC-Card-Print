using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Services;
using QRCoder;

namespace KeePassXCCardPrint.Rendering;

/// <summary>Offline synthetic OTP regression tests. No credentials or QR payloads are logged.</summary>
internal static class OtpQrMappingSelfTests
{
    internal static void Run(Action<bool,string> check)
    {
        const string field = "TOTP Secret";
        var record = new VaultRecord
        {
            Uuid = Guid.Parse("44444444-3333-2222-1111-000000000000"),
            Title = "Synthetic OTP", Group = "Tests", UserName = "TEST_ACCOUNT",
            AdditionalFields = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                [field] = "JBSWY3DPEHPK3PXP",
                ["TOTP Account"] = "TEST_ACCOUNT"
            }
        };
        var appearance = new CardAppearance
        {
            WidthMm = 86, HeightMm = 90, PaddingMm = 3,
            TotpQrEnabled = true, TotpQrPosition = "right",
            TotpQrHeightMm = 26, TotpQrMinHeightMm = 14,
            ShowDate = false, EntryIconMode = "none"
        };
        var rule = new FieldRule
        {
            Label = "TOTP", Role = "totp", Section = "auth", Order = 30,
            QrMode = "text", QrEcc = "Q", QrPosition = "right",
            QrMinModuleMm = .35, QrSizeMm = 26
        };
        var mapping = new Dictionary<string,FieldRule>(StringComparer.OrdinalIgnoreCase)
        {
            [field] = rule
        };
        static CardLine[] Lines(VaultRecord record, CardAppearance a,
            Dictionary<string,FieldRule> mapping) =>
            CardLayoutEngine.Build([record],a,mapping)
                .SelectMany(c => new[]{c.Front,c.Continuation})
                .Where(p=>p is not null).SelectMany(p=>p!.Lines).ToArray();

        var old = Lines(record,appearance,mapping).Single(l=>l.Kind==CardLineKind.Qr);
        check(!old.QrStrict && old.QrUri is not null &&
            old.QrUri.StartsWith("otpauth://",StringComparison.Ordinal),
            "legacy OTP QR and otpauth payload remain unchanged");

        rule.QrOtpOverride = true;
        rule.QrMode = "both";
        rule.QrEcc = "Q";
        rule.QrPosition = "right";
        var mapped = Lines(record,appearance,mapping).Single(l=>l.Kind==CardLineKind.Qr);
        check(mapped.QrStrict && mapped.QrEcc=="Q" && mapped.QrInline &&
            mapped.QrPosition=="right" && mapped.QrUri==old.QrUri && mapped.FontPt>=26,
            "mapped OTP uses error correction and the original right-side text layout");
        using(var encoded=QRCodeGenerator.GenerateQrCode(mapped.QrUri!,QrPrintPolicy.Correction("Q")))
            check(mapped.FontPt+.001 >= encoded.ModuleMatrix.Count*.35f,
                "mapped OTP QR meets the configured minimum module size");

        rule.QrPosition = "left";
        var left = Lines(record,appearance,mapping).Single(l=>l.Kind==CardLineKind.Qr);
        check(left.QrInline && left.QrPosition=="left" && left.QrStrict,
            "mapped OTP preserves left-side text layout");

        rule.QrMode = "qr";
        var onlyQr = Lines(record,appearance,mapping);
        check(onlyQr.Count(l=>l.Kind==CardLineKind.Qr && l.Field==field)==1 &&
            !onlyQr.Any(l=>l.Kind==CardLineKind.Value && l.Field==field),
            "mapped OTP QR-only mode omits the printed secret");

        rule.QrMode = "text";
        var onlyText=Lines(record,appearance,mapping);
        check(!onlyText.Any(l=>l.Kind==CardLineKind.Qr && l.Field==field) &&
            onlyText.Any(l=>l.Kind==CardLineKind.Value && l.Field==field),
            "mapped OTP text-only mode overrides the legacy QR toggle");

        rule.QrMode = "both";
        rule.QrSizeMm = 100;
        bool rejected = false;
        try { _ = Lines(record,appearance,mapping); }
        catch (InvalidOperationException ex) when (
            ex.Message.StartsWith("QR не помещается:", StringComparison.Ordinal))
        { rejected=true; }
        // CardLayoutEngine.Build uses Parallel.For; worker failures are wrapped.
        // Accept only the intended physical-size rejection, not unrelated errors.
        catch (AggregateException ex) when (
            ex.Flatten().InnerExceptions is { Count: > 0 } errors &&
            errors.All(error => error is InvalidOperationException &&
                error.Message.StartsWith("QR не помещается:", StringComparison.Ordinal)))
        { rejected=true; }
        check(rejected,"oversized OTP QR is rejected instead of being shrunk");
    }
}
