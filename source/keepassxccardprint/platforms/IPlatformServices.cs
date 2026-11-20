using KeePassXCCardPrint.Rendering;
using KeePassXCCardPrint.Services;
using KeePassXC.CloudTransport;

namespace KeePassXCCardPrint.Platforms;

/// <summary>Opaque printer-specific settings. Shared UI must never parse native driver data.</summary>
public abstract record PrinterDeviceOptions;

public enum PrintRenderingMode { Automatic, Vector, Raster }

/// <summary>One ready-to-submit group of physical sheets; no secret files are produced.</summary>
public sealed record PrintSubmission(
    string Printer, PrinterDeviceOptions? DeviceOptions, IReadOnlyList<CardPrintSheet> Sheets,
    CardAppearance Appearance, PrintRenderingMode RenderingMode, int RasterDpi,
    double FrontX, double FrontY, double BackX, double BackY, int DriverDuplex);

public sealed record PrintSubmissionResult(string MethodLabel);

/// <summary>MayHaveSubmitted prevents unsafe automatic retries after ambiguous spooler failures.</summary>
public sealed class PrintSubmissionException(string message, bool mayHaveSubmitted, Exception? cause = null)
    : Exception(message, cause)
{
    public bool MayHaveSubmitted { get; } = mayHaveSubmitted;
}

public interface IPrinterService
{
    string VectorFormatName { get; }
    string SpoolerName { get; }
    IReadOnlyList<string> Printers();
    string DefaultPrinter();
    bool IsKnownManualDuplexOnly(string printer);
    bool SupportsDuplex(string printer);
    (int X, int Y)? CurrentDriverResolution(string printer, PrinterDeviceOptions? settings);
    IReadOnlyList<(int X, int Y)> SupportedResolutions(string printer);
    bool IsVectorAvailable(string printer);
    bool MaySaveDocumentToFile(string printer);
    PrinterDeviceOptions? Properties(string printer, nint owner, PrinterDeviceOptions? previous);
    PrinterDeviceOptions DefaultJobSettings(string printer);
    PrintSubmissionResult Submit(PrintSubmission job, CancellationToken cancellationToken);
    void ValidateVectorBackend();
}

/// <summary>Storage-mode persistence and program-specific roots vary by host/OS.</summary>
public interface IConfigurationEnvironment
{
    string AppDataRoot { get; }
    string LocalRoot { get; }
    ConfigurationStorage ReadMode();
    void Activate(ConfigurationStorage mode);
}

/// <summary>A scoped input-locale change; the user may always override it.</summary>
public interface IPasswordInputLanguageScope
{
    void Enter();
    void Leave();
}

public interface ISecurityKeyService
{
    IReadOnlyList<string> AvailableSerials();
    byte[] ComputeChallengeResponse(byte[] challenge, int slot, bool shortChallenge, string? serial);
}

/// <summary>CloudTransport host dependencies supplied by the current OS.
/// No P/Invoke or platform credential storage lives in shared code.</summary>
public sealed record ProtonCliStatus(string State = "idle", string Current = "",
    string Latest = "", string Path = "", string Error = "");

public interface ICloudPlatformServices
{
    ICloudSecretStore Secrets { get; }
    CloudTransportServices CreateServices();
    string ResolveProtonExecutable(string? configured);
    Task<ProtonCliStatus> CheckProtonCliAsync(string? configured, CancellationToken ct);
    Task<string> InstallProtonCliAsync(string? configured, IProgress<int>? progress, CancellationToken ct);
}

/// <summary>Only OS-dependent capabilities belong here. Avalonia/Skia services stay shared.</summary>
public readonly record struct WindowFrameInsets(int WidthPx, int HeightPx);

public readonly record struct PlatformDependency(string Name, string Version);

public interface IPlatformServices
{
    string PlatformLabel { get; }
    IReadOnlyList<PlatformDependency> PlatformDependencies { get; }
    string ResourceDirectory { get; }
    WindowFrameInsets WindowFrame { get; }
    StringComparison FileNameComparison { get; }
    string NormalizeDatabaseIdentityPath(string path);
    string ResolveSystemFontFamily(string family);
    string SystemFontOptions { get; }
    string FallbackTextFont { get; }
    string FallbackSecretFont { get; }
    IPrinterService Printing { get; }
    IConfigurationEnvironment Configuration { get; }
    ISecurityKeyService SecurityKeys { get; }
    ICloudPlatformServices Cloud { get; }
    IPasswordInputLanguageScope CreatePasswordInputLanguageScope();
}

/// <summary>Configured once by the platform host, before any UI or storage is initialized.</summary>
public static class PlatformServices
{
    private static IPlatformServices? _current;
    public static bool IsInitialized => _current is not null;
    public static IPlatformServices Current => _current
        ?? throw new InvalidOperationException("Platform services must be initialized by the host.");

    public static void Initialize(IPlatformServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (Interlocked.CompareExchange(ref _current, services, null) is not null)
            throw new InvalidOperationException("Platform services have already been initialized.");
    }
}
