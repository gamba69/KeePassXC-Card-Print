using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using KeePassXCCardPrint.Platforms;
using KeePassXCCardPrint.Services;
using SkiaSharp;

namespace KeePassXCCardPrint.ViewModels;

public sealed record AboutLibraryVersion(string Name, string Version);

/// <summary>Safe technical metadata only. No KDBX paths, entry titles or secrets.</summary>
public sealed class AboutViewModel : INotifyPropertyChanged
{
    private readonly UiSettings _settings;
    private bool _overlaysEnabled;
    private double _displayScaling = 1;
    private string _median = "—", _p95 = "—", _cache = "—", _memory = "—", _skiaNative = "—";
    private bool _checkedSkia;
    private string _copyStatus = "";
    private string _repositoryStatus = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<bool>? OverlayChangeRequested;

    public AboutViewModel(UiSettings settings)
    {
        _settings = settings;
        Libraries = new ObservableCollection<AboutLibraryVersion>
        {
            new("Avalonia UI", AboutDependencyVersions.Avalonia),
            new("Avalonia.Desktop", AboutDependencyVersions.AvaloniaDesktop),
            new("Avalonia.Themes.Fluent", AboutDependencyVersions.AvaloniaThemes),
            new("Avalonia.Skia", AboutDependencyVersions.AvaloniaSkia),
            new("Avalonia.HarfBuzz", AboutDependencyVersions.AvaloniaHarfBuzz),
            new("SkiaSharp", AboutDependencyVersions.SkiaSharp),
            new("SkiaSharp.HarfBuzz", AboutDependencyVersions.SkiaSharpHarfBuzz),
            new("HarfBuzzSharp", AboutDependencyVersions.HarfBuzzSharp),
            new("FluentIcons.Avalonia", AboutDependencyVersions.FluentIcons),
            new("DgNet.Keepass", AboutDependencyVersions.KeePass),
            new("Yubico.YubiKey", AboutDependencyVersions.YubiKey),
            new("QRCoder", AboutDependencyVersions.QRCoder)
        };
        foreach (var dependency in PlatformServices.Current.PlatformDependencies)
            Libraries.Add(new AboutLibraryVersion(dependency.Name, dependency.Version));
        // Split the actual resolved list evenly into two columns.
        var midpoint = (Libraries.Count + 1) / 2;
        LibrariesLeft = Libraries.Take(midpoint).ToArray();
        LibrariesRight = Libraries.Skip(midpoint).ToArray();
    }

