namespace KeePassXCCardPrint.Services;

/// <summary>
/// Three deliberately contrasting, fully read-only print templates. Every
/// preset provides front/reverse styling AND field rules. Personal copies are
/// editable, but changing a preset never modifies its default configuration.
/// The retired styles remain loadable for existing personal template files.
/// </summary>
public static class CardAppearancePresets
{
    public static CardAppearance Create(string style)
    {
        if (style is not ("archive" or "technical" or "table" or "classic" or "minimal"))
            throw new ArgumentException("Unknown built-in card style.", nameof(style));

        var result = new CardAppearance { Style = style };
        switch (style)
        {
            // Comfortable, high-contrast editorial layout. Its reverse is a
            // restrained security emblem, separate from the front decoration.
            case "archive":
                result.WidthMm = 62; result.HeightMm = 86;
                result.TitleSizePt = 8.4; result.LabelSizePt = 5.5;
                result.ValueSizePt = 6.7; result.FooterSizePt = 5.2;
                result.LabelsPosition = "above"; result.LabelsUpper = false;
                result.PaddingMm = 3.0; result.FieldGapPt = 1.2;
                result.HeaderFill = true; result.HeaderAccent = true;
                result.HeaderGroupShow = true; result.HeaderGroupPosition = "top";
                result.HeaderGroupSizePt = 5.3;
                result.HeaderColor = "#EFF3F7"; result.BackgroundColor = "#FFFFFF";
                result.InkColor = "#203447"; result.MutedColor = "#5B6A77";
                result.AccentColor = "#237C83"; result.BorderColor = "#A4B8BF";
                result.RuleColor = "#CFD9DF"; result.Frame = true;
                result.CornerRadiusMm = 1.8; result.FrameWidthPt = .35;
                result.EntryIconMode = "all"; result.EntryIconSizeMm = 5;
                result.EntryIconPrintMode = "original";
                result.GroupLongValues = false; result.RecoveryColumns = 2;
                result.RecoveryCheckboxes = true; result.RecoveryAutosize = true;
                result.RecoveryMinFontPt = 5.2; result.RecoveryMaxFontPt = 8.0;
                result.CutGuideMode = "cross"; result.FooterUuidLength = 12;
                result.DuplexEnabled = true; result.DuplexBackFill = "design";
                result.DuplexBackStyle = "minimal"; result.EmptyBackShowArt = true;
                result.EmptyBackShowIcon = true; result.BackArtIcon = "key";
                result.BackArtIconColor = "#237C83"; result.BackArtIconSizeMm = 15;
                result.BackArtIconOpacity = 64;
                result.EmptyBackBgSource = "custom";
                result.EmptyBackBgPattern = "gradient";
                result.EmptyBackBgGray = 0; result.EmptyBackBgContrast = 12;
                break;

            // Dense engineering / recovery print: left-hand labels, grouped
            // keys, compact sections and technical paper. Pages may continue.
            case "technical":
                result.WidthMm = 65; result.HeightMm = 91;
                result.Density = "compact";
                result.TitleFont = "Atkinson Hyperlegible Mono";
                result.TitleWeight = "600"; result.LabelWeight = "500";
                result.ValueWeight = "500"; result.ValueTrackingPt = .08;
                result.TitleSizePt = 8.2; result.LabelSizePt = 5.6;
                result.ValueSizePt = 6.8; result.FooterSizePt = 5.0;
                result.LabelsPosition = "left"; result.LabelColumnPct = 35;
                result.PaddingMm = 2.8; result.FieldGapPt = .9;
                result.HeaderFill = false; result.HeaderAccent = true;
                result.HeaderGroupShow = true; result.HeaderGroupPosition = "left";
                result.HeaderGroupColumnPct = 29; result.HeaderGroupSizePt = 5.2;
                result.HeaderColor = "#FFFFFF";
                result.InkColor = "#152C39"; result.MutedColor = "#435B68";
                result.AccentColor = "#007D91"; result.RuleColor = "#7EA1AA";
                result.BorderColor = "#819BA2"; result.Frame = false;
                result.SectionHeadings = true; result.SectionRules = true;
                result.CardBgPattern = "technical"; result.CardBgGray = 0;
                result.CardBgContrast = 12;
                result.GroupLongValues = true; result.GroupSize = 4;
                result.GroupMinLength = 12; result.GroupSpaces = .65;
                result.PasswordGroupMode = "auto";
                result.RecoveryColumns = 3; result.RecoveryCheckboxes = false;
                result.RecoveryAutosize = true; result.RecoveryMinFontPt = 5.3;
                result.RecoveryMaxFontPt = 8.0;
                result.NotesMode = "small"; result.NotesSizePt = 5.5;
                result.EntryIconMode = "all"; result.EntryIconSizeMm = 4.4;
                result.EntryIconPrintMode = "outline";
                result.CutGuideMode = "edge"; result.FooterUuidLength = 16;
                result.FooterDateFormat = "yyyy/MM/dd";
                result.FooterUuidSeparators = "every4";
                result.DuplexEnabled = true; result.DuplexContinuation = true;
                result.DuplexBackFill = "design";
                result.DuplexBackStyle = "rules";
                result.BackRulesStyle = "simple"; result.BackRulesWidthMm = 36;
                result.BackRulesGapMm = 2.0; result.BackArtIcon = "shield";
                result.BackArtIconSizeMm = 12; result.BackArtIconColor = "#007D91";
                result.EmptyBackShowHeader = false;
                result.EmptyBackBgSource = "custom";
                result.EmptyBackBgPattern = "technical";
                result.EmptyBackBgContrast = 14;
                break;

            // Colorful QR / security showcase: the QR sits beside its secret,
            // compact fields leave room for long multilingual demo records.
            case "table":
                result.WidthMm = 70; result.HeightMm = 94;
                result.TitleWeight = "700"; result.LabelWeight = "600";
                result.TitleSizePt = 7.8; result.LabelSizePt = 5.5;
                result.ValueSizePt = 6.4; result.FooterSizePt = 5.2;
                result.PaddingMm = 3.1; result.FieldGapPt = 1.05;
                result.LabelsPosition = "above"; result.LabelsUpper = true;
                result.HeaderFill = true; result.HeaderAccent = true;
                // Keep the title full width on the QR design. Group badges
                // squeezed real-world two-word titles into an awkward second line.
                // The two other presets still showcase group-in-header layouts.
                result.HeaderGroupShow = false;
                result.TitleAlign = "center"; result.HeaderGroupAlign = "center";
                result.HeaderColor = "#E8F5F4";
                result.BackgroundColor = "#FCFEFD";
                result.InkColor = "#173C48"; result.MutedColor = "#42636B";
                result.AccentColor = "#087F86"; result.RuleColor = "#A6D4CE";
                result.BorderColor = "#73AAB0"; result.Frame = true;
                result.CornerRadiusMm = 2.6; result.FrameWidthPt = .45;
                result.SectionHeadings = true; result.SectionRules = true;
                result.FrontBgOverride = true;
                result.FrontBgPattern = "gradient"; result.FrontBgContrast = 13;
                result.BackBgOverride = true;
                result.DuplexBackBackground = "diagonal";
                result.BackBgGray = 0; result.BackBgContrast = 12;
                result.EntryIconMode = "all"; result.EntryIconSizeMm = 5.3;
                result.EntryIconPrintMode = "auto";
                result.TotpQrEnabled = true;
                result.TotpQrPosition = "right"; result.TotpQrHeightMm = 21;
                result.TotpQrMinHeightMm = 14;
                result.OtpCompact = true;
                result.RecoveryColumns = 2; result.RecoveryCheckboxes = true;
                result.RecoveryAutosize = true; result.RecoveryMinFontPt = 5.2;
                result.RecoveryMaxFontPt = 8.0;
                result.NotesMode = "optional"; result.NotesSizePt = 5.5;
                result.GroupLongValues = true; result.GroupSize = 4;
                result.GroupMinLength = 16; result.GroupSpaces = .7;
                result.DuplexEnabled = true; result.DuplexBackFill = "design";
                result.DuplexBackStyle = "rules";
                result.BackRulesStyle = "original";
                result.EmptyBackShowArt = true; result.EmptyBackShowIcon = true;
                result.BackArtIcon = "vault"; result.BackArtIconSizeMm = 17;
                result.BackArtIconColor = "#087F86";
                result.BackArtIconOpacity = 65;
                result.EmptyBackBgSource = "back";
                result.CutGuideMode = "both"; result.FooterUuidLength = 10;
                result.FooterUuidUppercase = false; result.FooterUuidPrefix = false;
                result.FooterDateFormat = "dd.MM.yyyy";
                break;

            // Retained only to read pre-existing personal templates with these styles.
            case "minimal":
                result.HeaderFill = false; result.HeaderAccent = false;
                result.SectionRules = false; result.Frame = false;
                result.InkColor = "#202020";
                break;
            case "classic":
                result.HeaderColor = "#EFF1F4";
                break;
        }
        CardPalette.LinkMatchingPresetColors(result);
        result.NormalizeHeaderFooter();
        return result;
    }

