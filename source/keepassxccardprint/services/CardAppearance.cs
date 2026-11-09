using System.Text.Json;
using System.Text.Json.Serialization;
using KeePassXCCardPrint.Platforms;
using System.Globalization;

namespace KeePassXCCardPrint.Services;

// Serializable physical dimensions, typography, decorations, and reverse-side settings.
public sealed class CardAppearance
{
    [JsonPropertyName("width_mm")] public double WidthMm { get; set; } = 62.0;
    [JsonPropertyName("height_mm")] public double HeightMm { get; set; } = 86.0;
    [JsonPropertyName("density")] public string Density { get; set; } = "normal";
    [JsonPropertyName("style")] public string Style { get; set; } = "archive";
    private string _secretFont = "Atkinson Hyperlegible Mono";
    [JsonPropertyName("secret_font")]
    public string SecretFont
    {
        get => _secretFont;
        set => _secretFont = string.IsNullOrWhiteSpace(value) ? "Atkinson Hyperlegible Mono" : value;
    }
    [JsonPropertyName("title_font")] public string TitleFont { get; set; } = "Segoe UI";
    [JsonPropertyName("label_font")] public string LabelFont { get; set; } = "Segoe UI";
    [JsonPropertyName("title_weight")] public string TitleWeight { get; set; } = "700";
    [JsonPropertyName("label_weight")] public string LabelWeight { get; set; } = "400";
    [JsonPropertyName("title_size_pt")] public double TitleSizePt { get; set; } = 9.5;
    [JsonPropertyName("label_size_pt")] public double LabelSizePt { get; set; } = 6.1;
    [JsonPropertyName("value_size_pt")] public double ValueSizePt { get; set; } = 8.0;
    [JsonPropertyName("value_weight")] public string ValueWeight { get; set; } = "400";
    [JsonPropertyName("value_tracking_pt")] public double ValueTrackingPt { get; set; } = 0.0;
    [JsonPropertyName("footer_size_pt")] public double FooterSizePt { get; set; } = 5.6;
    [JsonPropertyName("line_spacing")] public double LineSpacing { get; set; } = 1.08;
    [JsonPropertyName("field_gap_pt")] public double FieldGapPt { get; set; } = 1.15;
    [JsonPropertyName("adaptive_field_gaps")] public bool AdaptiveFieldGaps { get; set; } = true;
    [JsonPropertyName("adaptive_gap_max_mm")] public double AdaptiveGapMaxMm { get; set; } = 3.0;
    [JsonPropertyName("padding_mm")] public double PaddingMm { get; set; } = 2.9;
    // 0 = auto: header height follows its contents. Otherwise minimum visual height in millimetres.
    [JsonPropertyName("header_height_mm")] public double HeaderHeightMm { get; set; } = 0.0;
    [JsonPropertyName("header_top_mm")] public double HeaderTopMm { get; set; } = 2.5;
    [JsonPropertyName("header_gap_mm")] public double HeaderGapMm { get; set; } = 4.1;
    [JsonPropertyName("footer_height_mm")] public double FooterHeightMm { get; set; } = 5.0;
    // Zero keeps the default inset; positive values move footer text and its rule
    // closer to the card's lower edge without changing horizontal padding.
    [JsonPropertyName("footer_bottom_shift_mm")] public double FooterBottomShiftMm { get; set; } = 0.0;
    // Keep at least 0.5 mm of paper under footer text when possible.
    [JsonIgnore] public double FooterMaximumBottomShiftMm =>
        double.IsFinite(PaddingMm) ? Math.Max(0, PaddingMm - 0.5) : 0;
    [JsonIgnore] public double FooterBottomInsetMm =>
        Math.Max(0, PaddingMm-Math.Clamp(FooterBottomShiftMm,0,FooterMaximumBottomShiftMm));
    [JsonPropertyName("frame_width_pt")] public double FrameWidthPt { get; set; } = 0.3;
    [JsonPropertyName("corner_radius_mm")] public double CornerRadiusMm { get; set; } = 1.0;
    [JsonPropertyName("title_align")] public string TitleAlign { get; set; } = "left";
    // Header composition: group may be on the left or above the title.
    [JsonPropertyName("header_group_show")] public bool HeaderGroupShow { get; set; } = false;
    [JsonPropertyName("header_group_position")] public string HeaderGroupPosition { get; set; } = "top";
    [JsonPropertyName("header_group_font")] public string HeaderGroupFont { get; set; } = "Segoe UI";
    [JsonPropertyName("header_group_size_pt")] public double HeaderGroupSizePt { get; set; } = 6.2;
    [JsonPropertyName("header_group_weight")] public string HeaderGroupWeight { get; set; } = "500";
    [JsonPropertyName("header_group_color")] public string HeaderGroupColor { get; set; } = "#53616E";
    [JsonPropertyName("header_group_gap_mm")] public double HeaderGroupGapMm { get; set; } = 0.75;
    [JsonPropertyName("header_group_column_pct")] public int HeaderGroupColumnPct { get; set; } = 30;
    [JsonPropertyName("header_group_align")] public string HeaderGroupAlign { get; set; } = "left";
    [JsonPropertyName("header_accent_width_mm")] public double HeaderAccentWidthMm { get; set; } = 0.75;

