using KeePassXCCardPrint.Rendering;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Platforms.Windows.Printing;

/// <summary>Windows-only DEVMODE is opaque to all shared printing and UI code.</summary>
internal sealed record WindowsPrinterDeviceOptions(byte[] DevMode) : PrinterDeviceOptions;

/// <summary>All Windows spooler APIs and the XPS native bridge are behind this service.</summary>
internal sealed class WindowsPrinterService : IPrinterService
{
    public string VectorFormatName => "XPS";
    public string SpoolerName => "Windows";
    public IReadOnlyList<string> Printers() => WindowsRasterPrinter.Printers();
    public string DefaultPrinter() => WindowsRasterPrinter.DefaultPrinter();
    public bool IsKnownManualDuplexOnly(string printer) => WindowsRasterPrinter.IsKnownManualDuplexOnly(printer);
    public bool SupportsDuplex(string printer) => WindowsRasterPrinter.SupportsDuplex(printer);
    public (int X, int Y)? CurrentDriverResolution(string printer, PrinterDeviceOptions? settings)
        => WindowsRasterPrinter.CurrentDriverResolution(printer, UnwrapOrNull(settings));
    public IReadOnlyList<(int X, int Y)> SupportedResolutions(string printer)
        => WindowsRasterPrinter.SupportedResolutions(printer);
    public bool IsVectorAvailable(string printer)
        => WindowsXpsPrinter.IsBridgeInstalled() && WindowsRasterPrinter.CanAcceptDocumentJobs(printer);
    public bool MaySaveDocumentToFile(string printer)
        => WindowsRasterPrinter.MaySaveDocumentToFile(printer);
    public PrinterDeviceOptions? Properties(string printer, nint owner, PrinterDeviceOptions? previous)
    {
        var settings = WindowsRasterPrinter.Properties(printer, owner, UnwrapOrNull(previous));
        return settings is null ? null : new WindowsPrinterDeviceOptions(settings);
    }
    public PrinterDeviceOptions DefaultJobSettings(string printer)
        => new WindowsPrinterDeviceOptions(WindowsRasterPrinter.DefaultJobSettings(printer));

    private static byte[]? UnwrapOrNull(PrinterDeviceOptions? options) => options switch
    {
        null => null,
        WindowsPrinterDeviceOptions win => win.DevMode,
        _ => throw new ArgumentException("Printer driver settings belong to a different platform.", nameof(options))
    };

    public PrintSubmissionResult Submit(PrintSubmission job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        // The printer decision and its safe pre-spool fallback are platform details.
        var mode = UnwrapOrNull(job.DeviceOptions) ?? WindowsRasterPrinter.DefaultJobSettings(job.Printer);
        void Raster(int dpi) => WindowsRasterPrinter.Print(job.Printer, mode, job.Sheets,
            job.Appearance, dpi, job.FrontX, job.FrontY, job.BackX, job.BackY,
            job.DriverDuplex, cancellationToken);
        if (job.RenderingMode == PrintRenderingMode.Raster ||
            (job.RenderingMode == PrintRenderingMode.Automatic && !IsVectorAvailable(job.Printer)))
        {
            int dpi = job.RenderingMode == PrintRenderingMode.Raster ? job.RasterDpi : 600;
            Raster(dpi);
            return new PrintSubmissionResult($"Растр {dpi} DPI");
        }
        try
        {
            WindowsXpsPrinter.Print(job.Printer, mode, job.Sheets, job.Appearance,
                job.FrontX, job.FrontY, job.BackX, job.BackY,
                job.DriverDuplex, cancellationToken);
            return new PrintSubmissionResult("Вектор XPS");
        }
        catch (XpsPrintException failure) when
            (job.RenderingMode == PrintRenderingMode.Automatic && !failure.MayHaveSubmitted)
        {
            // Only retry if the Windows bridge guarantees NOTHING entered the spooler.
            Raster(600);
            return new PrintSubmissionResult("Растр 600 DPI (резерв)");
        }
        catch (XpsPrintException failure)
        {
            throw new PrintSubmissionException(failure.Message, failure.MayHaveSubmitted, failure);
        }
    }

    public void ValidateVectorBackend() => WindowsXpsPrinter.ValidateSyntheticPage();
}