    public IReadOnlyList<AboutLibraryVersion> LibrariesLeft { get; }
    public IReadOnlyList<AboutLibraryVersion> LibrariesRight { get; }
    public ObservableCollection<AboutLibraryVersion> Libraries { get; }
    private string T(string key) => Localizer.Text(_settings.Language, key);
    public string UpdateLanguage => _settings.Language;
    public const string RepositoryAddress = "https://github.com/gamba69/KeePassXC-Card-Print";
    public string AppTitle => "KeePassXC Card Print";
    public string AppVersion => $"{AppBuildInfo.Version} · {PlatformServices.Current.PlatformLabel} · {RuntimeInformation.ProcessArchitecture} · " +
        (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT") +
        " · © 2026 Igor К. Dulevich · Developed with AI assistance (OpenAI Codex) · ";
    public string RepositoryUrl => RepositoryAddress;
    public string RepositoryStatus => _repositoryStatus;
    public bool HasRepositoryStatus => !string.IsNullOrEmpty(_repositoryStatus);
    public string PurposeTitle => T("AboutPurposeTitle");
    public string Purpose => T("AboutPurpose");
    public string ComponentsTitle => T("AboutComponents");
    public string GraphicsTitle => T("AboutGraphics");
    public string PerformanceTitle => T("AboutPerformance");
    public string PrintingTitle => T("AboutPrinting");
    public string HostLabel => T("AboutHost");
    public string RuntimeLabel => T("AboutRuntime");
    public string NativeSkiaLabel => T("AboutNativeSkia");
    public string RenderEngineLabel => T("AboutRenderEngine");
    public string GpuLabel => T("AboutGpu");
    public string CardEngineLabel => T("AboutCardEngine");
    public string TextEngineLabel => T("AboutTextEngine");
    public string ScalingLabel => T("AboutScaling");
    public string SpoolerLabel => T("AboutPrintBackend");
    public string RasterLabel => T("AboutRasterExport");
    public string ExportLabel => T("AboutPdfExport");
    public string MedianLabel => T("AboutMedian");
    public string P95Label => T("AboutP95");
    public string CacheLabel => T("AboutCacheHit");
    public string MemoryLabel => T("AboutMemory");
    public string OverlaysLabel => T("AboutOverlays");
    public string CopyLabel => T("AboutCopy");
    public string SafeHint => T("AboutSafeHint");
    public string CopyStatus => _copyStatus;
    public bool HasCopyStatus => !string.IsNullOrEmpty(_copyStatus);
    public string HostValue => PlatformServices.Current.PlatformLabel;
    public string RuntimeValue => RuntimeInformation.FrameworkDescription;
    public string RenderingValue => "Avalonia.Skia · " + PlatformServices.Current.PlatformLabel;
    public string GpuValue => T("AboutGpuUnknown");
    public string CardEngineValue => T("AboutCardEngineValue");
    public string TextEngineValue => "Avalonia.HarfBuzz · HarfBuzzSharp";
    public string ScalingValue => (_displayScaling*100d).ToString("0.#", CultureInfo.CurrentCulture) + "%";
    public string SpoolerValue => PlatformServices.Current.Printing.SpoolerName + " · " + PlatformServices.Current.Printing.VectorFormatName;
    public string RasterValue => T("AboutRasterExportValue");
    public string ExportValue => T("AboutPdfExportValue");
    public string NativeSkiaValue => _skiaNative;
    public string MedianValue => _median;
    public string P95Value => _p95;
    public string CacheValue => _cache;
    public string MemoryValue => _memory;
    public bool OverlaysEnabled
    {
        get => _overlaysEnabled;
        set
        {
            if (_overlaysEnabled == value) return;
            _overlaysEnabled = value;
            Changed();
            OverlayChangeRequested?.Invoke(value);
        }
    }

    private void Changed([CallerMemberName]string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void SetCopyResult(bool success)
    {
        _copyStatus = T(success ? "AboutCopied" : "AboutCopyFailed");
        Changed(nameof(CopyStatus));
        Changed(nameof(HasCopyStatus));
    }

    public void SetRepositoryOpenFailed()
    {
        _repositoryStatus = T("AboutRepositoryOpenFailed");
        Changed(nameof(RepositoryStatus));
        Changed(nameof(HasRepositoryStatus));
    }

    public void Relocalize()
    {
        _copyStatus = "";
        _repositoryStatus = "";
        Changed(nameof(UpdateLanguage));
        Changed(nameof(CopyStatus));
        Changed(nameof(HasCopyStatus));
        Changed(nameof(RepositoryStatus));
        Changed(nameof(HasRepositoryStatus));
        foreach (var name in new[]
        {
            nameof(PurposeTitle),nameof(Purpose),nameof(ComponentsTitle),nameof(GraphicsTitle),
            nameof(PerformanceTitle),nameof(PrintingTitle),nameof(HostLabel),nameof(RuntimeLabel),
            nameof(NativeSkiaLabel),nameof(RenderEngineLabel),nameof(GpuLabel),nameof(CardEngineLabel),
            nameof(TextEngineLabel),nameof(ScalingLabel),nameof(SpoolerLabel),nameof(RasterLabel),
            nameof(ExportLabel),nameof(MedianLabel),nameof(P95Label),nameof(CacheLabel),nameof(MemoryLabel),
            nameof(OverlaysLabel),nameof(CopyLabel),nameof(SafeHint),nameof(GpuValue),
            nameof(CardEngineValue),nameof(RasterValue),nameof(ExportValue)
        }) Changed(name);
    }

    public void RefreshDiagnostics(double renderScaling)
    {
        _displayScaling = double.IsFinite(renderScaling) && renderScaling > 0 ? renderScaling : 1;
        Changed(nameof(ScalingValue));
        if (!_checkedSkia)
        {
            _checkedSkia = true;
            try { _skiaNative = SkiaSharpVersion.Native.ToString(); }
            catch (Exception) { _skiaNative = "—"; } // Native library might not be loaded yet.
            Changed(nameof(NativeSkiaValue));
        }
        var sample = PreviewTelemetry.Snapshot();
        _median = sample.MedianMs.HasValue ? sample.MedianMs.Value.ToString("0.0", CultureInfo.CurrentCulture)+" ms" : T("AboutNoSample");
        _p95 = sample.P95Ms.HasValue ? sample.P95Ms.Value.ToString("0.0", CultureInfo.CurrentCulture)+" ms" : T("AboutNoSample");
        _cache = sample.CacheHitPct.HasValue ? sample.CacheHitPct.Value.ToString("0.0", CultureInfo.CurrentCulture)+"%" : T("AboutNoSample");
        try { using var process = Process.GetCurrentProcess(); _memory = (process.WorkingSet64/1048576d).ToString("0", CultureInfo.CurrentCulture)+" MiB"; }
        catch (Exception) { _memory = "—"; }
        Changed(nameof(MedianValue)); Changed(nameof(P95Value)); Changed(nameof(CacheValue)); Changed(nameof(MemoryValue));
    }

    public string SafeDiagnosticSummary()
    {
        // Fixed allowlist. No files, paths, record names, device serials or settings.
        var lines = new List<string>
        {
            "KeePassXC Card Print " + AppBuildInfo.Version,
            "Platform: " + PlatformServices.Current.PlatformLabel + " / " + RuntimeInformation.ProcessArchitecture,
            "Runtime: " + RuntimeInformation.FrameworkDescription + " / " + (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT"),
            "UI: Avalonia.Skia (" + PlatformServices.Current.PlatformLabel + " host)",
            "GPU backend: unavailable through public Avalonia API",
            "Native Skia: " + _skiaNative,
            "Display scaling: " + (_displayScaling*100d).ToString("0.#", CultureInfo.InvariantCulture) + "%",
            "Preview median (last 30 samples): " + _median,
            "Preview P₉₅ (last 30 samples): " + _p95,
            "Preview cache hit: " + _cache,
            "Process working set: " + _memory,
            "Spooler/vector: " + SpoolerValue,
            "Dependencies:"
        };
        foreach (var item in Libraries) lines.Add("  " + item.Name + ": " + item.Version);
        return string.Join(Environment.NewLine, lines);
    }
}