    // UUID always uses the full Guid internally. Only the printed prefix is configurable.
    [JsonPropertyName("footer_uuid_show")] public bool FooterUuidShow { get; set; } = true;
    [JsonPropertyName("footer_uuid_length")] public int FooterUuidLength { get; set; } = 12;
    [JsonPropertyName("footer_uuid_separators")] public string FooterUuidSeparators { get; set; } = "none";
    [JsonPropertyName("footer_uuid_uppercase")] public bool FooterUuidUppercase { get; set; } = true;
    [JsonPropertyName("footer_uuid_prefix")] public bool FooterUuidPrefix { get; set; } = true;
    [JsonPropertyName("footer_uuid_size_pt")] public double FooterUuidSizePt { get; set; } = 5.6;
    [JsonPropertyName("footer_uuid_weight")] public string FooterUuidWeight { get; set; } = "400";
    [JsonPropertyName("footer_uuid_color")] public string FooterUuidColor { get; set; } = "#53616E";
    [JsonPropertyName("footer_date_format")] public string FooterDateFormat { get; set; } = "yyyy-MM-dd";
    [JsonPropertyName("footer_date_size_pt")] public double FooterDateSizePt { get; set; } = 5.6;
    [JsonPropertyName("footer_date_weight")] public string FooterDateWeight { get; set; } = "400";
    [JsonPropertyName("footer_date_color")] public string FooterDateColor { get; set; } = "#53616E";
    [JsonPropertyName("entry_icon_mode")] public string EntryIconMode { get; set; } = "custom";
    [JsonPropertyName("entry_icon_size_mm")] public double EntryIconSizeMm { get; set; } = 5.0;
    [JsonPropertyName("labels_position")] public string LabelsPosition { get; set; } = "above";
    [JsonPropertyName("label_column_pct")] public int LabelColumnPct { get; set; } = 29;
    [JsonPropertyName("labels_upper")] public bool LabelsUpper { get; set; } = true;
    [JsonPropertyName("section_headings")] public bool SectionHeadings { get; set; } = false;
    [JsonPropertyName("section_rules")] public bool SectionRules { get; set; } = true;
    [JsonPropertyName("header_fill")] public bool HeaderFill { get; set; } = true;
    [JsonPropertyName("header_accent")] public bool HeaderAccent { get; set; } = true;
    [JsonPropertyName("recovery_columns")] public int RecoveryColumns { get; set; } = 2;
    [JsonPropertyName("recovery_checkboxes")] public bool RecoveryCheckboxes { get; set; } = true;
    [JsonPropertyName("recovery_autosize")] public bool RecoveryAutosize { get; set; } = true;
    [JsonPropertyName("recovery_min_font_pt")] public double RecoveryMinFontPt { get; set; } = 5.5;
    [JsonPropertyName("recovery_max_font_pt")] public double RecoveryMaxFontPt { get; set; } = 11.0;
    [JsonPropertyName("continuation_repeat_label")] public bool ContinuationRepeatLabel { get; set; } = true;
    [JsonPropertyName("otp_compact")] public bool OtpCompact { get; set; } = true;
    [JsonPropertyName("background_color")] public string BackgroundColor { get; set; } = "#FFFFFF";
    [JsonPropertyName("ink_color")] public string InkColor { get; set; } = "#1A2B40";
    [JsonPropertyName("muted_color")] public string MutedColor { get; set; } = "#53616E";
    [JsonPropertyName("accent_color")] public string AccentColor { get; set; } = "#36618D";
    [JsonPropertyName("header_color")] public string HeaderColor { get; set; } = "#F4F7FA";
    [JsonPropertyName("rule_color")] public string RuleColor { get; set; } = "#CCD5E0";
    [JsonPropertyName("border_color")] public string BorderColor { get; set; } = "#C1C8D0";
    [JsonPropertyName("mark_zero")] public bool MarkZero { get; set; } = true;
    [JsonPropertyName("print_username")] public bool PrintUsername { get; set; } = true;
    [JsonPropertyName("print_password")] public bool PrintPassword { get; set; } = true;
    [JsonPropertyName("print_url")] public bool PrintUrl { get; set; } = true;
    [JsonPropertyName("print_notes")] public bool PrintNotes { get; set; } = true;
    [JsonPropertyName("print_custom")] public bool PrintCustom { get; set; } = true;
    [JsonPropertyName("show_date")] public bool ShowDate { get; set; } = true;
    [JsonPropertyName("crop_marks")] public bool CropMarks { get; set; } = true;
    [JsonPropertyName("cut_guide_mode")] public string CutGuideMode { get; set; } = "cross";
    [JsonPropertyName("cut_cross_size_mm")] public double CutCrossSizeMm { get; set; } = 1.6;
    [JsonPropertyName("cut_cross_width_pt")] public double CutCrossWidthPt { get; set; } = 0.3;
    [JsonPropertyName("frame")] public bool Frame { get; set; } = true;
    [JsonPropertyName("print_group")] public bool PrintGroup { get; set; } = false;
    [JsonPropertyName("short_fit_min_pct")] public int ShortFitMinPct { get; set; } = 78;
    [JsonPropertyName("split_boost_pct")] public int SplitBoostPct { get; set; } = 115;
    [JsonPropertyName("notes_size_pt")] public double NotesSizePt { get; set; } = 6.5;
    [JsonPropertyName("notes_mode")] public string NotesMode { get; set; } = "normal";
    [JsonPropertyName("group_long_values")] public bool GroupLongValues { get; set; } = false;
    [JsonPropertyName("group_size")] public int GroupSize { get; set; } = 4;
    [JsonPropertyName("group_min_length")] public int GroupMinLength { get; set; } = 12;
    [JsonPropertyName("group_spaces")] public double GroupSpaces { get; set; } = 1.0;
    [JsonPropertyName("password_group_mode")] public string PasswordGroupMode { get; set; } = "auto";
    [JsonPropertyName("entry_icon_print_mode")] public string EntryIconPrintMode { get; set; } = "original";
    [JsonPropertyName("duplex_enabled")] public bool DuplexEnabled { get; set; } = false;
    [JsonPropertyName("duplex_continuation")] public bool DuplexContinuation { get; set; } = true;
    [JsonPropertyName("duplex_back_fill")] public string DuplexBackFill { get; set; } = "design";
    [JsonPropertyName("duplex_combine_different")] public bool DuplexCombineDifferent { get; set; } = false;
    [JsonPropertyName("duplex_back_style")] public string DuplexBackStyle { get; set; } = "minimal";
    [JsonPropertyName("duplex_back_background")] public string DuplexBackBackground { get; set; } = "clean";
    [JsonPropertyName("duplex_back_language")] public string DuplexBackLanguage { get; set; } = "ru";
    [JsonPropertyName("duplex_flip")] public string DuplexFlip { get; set; } = "long";
    [JsonPropertyName("duplex_back_offset_x_mm")] public double DuplexBackOffsetXMm { get; set; } = 0.0;
    [JsonPropertyName("duplex_back_offset_y_mm")] public double DuplexBackOffsetYMm { get; set; } = 0.0;
    [JsonPropertyName("totp_qr_enabled")] public bool TotpQrEnabled { get; set; } = false;
    [JsonPropertyName("totp_qr_position")] public string TotpQrPosition { get; set; } = "center";
    [JsonPropertyName("totp_qr_height_mm")] public double TotpQrHeightMm { get; set; } = 28.0;
    [JsonPropertyName("totp_qr_min_height_mm")] public double TotpQrMinHeightMm { get; set; } = 14.0;

