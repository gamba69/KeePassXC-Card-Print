using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeePassXCCardPrint.Services;

// UI-only. NEVER add passwords, KDBX contents, record names or OTP secrets here.
public sealed class PrintCalibration
{
    public double FrontX {get;set;}
    public double FrontY {get;set;}
    public double BackX {get;set;}
    public double BackY {get;set;}
    public void Clamp()
    {
        FrontX=double.IsFinite(FrontX)?Math.Clamp(FrontX,-10,10):0;
        FrontY=double.IsFinite(FrontY)?Math.Clamp(FrontY,-10,10):0;
        BackX=double.IsFinite(BackX)?Math.Clamp(BackX,-10,10):0;
        BackY=double.IsFinite(BackY)?Math.Clamp(BackY,-10,10):0;
    }
}

public sealed class UiSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "auto";
    public string Theme { get; set; } = "auto";
    public double WindowWidth { get; set; } = 1670;
    public double WindowHeight { get; set; } = 910;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool Maximized { get; set; }
    public double LeftPaneWidth { get; set; } = 950;
    public int ActiveTab { get; set; }
    public string PreviewZoom { get; set; } = "fit";
    public bool MappingScopeCurrent { get; set; } = true;
    public bool MappingAllColumns { get; set; }
    public bool PreviewMonochrome { get; set; }
    // Optional host-specific layout switch while the KDBX master-password field has focus.
    public bool AutoEnglishPasswordLayout { get; set; } = true;
    // Independent preview crossfade triggers. No KDBX data is stored here.
    public bool PreviewAnimation { get; set; } = true;
    public bool PreviewAnimateAfterDelay { get; set; } = true;
    public bool PreviewAnimateNavigation { get; set; } = true;
    public bool PreviewAnimateSideChange { get; set; } = true;
    public int PreviewAnimationDurationMs { get; set; } = 400;
    public int PreviewZoomPercent { get; set; } = 100;
    // Adjustable wheel sensitivity, percent per wheel notch; no rendering cost.
    public int PreviewWheelStepPercent { get; set; } = 8;
    public string PreviewToolbarDock { get; set; } = "top";
    public double MappingListFraction { get; set; } = .45;
    public bool PreviewShowSheets { get; set; }
    public string PaperFormat { get; set; } = "A4";
    public int PrintStartSlot { get; set; } = 1;
    public bool PrintLandscape { get; set; }
    public bool PrintShortEdgeFlip { get; set; }
    public string LastPrintDevice { get; set; } = "";
    public string PrintMethod { get; set; } = "auto";
    public int PrintRasterDpi { get; set; } = 600;
    public Dictionary<string, PrintCalibration> PrintCalibrations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UiSettings))]
[JsonSerializable(typeof(PrintCalibration))]
internal partial class UiSettingsJsonContext : JsonSerializerContext { }

public sealed class UiSettingsStore
{
    private const int MaxJsonBytes = 64 * 1024;
    private readonly string _directory;
    public string SettingsPath => Path.Combine(_directory, "ui-shell.json");

    public UiSettingsStore(string? directory = null)
    {
        _directory = directory ?? SettingsLocation.DirectoryPath;
    }

    public UiSettings Load()
    {
        if (File.Exists(SettingsPath))
        {
            try
            {
                var data = ReadBounded(SettingsPath);
                var value = JsonSerializer.Deserialize(data, UiSettingsJsonContext.Default.UiSettings);
                if (value is not null && value.SchemaVersion == 1)
                    return Validate(value);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                // Invalid UI settings must not affect the KDBX database.
            }
        }
        return new UiSettings();
    }

    public void Save(UiSettings settings)
    {
        var validated = Validate(settings);
        Directory.CreateDirectory(_directory);
        var tmp = Path.Combine(_directory, ".ui-shell." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(validated, UiSettingsJsonContext.Default.UiSettings);
            File.WriteAllBytes(tmp, json);
            File.Move(tmp, SettingsPath, true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static byte[] ReadBounded(string path, int limit = MaxJsonBytes)
    {
        var length = new FileInfo(path).Length;
        if (length > limit) throw new IOException("UI configuration is too large.");
        return File.ReadAllBytes(path);
    }

    private static UiSettings Validate(UiSettings data)
    {
        data.SchemaVersion = 1;
        data.PreviewToolbarDock = data.PreviewToolbarDock is "top" or "side" ? data.PreviewToolbarDock : "top";
        data.MappingListFraction = AdaptiveViewport.MappingFraction(data.MappingListFraction);
        data.PreviewAnimationDurationMs = Math.Clamp(data.PreviewAnimationDurationMs,50,500);
        data.PreviewZoomPercent = Math.Clamp(data.PreviewZoomPercent,5,600);
        data.PreviewWheelStepPercent = Math.Clamp(data.PreviewWheelStepPercent,2,30);
        data.PrintStartSlot = Math.Clamp(data.PrintStartSlot,1,36);
        if(data.PrintMethod is not ("auto" or "vector" or "raster")) data.PrintMethod="auto";
        if(data.PrintRasterDpi is not (300 or 600 or 1200))data.PrintRasterDpi=600;
        if(data.LastPrintDevice is null || data.LastPrintDevice.Length>256)data.LastPrintDevice="";
        data.PrintCalibrations ??= new(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in data.PrintCalibrations.Values)entry.Clamp();
        if (data.PaperFormat is not ("A3" or "A4" or "A5" or "A6" or "Letter" or "Legal")) data.PaperFormat = "A4";
        if (data.Language is not ("auto" or "ru" or "uk" or "en")) data.Language = "auto";
        if (data.Theme is not ("auto" or "dark" or "light")) data.Theme = "auto";
        if (!double.IsFinite(data.WindowWidth) || data.WindowWidth < 400 || data.WindowWidth > 10000) data.WindowWidth = 1670;
        if (!double.IsFinite(data.WindowHeight) || data.WindowHeight < 300 || data.WindowHeight > 10000) data.WindowHeight = 910;
        if (data.WindowX is < -40000 or > 40000) data.WindowX = null;
        if (data.WindowY is < -40000 or > 40000) data.WindowY = null;
        if (!double.IsFinite(data.LeftPaneWidth) || data.LeftPaneWidth < 490 || data.LeftPaneWidth > 10000)
            data.LeftPaneWidth = 950;
        data.ActiveTab = Math.Clamp(data.ActiveTab, 0, 5);
        if (data.PreviewZoom is not ("fit" or "width" or "100" or "card")) data.PreviewZoom = "fit";
        return data;
    }
}
