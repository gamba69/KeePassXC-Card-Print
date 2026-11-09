using KeePassXCCardPrint.Services;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeePassXCCardPrint.Rendering;

namespace KeePassXCCardPrint.Platforms.Windows.Printing;

/// <summary>
/// Native Microsoft XPS Object Model path. Unlike SkiaSharp.CreateXps(), pages
/// contain individually vectorized paths and text outlines. The native bridge
/// talks to the Windows Print Document Package target; it NEVER uses RAW spool
/// writes, and it does not create temporary documents with vault secrets.
/// </summary>
internal static partial class WindowsXpsPrinter
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeImage
    {
        public IntPtr Uri,Bytes;
        public uint ByteCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePage
    {
        public IntPtr Markup;
        public uint MarkupBytes;
        public double WidthMm,HeightMm;
        public uint ImageCount;
        public IntPtr Images;
        public uint FontCount;
        public IntPtr Fonts;
    }

    [LibraryImport("KpcXpsBridge.dll",EntryPoint="KpcPrintXps",StringMarshalling=StringMarshalling.Utf16)]
    private static partial int PrintNative(string printer,IntPtr driverMode,uint driverLength,
        int duplex,IntPtr pages,uint pageCount,out int mayHaveSubmitted);

    [LibraryImport("KpcXpsBridge.dll",EntryPoint="KpcValidateXps")]
    private static partial int ValidateNative(IntPtr pages,uint pageCount);

    /// <summary>
    /// Validate genuine XPS Object Model markup without opening a printer.
    /// Separate probes identify the FIRST incompatible feature of the page;
    /// no self-test failure is suppressed and no bitmap-page fallback is used.
    /// Everything rendered here is synthetic and contains no vault data.
    /// </summary>
    internal static void ValidateSyntheticPage()
    {
        if(!IsBridgeInstalled())
            throw new InvalidOperationException("Missing KpcXpsBridge.dll");

        // These independent probes make XPS_E_INVALID_MARKUP actionable:
        // an invalid gradient cannot conceal a basic FixedPage issue, and an
        // invalid glyph contour cannot conceal a missing image resource.
        CheckXpsScene("01 FixedPage + Canvas", static _ => { });
        CheckXpsScene("02 filled rectangle", static canvas =>
        {
            using var paper=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.White};
            canvas.DrawRect(0,0,1500,2100,paper);
        });
        CheckXpsScene("03 stroked rounded path", static canvas =>
        {
            using var pen=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black,
                Style=SkiaSharp.SKPaintStyle.Stroke,StrokeWidth=.8f};
            canvas.DrawRoundRect(new SkiaSharp.SKRect(25,25,280,380),12,12,pen);
        });
        CheckXpsScene("04 radial gradient", static canvas =>
        {
            canvas.DrawRadialGradientRect(new SkiaSharp.SKRect(30,30,220,180),
                new SkiaSharp.SKPoint(100,100),80,
                [SkiaSharp.SKColors.LightGray,SkiaSharp.SKColors.White],[0f,1f]);
        });
        CheckXpsScene("05a converted Skia contour grammar", static canvas =>
        {
            using var geometryBuilder=new SkiaSharp.SKPathBuilder();
            geometryBuilder.MoveTo(10,20);
            geometryBuilder.QuadTo(30,5,50,20);
            geometryBuilder.CubicTo(60,30,50,45,10,50);
            geometryBuilder.Close();
            using var geometry=geometryBuilder.Detach();
            using var ink=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black};
            canvas.DrawPath(geometry,ink);
        });
        CheckXpsScene("05 positioned Latin text", static canvas =>
        {
            using var latin=ShapedTextEngine.CreateLayout("0O 1l I| /\\ TEST",17f,
                "Consolas","400",markZero:true);
            using var ink=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black};
            canvas.DrawGlyphRuns(latin,40,260,ink);
        });
        CheckXpsScene("06 positioned Cyrillic text", static canvas =>
        {
            using var text=ShapedTextEngine.CreateLayout("Кириллица / HarfBuzz",17f,
                "Segoe UI","400");
            using var ink=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black};
            canvas.DrawGlyphRuns(text,40,300,ink);
        });
        CheckXpsScene("07 PNG resource and ImageBrush", static canvas =>
        {
            using var bitmap=new SkiaSharp.SKBitmap(16,16);
            bitmap.Erase(SkiaSharp.SKColors.Black);
            canvas.DrawBitmap(bitmap,new SkiaSharp.SKRect(330,70,400,140),
                new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell));
        });
        CheckXpsScene("08 composite page", static canvas =>
        {
            using(var paper=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.White})
                canvas.DrawRect(0,0,1500,2100,paper);
            using(var pen=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black,
                Style=SkiaSharp.SKPaintStyle.Stroke,StrokeWidth=.8f})
                canvas.DrawRoundRect(new SkiaSharp.SKRect(25,25,280,380),12,12,pen);
            canvas.DrawRadialGradientRect(new SkiaSharp.SKRect(30,30,220,180),
                new SkiaSharp.SKPoint(100,100),80,
                [SkiaSharp.SKColors.LightGray,SkiaSharp.SKColors.White],[0f,1f]);
            using(var latin=ShapedTextEngine.CreateLayout("0O 1l I| /\\ TEST",17f,
                      "Consolas","400",markZero:true))
            using(var cyrillic=ShapedTextEngine.CreateLayout("Кириллица / HarfBuzz",17f,
                      "Segoe UI","400"))
            using(var ink=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black})
            {
                canvas.DrawGlyphRuns(latin,40,260,ink);
                canvas.DrawGlyphRuns(cyrillic,40,300,ink);
            }
            using(var bitmap=new SkiaSharp.SKBitmap(16,16))
            {
                bitmap.Erase(SkiaSharp.SKColors.Black);
                canvas.DrawBitmap(bitmap,new SkiaSharp.SKRect(330,70,400,140),
                    new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell));
            }
        });
        CheckXpsScene("09 native Glyphs + embedded system font",static canvas =>
        {
            using var layout=ShapedTextEngine.CreateLayout("0O 1l XPS",17f,
                "Segoe UI","400");
            using var ink=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black};
            canvas.DrawGlyphRuns(layout,40,360,ink);
        });
        CheckXpsScene("10 static variable-font instance + native Glyphs",static canvas =>
        {
            using var layout=ShapedTextEngine.CreateLayout("0O wght 600",17f,
                "Atkinson Hyperlegible Mono","600");
            using var ink=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black};
            canvas.DrawGlyphRuns(layout,40,410,ink);
        },requireEmbeddedGlyphs:true);
    }

    private static void CheckXpsScene(string stage,Action<XpsCardCanvas> draw,
        bool requireEmbeddedGlyphs=false)
    {
        using var canvas=new XpsCardCanvas(PaperGeometry.Resolve("A4"));
        try
        {
            draw(canvas);
        }
        catch(Exception e) when (e is not OutOfMemoryException)
        {
            throw new InvalidOperationException($"XPS self-test stage {stage}: rendering failed: {e.Message}",e);
        }
        if(requireEmbeddedGlyphs && canvas.EmbeddedGlyphCount==0)
            throw new InvalidOperationException($"XPS self-test stage {stage}: " +
                "no native Glyphs created: "+XpsFontEmbedding.LastFailure);
        byte[] xml=canvas.Complete();
        var pins=new List<GCHandle>();
        var allocations=new List<IntPtr>();
        try
        {
            // XmlReader checks well-formedness. Windows XPS OM then validates
            // the real XPS vocabulary and geometry, which XmlReader cannot.
            using(var stream=new MemoryStream(xml,writable:false))
            using(var reader=System.Xml.XmlReader.Create(stream,
                new System.Xml.XmlReaderSettings
                {
                    DtdProcessing=System.Xml.DtdProcessing.Prohibit,
                    XmlResolver=null
                }))
            {
                while(reader.Read()) { }
            }
            static IntPtr Pin(byte[] bytes,List<GCHandle> handles)
            {
                var handle=GCHandle.Alloc(bytes,GCHandleType.Pinned);
                handles.Add(handle);
                return handle.AddrOfPinnedObject();
            }
            var imageList=new List<NativeImage>();
            foreach(var (uri,data) in canvas.Images)
            {
                IntPtr uriPointer=Marshal.StringToHGlobalUni(uri);
                allocations.Add(uriPointer);
                imageList.Add(new NativeImage{Uri=uriPointer,Bytes=Pin(data,pins),
                    ByteCount=checked((uint)data.Length)});
            }
            int resourceSize=Marshal.SizeOf<NativeImage>();
            IntPtr imagePointer=IntPtr.Zero;
            if(imageList.Count>0)
            {
                imagePointer=Marshal.AllocHGlobal(checked(imageList.Count*resourceSize));
                allocations.Add(imagePointer);
                for(int i=0;i<imageList.Count;i++)
                    Marshal.StructureToPtr(imageList[i],
                        IntPtr.Add(imagePointer,i*resourceSize),false);
            }
            var fontList=new List<NativeImage>();
            foreach(var (uri,data) in canvas.Fonts)
            {
                IntPtr uriPointer=Marshal.StringToHGlobalUni(uri);
                allocations.Add(uriPointer);
                fontList.Add(new NativeImage{Uri=uriPointer,Bytes=Pin(data,pins),
                    ByteCount=checked((uint)data.Length)});
            }
            IntPtr fontPointer=IntPtr.Zero;
            if(fontList.Count>0)
            {
                fontPointer=Marshal.AllocHGlobal(checked(fontList.Count*resourceSize));
                allocations.Add(fontPointer);
                for(int i=0;i<fontList.Count;i++)
                    Marshal.StructureToPtr(fontList[i],
                        IntPtr.Add(fontPointer,i*resourceSize),false);
            }
            NativePage page=new()
            {
                Markup=Pin(xml,pins),MarkupBytes=checked((uint)xml.Length),
                WidthMm=210,HeightMm=297,
                ImageCount=checked((uint)imageList.Count),Images=imagePointer,
                FontCount=checked((uint)fontList.Count),Fonts=fontPointer
            };
            IntPtr buffer=Marshal.AllocHGlobal(Marshal.SizeOf<NativePage>());
            allocations.Add(buffer);
            Marshal.StructureToPtr(page,buffer,false);
            int hr=ValidateNative(buffer,1);
            if(hr<0)
                throw new InvalidOperationException(
                    $"XPS OM parser rejected stage {stage}: 0x{hr:X8} " +
                    "(the synthetic FixedPage markup or its resources are invalid)");
        }
        catch(Exception e) when(e is not OutOfMemoryException &&
            !e.Message.Contains($"stage {stage}",StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"XPS self-test stage {stage}: {e.Message}",e);
        }
        finally
        {
            foreach(var pin in pins)if(pin.IsAllocated)pin.Free();
            foreach(var ptr in allocations)Marshal.FreeHGlobal(ptr);
            CryptographicOperations.ZeroMemory(xml);
        }
    }

    internal static bool IsBridgeInstalled()
    {
        if(!OperatingSystem.IsWindows())return false;
        string path=Path.Combine(AppContext.BaseDirectory,"KpcXpsBridge.dll");
        if(!File.Exists(path) || !NativeLibrary.TryLoad(path,out var handle))return false;
        NativeLibrary.Free(handle);
        return true;
    }

    /// <param name="duplex">0=simplex; 1=long-edge; 2=short-edge</param>
    internal static void Print(string printer,byte[] driverMode,IReadOnlyList<CardPrintSheet> sheets,
        CardAppearance appearance,double frontX,double frontY,double backX,double backY,
        int duplex,CancellationToken cancellationToken)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        if(!IsBridgeInstalled())throw new XpsPrintException("Нативный модуль XPS не установлен.",false);
        if(sheets.Count==0 || driverMode.Length<96 || string.IsNullOrWhiteSpace(printer))
            throw new ArgumentException("Не выбран принтер или печатаемые страницы.");
        if(sheets.Any(p=>p.Paper!=sheets[0].Paper))
            throw new InvalidOperationException("Все страницы одного задания должны иметь одинаковый формат.");

        byte[] mode=(byte[])driverMode.Clone();
        // Apply the requested physical sheet geometry to the job-specific
        // DEVMODE, never to Windows printer defaults.
        uint fields=BitConverter.ToUInt32(mode,72);
        fields|=0x00000001u; // DM_ORIENTATION
        bool landscape=sheets[0].Paper.WidthMm>sheets[0].Paper.HeightMm;
        BitConverter.GetBytes((short)(landscape?2:1)).CopyTo(mode,76);
        int paperCode=sheets[0].Paper.Format switch
        {"A3"=>8,"A4"=>9,"A5"=>11,"A6"=>70,"Letter"=>1,"Legal"=>5,_=>0};
        if(paperCode!=0)
        {
            fields|=0x00000002u; // DM_PAPERSIZE
            BitConverter.GetBytes((short)paperCode).CopyTo(mode,78);
        }
        else
        {
            // Driver uses tenths of millimetres for custom paper.
            fields &= ~0x00000002u; // clear DM_PAPERSIZE so dimensions take precedence
            fields|=0x00000004u|0x00000008u; // DM_PAPERLENGTH | DM_PAPERWIDTH
            BitConverter.GetBytes((short)Math.Round(sheets[0].Paper.HeightMm*10)).CopyTo(mode,80);
            BitConverter.GetBytes((short)Math.Round(sheets[0].Paper.WidthMm*10)).CopyTo(mode,82);
        }
        BitConverter.GetBytes(fields).CopyTo(mode,72);

        var pinned=new List<GCHandle>();
        var unmanaged=new List<IntPtr>();
        var pages=new List<XpsFixedPage>();
        try
        {
            static IntPtr Alloc(int size,List<IntPtr> allocations)
            {
                IntPtr ptr=Marshal.AllocHGlobal(size);
                allocations.Add(ptr);
                return ptr;
            }
            static IntPtr Pin(byte[] data,List<GCHandle> handles)
            {
                var handle=GCHandle.Alloc(data,GCHandleType.Pinned);
                handles.Add(handle);
                return handle.AddrOfPinnedObject();
            }
            IntPtr pMode=Pin(mode,pinned);
            var nativePages=new NativePage[sheets.Count];
            for(int i=0;i<sheets.Count;i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                XpsFixedPage page;
                try
                {
                    page=WindowsXpsPageBuilder.BuildXpsPage(sheets[i],appearance,
                        frontX,frontY,backX,backY,cancellationToken);
                }
                catch(Exception error) when(error is not OperationCanceledException and not OutOfMemoryException)
                {
                    // Failure while building the page cannot have reached Windows.
                    throw new XpsPrintException("Не удалось подготовить векторную страницу XPS.",false,error);
                }
                pages.Add(page);
                var resources=new NativeImage[page.Images.Count];
                int j=0;
                foreach(var image in page.Images)
                {
                    var uri=Marshal.StringToHGlobalUni(image.Key);
                    unmanaged.Add(uri);
                    resources[j++]=new NativeImage
                    {
                        Uri=uri,Bytes=Pin(image.Value,pinned),ByteCount=checked((uint)image.Value.Length)
                    };
                }
                int imageSize=Marshal.SizeOf<NativeImage>();
                var imagesPtr=resources.Length==0?IntPtr.Zero:Alloc(checked(resources.Length*imageSize),unmanaged);
                for(j=0;j<resources.Length;j++)
                    Marshal.StructureToPtr(resources[j],IntPtr.Add(imagesPtr,j*imageSize),false);
                var fontResources=new NativeImage[page.Fonts.Count];
                j=0;
                foreach(var font in page.Fonts)
                {
                    var uri=Marshal.StringToHGlobalUni(font.Key);
                    unmanaged.Add(uri);
                    fontResources[j++]=new NativeImage
                    {
                        Uri=uri,Bytes=Pin(font.Value,pinned),ByteCount=checked((uint)font.Value.Length)
                    };
                }
                var fontPtr=fontResources.Length==0?IntPtr.Zero:
                    Alloc(checked(fontResources.Length*imageSize),unmanaged);
                for(j=0;j<fontResources.Length;j++)
                    Marshal.StructureToPtr(fontResources[j],IntPtr.Add(fontPtr,j*imageSize),false);
                nativePages[i]=new NativePage
                {
                    Markup=Pin(page.Xml,pinned),MarkupBytes=checked((uint)page.Xml.Length),
                    WidthMm=page.Paper.WidthMm,HeightMm=page.Paper.HeightMm,
                    ImageCount=checked((uint)resources.Length),Images=imagesPtr,
                    FontCount=checked((uint)fontResources.Length),Fonts=fontPtr
                };
            }
            cancellationToken.ThrowIfCancellationRequested();
            int pageSize=Marshal.SizeOf<NativePage>();
            var pagesPtr=Alloc(checked(pageSize*nativePages.Length),unmanaged);
            for(int i=0;i<nativePages.Length;i++)
                Marshal.StructureToPtr(nativePages[i],IntPtr.Add(pagesPtr,i*pageSize),false);
            int submitted=0;
            int hr;
            try
            {
                hr=PrintNative(printer,pMode,checked((uint)mode.Length),duplex,pagesPtr,
                    checked((uint)nativePages.Length),out submitted);
            }
            catch(DllNotFoundException e){throw new XpsPrintException("Не загружена библиотека XPS.",false,e);}
            catch(EntryPointNotFoundException e){throw new XpsPrintException("Несовместимая версия модуля XPS.",false,e);}
            if(hr<0)
                throw new XpsPrintException($"Windows XPS OM: HRESULT 0x{hr:X8}. " +
                    "Проверьте принтер, драйвер, свойства задания и доступность XPS.",submitted!=0);
        }
        finally
        {
            // Pinned memory must not be overwritten while the native call runs.
            for(int i=pinned.Count-1;i>=0;i--)if(pinned[i].IsAllocated)pinned[i].Free();
            foreach(var ptr in unmanaged)Marshal.FreeHGlobal(ptr);
            foreach(var p in pages)p.Dispose();
            CryptographicOperations.ZeroMemory(mode);
        }
    }
}

internal sealed class XpsPrintException(string message,bool mayHaveSubmitted,Exception? inner=null)
    : Exception(message,inner)
{
    /// <summary>If true the job can have reached the spooler: NEVER retry automatically.</summary>
    public bool MayHaveSubmitted { get; }=mayHaveSubmitted;
}
