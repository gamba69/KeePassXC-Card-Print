using KeePassXCCardPrint.Services;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using KeePassXCCardPrint.Rendering;
using SkiaSharp;

namespace KeePassXCCardPrint.Platforms.Windows.Printing;

/// <summary>Direct Windows printer DC output; the application never creates a temporary document.
/// Physical paper dimensions are validated before any KDBX contents are spooled.</summary>
internal static class WindowsRasterPrinter
{
    private const int PrinterEnumLocal=2, PrinterEnumConnections=4;
    private const int PhysicalWidth=110, PhysicalHeight=111, PhysicalOffsetX=112, PhysicalOffsetY=113;
    private const int DpiX=88, DpiY=90, ColorOnColor=3, DibRgbColors=0, Srccopy=0x00CC0020;

    [DllImport("winspool.drv", EntryPoint="EnumPrintersW", CharSet=CharSet.Unicode, SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr buffer,
        uint bufferSize, out uint needed, out uint returned);
    [DllImport("winspool.drv", EntryPoint="GetDefaultPrinterW", CharSet=CharSet.Unicode, SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDefaultPrinter(System.Text.StringBuilder? buffer, ref uint length);
    [DllImport("winspool.drv", EntryPoint="OpenPrinterW", CharSet=CharSet.Unicode, SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", EntryPoint="ClosePrinter", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr handle);
    [DllImport("winspool.drv", EntryPoint="DocumentPropertiesW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern int DocumentProperties(IntPtr hwnd, IntPtr printer, string name,
        IntPtr output, IntPtr input, int mode);
    [DllImport("winspool.drv", EntryPoint="DeviceCapabilitiesW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern int DeviceCapabilities(string device,IntPtr port,short capability,IntPtr output,IntPtr mode);
    [DllImport("winspool.drv", EntryPoint="GetPrinterW", CharSet=CharSet.Unicode, SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPrinter(IntPtr handle,uint level,IntPtr buffer,
        uint bufferSize,out uint needed);

    /// <summary>Conservative, non-printing check for a Windows document spool path.
    /// A RAW-only queue cannot accept an XPS document. Successful detection
    /// does NOT promise that the physical driver retains vectors.</summary>
    internal static bool CanAcceptDocumentJobs(string printer)
    {
        if(!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(printer))return false;
        if(!OpenPrinter(printer,out var handle,IntPtr.Zero))return false;
        try
        {
            GetPrinter(handle,2,IntPtr.Zero,0,out uint needed);
            if(needed==0 || needed>4_000_000)return false;
            IntPtr memory=Marshal.AllocHGlobal(checked((int)needed));
            try
            {
                if(!GetPrinter(handle,2,memory,needed,out _))return false;
                // PRINTER_INFO_2 has thirteen pointer-sized fields before Attributes.
                uint attributes=unchecked((uint)Marshal.ReadInt32(memory,IntPtr.Size*13));
                const uint PrinterAttributeRawOnly=0x00001000;
                return (attributes & PrinterAttributeRawOnly)==0;
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        finally { ClosePrinter(handle); }
    }

    /// <summary>Conservative warning trigger for queues that may write a file.
    /// Covers common virtual printers and Windows FILE:/PORTPROMPT: ports.
    /// This is a *warning heuristic*, never a claim that a queue is safe.
    /// </summary>
    public static bool MaySaveDocumentToFile(string printer)
    {
        if(string.IsNullOrWhiteSpace(printer))return false;
        string[] virtualNames =
        [
            "print to pdf", "pdfcreator", "pdf24", "adobe pdf", "foxit pdf",
            "bullzip", "cutepdf", "dopdf", "pdf printer", "xps document writer",
            "microsoft xps", "onenote", "fax", "snagit"
        ];
        if(virtualNames.Any(marker=>printer.Contains(marker,StringComparison.OrdinalIgnoreCase)))
            return true;
        if(!OperatingSystem.IsWindows())return false;
        using var port=PrinterPortHandle.Open(printer);
        var name=port.Handle==IntPtr.Zero?null:Marshal.PtrToStringUni(port.Handle);
        return name is not null &&
            (name.StartsWith("FILE:",StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("PORTPROMPT:",StringComparison.OrdinalIgnoreCase));
    }

    // This is a deliberately small and sourced compatibility correction, not a
    // general device database. HP documents the P1102/P1102w as MANUAL duplex.
    // Older host-based HP drivers may report DC_DUPLEX=1 for driver-assisted
    // manual flipping. Never present that as a physical auto-flip unit.
    public static bool IsKnownManualDuplexOnly(string printer)
        => !string.IsNullOrWhiteSpace(printer) &&
           printer.Contains("LaserJet", StringComparison.OrdinalIgnoreCase) &&
           Regex.IsMatch(printer, @"\bP\s*1102w?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Returns only what the Windows driver DECLARES, not an independently
    // verified physical feature. Printer-specific manual-only overrides apply.
    public static bool SupportsDuplex(string printer)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(printer) ||
            IsKnownManualDuplexOnly(printer)) return false;
        using var port = PrinterPortHandle.Open(printer);
        return DeviceCapabilities(printer, port.Handle, 7, IntPtr.Zero, IntPtr.Zero) == 1;
    }

    private sealed class PrinterPortHandle : IDisposable
    {
        public IntPtr Handle { get; private set; }
        private PrinterPortHandle(IntPtr port) => Handle = port;
        public static PrinterPortHandle Open(string printer)
        {
            if (!OpenPrinter(printer, out var handle, IntPtr.Zero)) return new(IntPtr.Zero);
            try
            {
                GetPrinter(handle, 2, IntPtr.Zero, 0, out uint needed);
                if (needed < (uint)(IntPtr.Size * 14) || needed > 4_000_000)
                    return new(IntPtr.Zero);
                var data = Marshal.AllocHGlobal((int)needed);
                try
                {
                    if (!GetPrinter(handle, 2, data, needed, out _)) return new(IntPtr.Zero);
                    var portPtr = Marshal.ReadIntPtr(data, IntPtr.Size * 3);
                    var portName = Marshal.PtrToStringUni(portPtr);
                    return new(string.IsNullOrWhiteSpace(portName)
                        ? IntPtr.Zero : Marshal.StringToHGlobalUni(portName));
                }
                finally { Marshal.FreeHGlobal(data); }
            }
            finally { ClosePrinter(handle); }
        }
        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            Marshal.FreeHGlobal(Handle);
            Handle = IntPtr.Zero;
        }
    }

    /// <summary>Current raster resolution actually negotiated with the Windows driver.
    /// May differ from HP FastRes/other proprietary quality enhancement modes.</summary>
    public static (int X, int Y)? CurrentDriverResolution(string printer, byte[]? settings = null)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(printer)) return null;
        IntPtr mode = IntPtr.Zero;
        IntPtr dc = IntPtr.Zero;
        try
        {
            if (settings is { Length: > 0 })
            {
                mode = Marshal.AllocHGlobal(settings.Length);
                Marshal.Copy(settings, 0, mode, settings.Length);
            }
            dc = CreatePrinterDc("WINSPOOL", printer, IntPtr.Zero, mode);
            if (dc == IntPtr.Zero) return null;
            int x = GetDeviceCaps(dc, DpiX), y = GetDeviceCaps(dc, DpiY);
            return x is >= 75 and <= 9600 && y is >= 75 and <= 9600 ? (x, y) : null;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return null; }
        finally
        {
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (mode != IntPtr.Zero) Marshal.FreeHGlobal(mode);
        }
    }

    /// <summary>Resolutions declared by the currently installed Windows printer driver.
    /// These are OUTPUT-device modes, not the rasterization DPI chosen by this app.</summary>
    public static IReadOnlyList<(int X, int Y)> SupportedResolutions(string printer)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(printer)) return [];
        const short DcEnumResolutions = 13;
        using var port = PrinterPortHandle.Open(printer);
        int count = DeviceCapabilities(printer, port.Handle, DcEnumResolutions,
            IntPtr.Zero, IntPtr.Zero);
        if (count < 1 || count > 128) return [];
        IntPtr buffer = Marshal.AllocHGlobal(checked(count * sizeof(int) * 2));
        try
        {
            int returned = DeviceCapabilities(printer, port.Handle, DcEnumResolutions,
                buffer, IntPtr.Zero);
            if (returned < 1 || returned > count) return [];
            var modes = new SortedSet<(int X, int Y)>();
            for (int i = 0; i < returned; i++)
            {
                int x = Marshal.ReadInt32(buffer, i * 8);
                int y = Marshal.ReadInt32(buffer, i * 8 + 4);
                if (x is >= 75 and <= 9600 && y is >= 75 and <= 9600)
                    modes.Add((x, y));
            }
            return modes.ToArray();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("gdi32.dll", EntryPoint="CreateDCW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr CreatePrinterDc(string driver, string device, IntPtr output, IntPtr devMode);
    [DllImport("gdi32.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc,int index);
    [DllImport("gdi32.dll", EntryPoint="StartDocW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern int StartDoc(IntPtr dc,ref DocInfo info);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern int EndDoc(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern int AbortDoc(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern int StartPage(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern int EndPage(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc,int mode);
    [DllImport("gdi32.dll", SetLastError=true)]
    private static extern int StretchDIBits(IntPtr dc,int xDest,int yDest,int destWidth,int destHeight,
        int xSrc,int ySrc,int srcWidth,int srcHeight,byte[] bits,
        ref BitmapInfo info,uint usage,uint rasterOperation);

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct DocInfo
    {
        public int Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        public IntPtr Output;
        public IntPtr DataType;
        public uint Type;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter,YPelsPerMeter;
        public uint ColorsUsed,ColorsImportant;
    }

    public static IReadOnlyList<string> Printers()
    {
        if(!OperatingSystem.IsWindows())return [];
        EnumPrinters(PrinterEnumLocal|PrinterEnumConnections,null,4,IntPtr.Zero,0,out uint needed,out _);
        if(needed==0 || needed>4_000_000)return [];
        IntPtr buffer=Marshal.AllocHGlobal(checked((int)needed));
        try
        {
            if(!EnumPrinters(PrinterEnumLocal|PrinterEnumConnections,null,4,buffer,needed,out _,out uint count))return [];
            var names=new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            int step=IntPtr.Size*2+4;
            // PRINTER_INFO_4 has pointer,pointer,DWORD; 64-bit struct alignment is 8.
            step=(step+IntPtr.Size-1)/IntPtr.Size*IntPtr.Size;
            for(int i=0;i<count;i++)
            {
                var ptr=Marshal.ReadIntPtr(buffer,checked(i*step));
                var name=Marshal.PtrToStringUni(ptr);
                if(!string.IsNullOrWhiteSpace(name))names.Add(name);
            }
            return names.ToArray();
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }

    public static string DefaultPrinter()
    {
        if(!OperatingSystem.IsWindows())return "";
        uint size=0;
        GetDefaultPrinter(null,ref size);
        if(size is 0 or > 32768)return "";
        var result=new System.Text.StringBuilder((int)size);
        return GetDefaultPrinter(result,ref size)?result.ToString():"";
    }

    /// <summary>Open the printer manufacturer's actual job properties, retaining the returned
    /// DEVMODE privately for this dialog only. No system defaults are modified.</summary>
    public static byte[]? Properties(string printer,IntPtr owner,byte[]? previous)
    {
        if(!OpenPrinter(printer,out var handle,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            int length=DocumentProperties(owner,handle,printer,IntPtr.Zero,IntPtr.Zero,0);
            if(length<=0 || length>65536)throw new InvalidOperationException("Драйвер не предоставил свойства печати.");
            IntPtr buffer=Marshal.AllocHGlobal(length);
            try
            {
                // Load the *current* printer defaults, then ask the driver to edit a job-local copy.
                if(DocumentProperties(owner,handle,printer,buffer,IntPtr.Zero,2)<0)
                    throw new InvalidOperationException("Не удалось получить параметры драйвера.");
                if(previous is {Length:>0} && previous.Length==length)Marshal.Copy(previous,0,buffer,length);
                int answer=DocumentProperties(owner,handle,printer,buffer,buffer,4|8|2);
                if(answer!=1)return previous; // IDCANCEL leaves previous settings unchanged.
                byte[] result=new byte[length];
                Marshal.Copy(buffer,result,0,length);
                return result;
            }
            finally{Marshal.FreeHGlobal(buffer);}
        }
        finally{ClosePrinter(handle);}
    }

    public static byte[] DefaultJobSettings(string printer)
    {
        if(!OpenPrinter(printer,out var handle,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            int size=DocumentProperties(IntPtr.Zero,handle,printer,IntPtr.Zero,IntPtr.Zero,0);
            if(size<=0||size>65536)throw new InvalidOperationException("Не удаётся получить настройки принтера.");
            IntPtr buffer=Marshal.AllocHGlobal(size);
            try
            {
                if(DocumentProperties(IntPtr.Zero,handle,printer,buffer,IntPtr.Zero,2)<0)
                    throw new InvalidOperationException("Не удаётся получить настройки принтера.");
                var result=new byte[size];
                Marshal.Copy(buffer,result,0,size);
                return result;
            }
            finally{Marshal.FreeHGlobal(buffer);}
        }
        finally{ClosePrinter(handle);}
    }

    /// <param name="duplex">0=simplex, 1=driver duplex long edge, 2=short edge.</param>
    public static void Print(string printer,byte[]? devmode,IReadOnlyList<CardPrintSheet> sheets,
        CardAppearance appearance,int dpi,double frontX,double frontY,double backX,double backY,
        int duplex=0,CancellationToken token=default)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Для печати необходим Windows.");
        if(string.IsNullOrWhiteSpace(printer)||sheets.Count==0)throw new InvalidOperationException("Не выбран принтер или листы.");
        if(dpi is not (300 or 600 or 1200))throw new ArgumentOutOfRangeException(nameof(dpi));
        IntPtr mode=IntPtr.Zero,dc=IntPtr.Zero;
        bool started=false;
        try
        {
            var settings=devmode??DefaultJobSettings(printer);
            if(settings.Length<96)throw new InvalidOperationException("Драйвер вернул неполные параметры печати.");
            settings=(byte[])settings.Clone();
            int paperCode=sheets[0].Paper.Format switch
            {"A3"=>8,"A4"=>9,"A5"=>11,"A6"=>70,"Letter"=>1,"Legal"=>5,_=>0};
            uint fields=BitConverter.ToUInt32(settings,72);
            fields|=0x00000001u|0x00001000u; // DM_ORIENTATION | DM_DUPLEX
            bool landscape=sheets[0].Paper.WidthMm>sheets[0].Paper.HeightMm;
            BitConverter.GetBytes((short)(landscape?2:1)).CopyTo(settings,76);
            BitConverter.GetBytes((short)(duplex switch {1=>2,2=>3,_=>1})).CopyTo(settings,94);
            if(paperCode!=0)
            {
                fields|=0x00000002u; // DM_PAPERSIZE
                BitConverter.GetBytes((short)paperCode).CopyTo(settings,78);
            }
            BitConverter.GetBytes(fields).CopyTo(settings,72);
            mode=Marshal.AllocHGlobal(settings.Length);
            Marshal.Copy(settings,0,mode,settings.Length);
            dc=CreatePrinterDc("WINSPOOL",printer,IntPtr.Zero,mode);
            if(dc==IntPtr.Zero)throw new InvalidOperationException("Не удалось открыть принтер Windows.");
            int pxW=GetDeviceCaps(dc,PhysicalWidth),pxH=GetDeviceCaps(dc,PhysicalHeight);
            int offX=GetDeviceCaps(dc,PhysicalOffsetX),offY=GetDeviceCaps(dc,PhysicalOffsetY);
            int nativeX=GetDeviceCaps(dc,DpiX),nativeY=GetDeviceCaps(dc,DpiY);
            if(pxW<=0||pxH<=0||nativeX<=0||nativeY<=0)
                throw new InvalidOperationException("Драйвер не сообщил размеры печатной страницы.");
            var expected=sheets[0].Paper;
            double driverW=pxW*25.4/nativeX,driverH=pxH*25.4/nativeY;
            if(Math.Abs(driverW-expected.WidthMm)>2.5 || Math.Abs(driverH-expected.HeightMm)>2.5)
                throw new InvalidOperationException($"Формат в драйвере {driverW:0.#}×{driverH:0.#} мм, нужен {expected.WidthMm:0.#}×{expected.HeightMm:0.#} мм. Исправьте свойства принтера.");
            var doc=new DocInfo{Size=Marshal.SizeOf<DocInfo>(),Name="KeePassXC Card Print"};
            if(duplex!=0 && !SupportsDuplex(printer))
                throw new InvalidOperationException("Этот принтер не поддерживает автоматическую двустороннюю печать.");
            if(StartDoc(dc,ref doc)<=0)throw new InvalidOperationException("Windows не приняла задание печати.");
            started=true;
            SetStretchBltMode(dc,ColorOnColor);
            foreach(var sheet in sheets)
            {
                token.ThrowIfCancellationRequested();
                if(StartPage(dc)<=0)throw new InvalidOperationException("Не удалось начать страницу.");
                int imageW=checked((int)Math.Ceiling(sheet.Paper.WidthMm*dpi/25.4));
                int imageH=checked((int)Math.Ceiling(sheet.Paper.HeightMm*dpi/25.4));
                double dx=sheet.Reverse?backX:frontX,dy=sheet.Reverse?backY:frontY;
                int offsetX=-offX+(int)Math.Round(dx*nativeX/25.4);
                int offsetY=-offY+(int)Math.Round(dy*nativeY/25.4);
                const int bandRows=384;
                for(int y=0;y<imageH;y+=bandRows)
                {
                    token.ThrowIfCancellationRequested();
                    int count=Math.Min(bandRows,imageH-y);
                    byte[] pixels=PrintDocumentPipeline.RenderSheetBandBgra(sheet,appearance,dpi,y,count,token);
                    try
                    {
                        var info=new BitmapInfo
                        {
                            Size=(uint)Marshal.SizeOf<BitmapInfo>(),Width=imageW,Height=-count,
                            Planes=1,BitCount=32,Compression=0,SizeImage=checked((uint)pixels.Length)
                        };
                        int left=offsetX;
                        int top=offsetY+(int)Math.Round((double)y*nativeY/dpi);
                        int bottom=offsetY+(int)Math.Round((double)(y+count)*nativeY/dpi);
                        int width=(int)Math.Round((double)imageW*nativeX/dpi);
                        if(StretchDIBits(dc,left,top,width,bottom-top,0,0,imageW,count,pixels,
                            ref info,DibRgbColors,Srccopy)<=0)
                            throw new InvalidOperationException("Драйвер не принял растровую полосу.");
                    }
                    finally{Array.Clear(pixels);}
                }
                if(EndPage(dc)<=0)throw new InvalidOperationException("Не удалось завершить страницу.");
            }
            token.ThrowIfCancellationRequested();
            if(EndDoc(dc)<=0)throw new InvalidOperationException("Не удалось завершить задание.");
            started=false;
        }
        finally
        {
            if(started && dc!=IntPtr.Zero)AbortDoc(dc);
            if(dc!=IntPtr.Zero)DeleteDC(dc);
            if(mode!=IntPtr.Zero)Marshal.FreeHGlobal(mode);
        }
    }
}