    /// <summary>Immutable per-preset rules. Every call creates an independent map.</summary>
    public static Dictionary<string, FieldRule> CreateMapping(string style)
    {
        if (style is not ("archive" or "technical" or "table" or "classic" or "minimal"))
            throw new ArgumentException("Unknown built-in card style.", nameof(style));
        var map = new Dictionary<string, FieldRule>(StringComparer.OrdinalIgnoreCase);
        void Add(string field, string? label = null, string? role = null,
            string? section = null, int? order = null, string? fit = null,
            int sizePct = 100, string? group = null)
        {
            var rule = FieldMappingDefaults.Infer(field);
            if (label is not null) rule.Label = label;
            if (role is not null) rule.Role = role;
            if (section is not null) rule.Section = section;
            if (order.HasValue) rule.Order = order.Value;
            if (fit is not null) rule.Fit = fit;
            rule.ValueSizePct = sizePct;
            if (group is not null) rule.GroupMode = group;
            map.Add(field, rule);
        }
        Add("GROUP", "Раздел", "text", "account", 1);
        Add("USERNAME", "Логин", "secret", "account", 10);
        map["USERNAME"].KeepWithNext="field:PASSWORD";
        Add("PASSWORD", "Пароль", "secret", "auth", 20, "smart");
        Add("URL", "Адрес", "text", "account", 30, "shrink");
        Add("NOTES", "Примечания", "multiline", "extra", 900, "wrap", 90);
        Add("DEVICE", "Устройство", "text", "account", 12);
        Add("SERIAL NUMBER", "Серийный №", "secret", "account", 18);
        Add("STARTUP PIN", "PIN запуска", "secret", "auth", 21);
        Add("RECOVERY KEY ID", "ID ключа", "secret", "recovery", 35);
        Add("RECOVERY KEY", "BitLocker", "bitlocker", "recovery", 40, "split");
        Add("RECOVERY CODES", "Резервные коды", "codes", "recovery", 60);
        Add("BACKUP CODES", "Резервные коды", "codes", "recovery", 61);
        Add("TOTP Secret", "TOTP", "totp", "auth", 45, "wrap", 95);
        Add("HOTP Secret", "HOTP", "totp", "auth", 47, "wrap", 95);
        Add("OTP", "Исходный OTP URI", "text", "auth", 85);
        map["OTP"].Visibility = "hidden"; // Hide raw URI; normalized fields are printed.
        Add("API Token", "API токен", "secret", "auth", 26, "smart");
        Add("Wi-Fi SSID", "SSID", "text", "account", 16);
        Add("Wi-Fi Password", "Wi-Fi пароль", "secret", "auth", 27);
        Add("LICENSE KEY", "Лицензия", "secret", "auth", 28, "split");
        Add("ACCOUNT NUMBER", "Номер счёта", "secret", "account", 32);
        Add("PASSPHRASE", "Кодовая фраза", "multiline", "auth", 42, "wrap", 94);
        Add("LONG DESCRIPTION", "Описание", "multiline", "extra", 850, "wrap", 85);
        Add("SSH FINGERPRINT", "SSH ключ", "secret", "auth", 34, "smart");
        Add("EMERGENCY CONTACT", "Контакт", "text", "extra", 120);
        Add("OTP SCRATCH", "Секретный код", "secret", "recovery", 65);

        switch (style)
        {
            case "archive":
                map["URL"].UrlMode = "smart";
                map["URL"].UrlSource = "fallback";
                map["GROUP"].ShowLabel = false;
                map["PASSWORD"].ValueSizePct = 100;
                map["NOTES"].Visibility = "nonempty";
                break;
            case "technical":
                map["URL"].UrlMode = "domain";
                map["URL"].UrlSource = "fallback";
                map["USERNAME"].PhoneFormat = true;
                map["PASSWORD"].GroupMode = "on";
                map["PASSWORD"].GroupSize = 4;
                map["PASSWORD"].GroupSpaces = .65;
                map["PASSWORD"].ValueWeight = "600";
                map["RECOVERY KEY"].ValueSizePct = 92;
                map["RECOVERY KEY"].GroupMode = "on";
                map["NOTES"].ValueSizePct = 80;
                map["TOTP Secret"].ValueSizePct = 88;
                break;
            case "table":
                map["URL"].UrlMode = "smart";
                map["URL"].UrlSource = "fallback";
                map["TOTP Secret"].ValueSizePct = 95;
                map["HOTP Secret"].Visibility = "hidden"; // HOTP is not a TOTP QR.
                map["PASSWORD"].GroupMode = "auto";
                map["API Token"].ValueSizePct = 92;
                map["NOTES"].ValueSizePct = 80;
                map["LONG DESCRIPTION"].ValueSizePct = 76;
                break;
        }
        // Preserve the default hidden status of raw WebAuthn attributes. If a
        // user enables them in a copy of this preset, keep them with the export.
        foreach(var field in new[]{"KPEX_PASSKEY_RELYING_PARTY","KPEX_PASSKEY_USERNAME",
            "KPXC_PASSKEY_USERNAME","KPEX_PASSKEY_USER_HANDLE","KPEX_PASSKEY_CREDENTIAL_ID"})
        {
            var rule=FieldMappingDefaults.Infer(field);
            rule.Order=54;
            rule.KeepWithNext="field:"+PasskeyExport.FieldName;
            map[field]=rule;
        }
        return map;
    }
}