    // Decorative backgrounds are inherited unless a given side overrides them.
    // Never store decrypted entry data in these options.
    [JsonPropertyName("card_bg_pattern")] public string CardBgPattern { get; set; } = "clean";
    [JsonPropertyName("card_bg_gray")] public double CardBgGray { get; set; } = 0;
    [JsonPropertyName("card_bg_contrast")] public double CardBgContrast { get; set; } = 30;
    [JsonPropertyName("front_bg_override")] public bool FrontBgOverride { get; set; } = false;
    [JsonPropertyName("front_bg_pattern")] public string FrontBgPattern { get; set; } = "clean";
    [JsonPropertyName("front_bg_gray")] public double FrontBgGray { get; set; } = 0;
    [JsonPropertyName("front_bg_contrast")] public double FrontBgContrast { get; set; } = 30;
    [JsonPropertyName("back_bg_override")] public bool BackBgOverride { get; set; } = false;
    // Existing duplex_back_background is reused as the custom data-back pattern.
    [JsonPropertyName("back_bg_gray")] public double BackBgGray { get; set; } = 0;
    [JsonPropertyName("back_bg_contrast")] public double BackBgContrast { get; set; } = 30;
    [JsonPropertyName("empty_back_bg_source")] public string EmptyBackBgSource { get; set; } = "back";
    [JsonPropertyName("empty_back_bg_pattern")] public string EmptyBackBgPattern { get; set; } = "clean";
    [JsonPropertyName("empty_back_bg_gray")] public double EmptyBackBgGray { get; set; } = 0;
    [JsonPropertyName("empty_back_bg_contrast")] public double EmptyBackBgContrast { get; set; } = 30;
    [JsonPropertyName("empty_back_show_header")] public bool EmptyBackShowHeader { get; set; } = false;
    [JsonPropertyName("empty_back_show_footer")] public bool EmptyBackShowFooter { get; set; } = true;
    [JsonPropertyName("empty_back_show_art")] public bool EmptyBackShowArt { get; set; } = true;
    [JsonPropertyName("empty_back_show_icon")] public bool EmptyBackShowIcon { get; set; } = true;
    [JsonPropertyName("empty_back_show_frame")] public bool EmptyBackShowFrame { get; set; } = false;
    [JsonPropertyName("back_art_icon")] public string BackArtIcon { get; set; } = "lock";
    [JsonPropertyName("back_art_icon_size_mm")] public double BackArtIconSizeMm { get; set; } = 13;
    [JsonPropertyName("back_art_icon_color")] public string BackArtIconColor { get; set; } = "#455364";
    [JsonPropertyName("back_art_text_gap_mm")] public double BackArtTextGapMm { get; set; } = 4.0;
    [JsonPropertyName("back_art_icon_opacity")] public double BackArtIconOpacity { get; set; } = 75;
    [JsonPropertyName("back_art_icon_position")] public string BackArtIconPosition { get; set; } = "top";
    [JsonPropertyName("back_rules_style")] public string BackRulesStyle { get; set; } = "original";
    [JsonPropertyName("back_rules_width_mm")] public double BackRulesWidthMm { get; set; } = 34;
    [JsonPropertyName("back_rules_thickness_mm")] public double BackRulesThicknessMm { get; set; } = .18;
    [JsonPropertyName("back_rules_gap_mm")] public double BackRulesGapMm { get; set; } = 2.35;
    [JsonPropertyName("back_rules_opacity")] public double BackRulesOpacity { get; set; } = 100;

    // Clamp the geometry and shading inputs before rendering.
    internal void NormalizeHeaderFooter()
    {
        static double Safe(double value,double fallback,double min,double max) =>
            double.IsFinite(value) ? Math.Clamp(value,min,max) : fallback;
        HeaderHeightMm=Safe(HeaderHeightMm,0,0,50);
        HeaderTopMm=Safe(HeaderTopMm,2.5,.9,20);
        HeaderGapMm=Safe(HeaderGapMm,4.1,0,40);
        FooterHeightMm=Safe(FooterHeightMm,5,2,20);
        FooterBottomShiftMm=Safe(FooterBottomShiftMm,0,0,FooterMaximumBottomShiftMm);
        HeaderGroupSizePt=Safe(HeaderGroupSizePt,6.2,2,24);
        HeaderGroupGapMm=Safe(HeaderGroupGapMm,.75,0,10);
        HeaderGroupColumnPct=Math.Clamp(HeaderGroupColumnPct,15,55);
        HeaderAccentWidthMm=Safe(HeaderAccentWidthMm,.75,.2,3);
        FooterUuidLength=Math.Clamp(FooterUuidLength,4,32);
        FooterUuidSizePt=Safe(FooterUuidSizePt,5.6,2,18);
        FooterDateSizePt=Safe(FooterDateSizePt,5.6,2,18);
        CardBgGray=Safe(CardBgGray,0,0,100);
        CardBgContrast=Safe(CardBgContrast,30,0,100);
        FrontBgGray=Safe(FrontBgGray,0,0,100);
        FrontBgContrast=Safe(FrontBgContrast,30,0,100);
        BackBgGray=Safe(BackBgGray,0,0,100);
        BackBgContrast=Safe(BackBgContrast,30,0,100);
        EmptyBackBgGray=Safe(EmptyBackBgGray,0,0,100);
        EmptyBackBgContrast=Safe(EmptyBackBgContrast,30,0,100);
        BackArtIconSizeMm=Safe(BackArtIconSizeMm,13,4,28);
        BackArtTextGapMm=Safe(BackArtTextGapMm,4,2,12);
        BackArtIconOpacity=Safe(BackArtIconOpacity,75,0,100);
        BackRulesWidthMm=Safe(BackRulesWidthMm,34,8,55);
        BackRulesThicknessMm=Safe(BackRulesThicknessMm,.18,.05,1);
        BackRulesGapMm=Safe(BackRulesGapMm,2.35,.1,8);
        BackRulesOpacity=Safe(BackRulesOpacity,100,0,100);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CardAppearance))]
[JsonSerializable(typeof(string))]
internal partial class CardAppearanceJsonContext : JsonSerializerContext { }

public static class CardAppearanceStore
{
    private static string FilePath => SettingsLocation.FilePath("appearance.json");
    public static bool HasSavedAppearance => File.Exists(FilePath);
    public static CardAppearance Load()
    {
        try
        {
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length < 256_000)
                return Normalized(JsonSerializer.Deserialize(File.ReadAllBytes(FilePath),
                    CardAppearanceJsonContext.Default.CardAppearance));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    public static bool ContentEquals(CardAppearance left, CardAppearance right) =>
        JsonSerializer.SerializeToUtf8Bytes(left, CardAppearanceJsonContext.Default.CardAppearance)
            .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, CardAppearanceJsonContext.Default.CardAppearance));

    private static CardAppearance Normalized(CardAppearance? appearance)
    {
        var result = appearance ?? new();
        result.NormalizeHeaderFooter();
        return result;
    }
    public static void Save(CardAppearance model)
    {
        model.NormalizeHeaderFooter();
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(model, CardAppearanceJsonContext.Default.CardAppearance);
        var tmp = path + ".tmp";
        try { File.WriteAllBytes(tmp, bytes); File.Move(tmp, path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static IReadOnlyList<string> Templates()
    {
        var dir = Path.Combine(SettingsLocation.DirectoryPath, "templates");
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() : [];
    }
    // Use the embedded template display name rather than its on-disk filename.
    public static string ReadTemplateName(string path)
    {
        if (new FileInfo(path).Length > 256_000) throw new InvalidDataException("Template too large.");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var format) ||
            format.GetString() != "KeePassCardPrint-template-v1" ||
            !root.TryGetProperty("settings", out var settings) ||
            settings.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unsupported template format.");
        if (root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(name.GetString()))
            return name.GetString()!;
        return Path.GetFileNameWithoutExtension(path);
    }
    public static CardAppearance ReadTemplate(string path)
    {
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(Path.Combine(SettingsLocation.DirectoryPath, "templates")) + Path.DirectorySeparatorChar, PlatformServices.Current.FileNameComparison))
            throw new IOException("Шаблон должен находиться в каталоге templates.");
        if (new FileInfo(path).Length > 256_000) throw new IOException("Файл шаблона слишком велик.");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        if (doc.RootElement.TryGetProperty("format", out var format) &&
            format.GetString() == "KeePassCardPrint-template-v1" &&
            doc.RootElement.TryGetProperty("settings", out var settings))
            return Normalized(settings.Deserialize(CardAppearanceJsonContext.Default.CardAppearance));
        throw new InvalidDataException("Неподдерживаемый шаблон оформления.");
    }
    // The selection file stores only a relative PERSONAL filename, never a path
    // to an arbitrary location and never a built-in preset name.
    private static string SelectionPath => SettingsLocation.FilePath("appearance-template-source.json");
    public static string? ReadActivePersonalTemplate()
    {
        try
        {
            if (!File.Exists(SelectionPath) || new FileInfo(SelectionPath).Length > 512) return null;
            var file = JsonSerializer.Deserialize(File.ReadAllBytes(SelectionPath), CardAppearanceJsonContext.Default.String);
            if (file is null || file != Path.GetFileName(file) || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
            return Templates().Select(p => Path.GetFileName(p)).FirstOrDefault(x => string.Equals(x, file, PlatformServices.Current.FileNameComparison));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
    public static void SaveActivePersonalTemplate(string? filename)
    {
        if (filename is not null && (filename != Path.GetFileName(filename) ||
             !filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Invalid personal template filename.");
        var path = SelectionPath;
        if (filename is null) { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(filename, CardAppearanceJsonContext.Default.String));
            File.Move(tmp, path, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    private static string TemplatesDirectory => Path.Combine(SettingsLocation.DirectoryPath, "templates");
    private static string EnsurePersonalPath(string path)
    {
        var folder = Path.GetFullPath(TemplatesDirectory) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(folder, PlatformServices.Current.FileNameComparison) ||
            !full.EndsWith(".json", PlatformServices.Current.FileNameComparison))
            throw new IOException("Можно сохранять только собственные шаблоны в папке templates.");
        return full;
    }
    private static string MakeEnvelope(string name, CardAppearance model) =>
        "{\"format\":\"KeePassCardPrint-template-v1\",\"name\":" +
        JsonSerializer.Serialize(name, CardAppearanceJsonContext.Default.String) +
        ",\"settings\":" + JsonSerializer.Serialize(model, CardAppearanceJsonContext.Default.CardAppearance) + "}";

    // New personal copies cannot overwrite existing templates.
    public static string CreatePersonalTemplate(string name, CardAppearance model)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 64 || name.EndsWith('.') ||
            name.Any(c => !(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')) ||
            new[] { "CON", "PRN", "AUX", "NUL" }.Contains(name, StringComparer.OrdinalIgnoreCase) ||
            ((name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
              name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             name.Length == 4 && char.IsDigit(name[3])))
            throw new ArgumentException("Имя шаблона: 1–64 символа, буквы, цифры, пробелы, - или _.");
        var dir = TemplatesDirectory;
        Directory.CreateDirectory(dir);
        if (Directory.EnumerateFiles(dir,"*.json").Any(file =>
            string.Equals(Path.GetFileNameWithoutExtension(file), name, PlatformServices.Current.FileNameComparison)))
            throw new IOException("Шаблон с таким именем уже существует. Укажите другое имя.");
        var path = Path.Combine(dir, name + ".json");
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tmp, MakeEnvelope(name, model));
            File.Move(tmp, path, false);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
        return path;
    }

    // Only a verified personal template path may be overwritten. The public
    // built-in presets have no files, and cannot be passed to this method.
    public static void UpdatePersonalTemplate(string path, CardAppearance model)
    {
        path = EnsurePersonalPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Пользовательский шаблон не найден.", path);
        var name = ReadTemplateName(path);
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tmp, MakeEnvelope(name, model));
            File.Move(tmp, path, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

}
