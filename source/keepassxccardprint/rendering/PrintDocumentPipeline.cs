using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Rendering;

/// <summary>Physical paper dimensions and exact, unscaled card placement in millimetres.
/// One placement plan is reused by preview, PDF and 600-DPI raster export.</summary>
public sealed record PaperGeometry(string Format, double WidthMm, double HeightMm)
{
    public static PaperGeometry Resolve(string format, bool landscape = false,
        double customWidthMm = 210, double customHeightMm = 297)
    {
        var (w,h) = format switch
        {
            "A3" => (297d,420d), "A4" => (210d,297d),
            "A5" => (148d,210d), "A6" => (105d,148d),
            "Letter" => (215.9d,279.4d), "Legal" => (215.9d,355.6d),
            "Custom" => (customWidthMm,customHeightMm),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        if (!double.IsFinite(w) || !double.IsFinite(h) || w < 50 || h < 50 || w > 420 || h > 594)
            throw new ArgumentOutOfRangeException(nameof(format),"Invalid physical sheet dimensions.");
        return landscape ? new PaperGeometry(format,h,w) : new PaperGeometry(format,w,h);
    }
}

public sealed record CardSheetSlot(int CardIndex, PhysicalCard Card, double Xmm, double Ymm);
public sealed record CardPrintSheet(PaperGeometry Paper, int SheetNumber, bool Reverse,
    IReadOnlyList<CardSheetSlot> Slots);

public static class PrintDocumentPipeline
{
    private const float DesignPixelsPerMm=CardSkiaRenderer.PixelsPerMillimetre;
    private const float PdfPointsPerMm=72f/25.4f;

    public static IReadOnlyList<CardPrintSheet> Plan(IReadOnlyList<PhysicalCard> cards,
        CardAppearance appearance, PaperGeometry paper, bool shortEdgeFlip=false,
        double marginMm=5, int firstSheetSlot=0)
    {
        if (cards.Count==0) return [];
        if(!double.IsFinite(marginMm)||marginMm<0||marginMm>20)
            throw new ArgumentOutOfRangeException(nameof(marginMm));
        double cw=appearance.WidthMm,ch=appearance.HeightMm;
        if(!double.IsFinite(cw)||!double.IsFinite(ch)||cw<=0||ch<=0)
            throw new InvalidOperationException("Invalid card geometry.");
        int columns=(int)Math.Floor((paper.WidthMm-2*marginMm+1e-7)/cw);
        int rows=(int)Math.Floor((paper.HeightMm-2*marginMm+1e-7)/ch);
        if(columns<1 || rows<1)
            throw new InvalidOperationException("Карточка не помещается на выбранном листе без изменения масштаба.");
        int capacity=checked(rows*columns);
        if(firstSheetSlot<0 || firstSheetSlot>=capacity)
            throw new InvalidOperationException("Начальная позиция выходит за пределы листа.");
        int sheetCount=checked((cards.Count+firstSheetSlot+capacity-1)/capacity);
        if(sheetCount>4096) throw new InvalidOperationException("Слишком много печатных листов.");
        var result=new List<CardPrintSheet>(sheetCount*(appearance.DuplexEnabled?2:1));
        // Cards touch edge-to-edge; only the *entire* grid is centered on paper.
        // Even a sparsely populated final sheet retains the complete grid origin,
        // which keeps physical pockets stable for partial reprints.
        double originX=(paper.WidthMm-columns*cw)/2;
        double originY=(paper.HeightMm-rows*ch)/2;
        for(int sheet=0;sheet<sheetCount;sheet++)
        {
            var front=new List<CardSheetSlot>();
            var back=new List<CardSheetSlot>();
            for(int slot=0;slot<capacity;slot++)
            {
                int cardIndex=sheet*capacity+slot-firstSheetSlot;
                if(cardIndex<0)continue;
                if(cardIndex>=cards.Count)break;
                int column=slot%columns,row=slot/columns;
                double x=originX+column*cw,y=originY+row*ch;
                front.Add(new CardSheetSlot(cardIndex,cards[cardIndex],x,y));
                if(appearance.DuplexEnabled)
                {
                    // Backside mirror depends on the actual duplex binding edge.
                    double bx=shortEdgeFlip?x:paper.WidthMm-x-cw;
                    double by=shortEdgeFlip?paper.HeightMm-y-ch:y;
                    back.Add(new CardSheetSlot(cardIndex,cards[cardIndex],bx,by));
                }
            }
            result.Add(new CardPrintSheet(paper,sheet+1,false,front));
            if(appearance.DuplexEnabled)
                result.Add(new CardPrintSheet(paper,sheet+1,true,back));
        }
        return result;
    }

    /// <summary>Same geometry and drawing calls as on-screen preview, without rasterizing
    /// text or vector logos before they reach Skia's PDF backend.</summary>
    private static void DrawSheet(SKCanvas canvas,CardPrintSheet sheet,CardAppearance appearance,
        CancellationToken cancellationToken)
        => DrawSheet(new SkiaCardCanvas(canvas),sheet,appearance,cancellationToken);

    internal static void DrawSheet(ICardCanvas canvas,CardPrintSheet sheet,CardAppearance appearance,
        CancellationToken cancellationToken)
    {
        float designW=(float)(sheet.Paper.WidthMm*DesignPixelsPerMm);
        float designH=(float)(sheet.Paper.HeightMm*DesignPixelsPerMm);
        using(var paper=new SKPaint{Color=SKColors.White,Style=SKPaintStyle.Fill})
            canvas.DrawRect(0,0,designW,designH,paper);
        if(sheet.Slots.Count==0)
        {
            // No real vault fields are ever used on the calibration page.
            using var grid=new SKPaint{Color=new SKColor(170,170,170),StrokeWidth=DesignPixelsPerMm*.12f,
                Style=SKPaintStyle.Stroke,IsAntialias=true};
            using var marker=new SKPaint{Color=SKColors.Black,StrokeWidth=DesignPixelsPerMm*.25f,
                Style=SKPaintStyle.Stroke,IsAntialias=true};
            float x1=10*DesignPixelsPerMm,y1=10*DesignPixelsPerMm;
            float x2=designW-x1,y2=designH-y1;
            canvas.DrawRect(x1,y1,x2-x1,y2-y1,grid);
            for(int n=20;n<sheet.Paper.WidthMm-10;n+=10)
            {
                float x=n*DesignPixelsPerMm;
                canvas.DrawLine(x,y1,x,y2,grid);
            }
            for(int n=20;n<sheet.Paper.HeightMm-10;n+=10)
            {
                float y=n*DesignPixelsPerMm;
                canvas.DrawLine(x1,y,x2,y,grid);
            }
            foreach(var x in new[]{x1,x2})foreach(var y in new[]{y1,y2})
            {
                canvas.DrawLine(x-4*DesignPixelsPerMm,y,x+4*DesignPixelsPerMm,y,marker);
                canvas.DrawLine(x,y-4*DesignPixelsPerMm,x,y+4*DesignPixelsPerMm,marker);
            }
            // Distinguish front and reverse without using a font or sensitive content.
            if(sheet.Reverse)
            {
                canvas.DrawCircle(designW/2,designH/2,6*DesignPixelsPerMm,marker);
                canvas.DrawCircle(designW/2,designH/2,3*DesignPixelsPerMm,marker);
            }
            else canvas.DrawRect(designW/2-6*DesignPixelsPerMm,designH/2-6*DesignPixelsPerMm,
                12*DesignPixelsPerMm,12*DesignPixelsPerMm,marker);
            return;
        }
        if(canvas is IDirectVectorCardCanvas)
        {
            // A direct XPS OM page contains the card's actual vector primitives.
            // No SKPicture / SVG / raster conversion is involved in this path.
            foreach(var slot in sheet.Slots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                canvas.Save();
                try
                {
                    var rect=new SKRect((float)(slot.Xmm*DesignPixelsPerMm),
                        (float)(slot.Ymm*DesignPixelsPerMm),
                        (float)((slot.Xmm+appearance.WidthMm)*DesignPixelsPerMm),
                        (float)((slot.Ymm+appearance.HeightMm)*DesignPixelsPerMm));
                    canvas.ClipRect(rect,SKClipOperation.Intersect,false);
                    canvas.Translate(rect.Left,rect.Top);
                    CardSkiaRenderer.Draw(canvas,slot.Card,appearance,sheet.Reverse);
                }
                finally { canvas.Restore(); }
            }
        }
        else
        {
        // Each card records its own Skia display list concurrently. Final
        // compositing remains sequential: SKCanvas is never shared by workers.
        var pictures=new SKPicture?[sheet.Slots.Count];
        try
        {
            var parallel=new ParallelOptions
            {
                CancellationToken=cancellationToken,
                MaxDegreeOfParallelism=Math.Clamp(Environment.ProcessorCount-1,1,8)
            };
            Parallel.For(0,sheet.Slots.Count,parallel,i =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var recorder=new SKPictureRecorder();
                var drawing=recorder.BeginRecording(new SKRect(0,0,
                    (float)(appearance.WidthMm*DesignPixelsPerMm),
                    (float)(appearance.HeightMm*DesignPixelsPerMm)));
                CardSkiaRenderer.Draw(drawing,sheet.Slots[i].Card,appearance,sheet.Reverse);
                pictures[i]=recorder.EndRecording();
            });
            for(int i=0;i<sheet.Slots.Count;i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slot=sheet.Slots[i];
                canvas.Save();
                try
                {
                    var rect=new SKRect((float)(slot.Xmm*DesignPixelsPerMm),
                        (float)(slot.Ymm*DesignPixelsPerMm),
                        (float)((slot.Xmm+appearance.WidthMm)*DesignPixelsPerMm),
                        (float)((slot.Ymm+appearance.HeightMm)*DesignPixelsPerMm));
                    // A hard clip avoids fractional-pixel antialias seams between
                    // physically adjacent cards on raster sheet previews.
                    canvas.ClipRect(rect,SKClipOperation.Intersect,false);
                    canvas.Translate(rect.Left,rect.Top);
                    canvas.DrawPicture(pictures[i] ?? throw new InvalidOperationException("Card picture missing."));
                }
                finally { canvas.Restore(); }
            }
        }
        finally
        {
            foreach(var picture in pictures)picture?.Dispose();
        }
        }
        // Small crop crosses use the same physical coordinates on PDF and PNG.
        using var marks=new SKPaint
        {
            Color=new SKColor(105,105,105),IsAntialias=true,
            Style=SKPaintStyle.Stroke,StrokeWidth=DesignPixelsPerMm*.12f
        };
        // Shared card corners are a single cut intersection, not 2-4
        // overprinted crosses. Round coordinates to micrometres to avoid
        // floating-point inequality between neighbouring card boundaries.
        var intersections=new HashSet<(long X, long Y)>();
        foreach(var slot in sheet.Slots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach(var mmX in new[]{slot.Xmm,slot.Xmm+appearance.WidthMm})
            foreach(var mmY in new[]{slot.Ymm,slot.Ymm+appearance.HeightMm})
                intersections.Add(((long)Math.Round(mmX*1000),(long)Math.Round(mmY*1000)));
        }
        float armLength=DesignPixelsPerMm*1.1f;
        foreach(var intersection in intersections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float x=(float)(intersection.X/1000d*DesignPixelsPerMm);
            float y=(float)(intersection.Y/1000d*DesignPixelsPerMm);
            canvas.DrawLine(x-armLength,y,x+armLength,y,marks);
            canvas.DrawLine(x,y-armLength,x,y+armLength,marks);
        }
    }

    /// <summary>Record one entire sheet using the SAME vector drawing method
    /// used for PDF export. Screen presentation never needs a PNG.</summary>
    internal static SkiaPreviewScene RecordSheet(CardPrintSheet sheet,CardAppearance appearance,
        CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        float width=(float)(sheet.Paper.WidthMm*DesignPixelsPerMm);
        float height=(float)(sheet.Paper.HeightMm*DesignPixelsPerMm);
        using var recorder=new SKPictureRecorder();
        var canvas=recorder.BeginRecording(new SKRect(0,0,width,height));
        DrawSheet(canvas,sheet,appearance,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new SkiaPreviewScene(recorder.EndRecording(),width,height);
    }

    /// <summary>Render one bounded BGRA stripe (top-down DIB) directly from the common
    /// drawing pipeline. 1200 DPI A4 never needs a 500 MB intermediate bitmap.</summary>
    public static byte[] RenderSheetBandBgra(CardPrintSheet sheet, CardAppearance appearance,
        int dpi, int startRow, int rows, CancellationToken cancellationToken=default)
    {
        if(dpi is not (300 or 600 or 1200))throw new ArgumentOutOfRangeException(nameof(dpi));
        int width=checked((int)Math.Ceiling(sheet.Paper.WidthMm*dpi/25.4));
        int height=checked((int)Math.Ceiling(sheet.Paper.HeightMm*dpi/25.4));
        if(startRow<0 || rows<1 || rows>512 || startRow+rows>height)
            throw new ArgumentOutOfRangeException(nameof(rows));
        cancellationToken.ThrowIfCancellationRequested();
        using var surface=SKSurface.Create(new SKImageInfo(width,rows,SKColorType.Bgra8888,SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Cannot allocate print stripe.");
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Translate(0,-startRow);
        surface.Canvas.Scale(dpi/25.4f/DesignPixelsPerMm);
        DrawSheet(surface.Canvas,sheet,appearance,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap=surface.Snapshot();
        byte[] pixels=new byte[checked(width*rows*4)];
        var info=new SKImageInfo(width,rows,SKColorType.Bgra8888,SKAlphaType.Premul);
        var pinned=System.Runtime.InteropServices.GCHandle.Alloc(pixels,
            System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            if(!bitmap.ReadPixels(info,pinned.AddrOfPinnedObject(),checked(width*4),0,0))
                throw new InvalidOperationException("Cannot read raster stripe.");
        }
        finally{pinned.Free();}
        return pixels;
    }

    public static byte[] RenderSheetPng(CardPrintSheet sheet,CardAppearance appearance,
        float dpi=150,CancellationToken cancellationToken=default)
    {
        if(!float.IsFinite(dpi)||dpi<48||dpi>600)
            throw new ArgumentOutOfRangeException(nameof(dpi));
        cancellationToken.ThrowIfCancellationRequested();
        int w=checked((int)Math.Ceiling(sheet.Paper.WidthMm*dpi/25.4));
        int h=checked((int)Math.Ceiling(sheet.Paper.HeightMm*dpi/25.4));
        if((long)w*h>85_000_000)throw new InvalidOperationException("Raster sheet exceeds memory budget.");
        using var surface=SKSurface.Create(new SKImageInfo(w,h,SKColorType.Rgba8888,SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Cannot allocate sheet raster buffer.");
        surface.Canvas.Scale(dpi/25.4f/DesignPixelsPerMm);
        DrawSheet(surface.Canvas,sheet,appearance,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var image=surface.Snapshot();
        using var bytes=image.Encode(SKEncodedImageFormat.Png,100)
            ?? throw new InvalidOperationException("Cannot encode sheet PNG.");
        return bytes.ToArray();
    }

    public static byte[] ExportVectorPdf(IReadOnlyList<CardPrintSheet> sheets,
        CardAppearance appearance,CancellationToken cancellationToken=default)
    {
        if(sheets.Count==0) throw new InvalidOperationException("Нет выбранных карточек.");
        using var output=new MemoryStream();
        using(var document=SKDocument.CreatePdf(output)
            ?? throw new InvalidOperationException("Не удалось создать PDF."))
        {
            foreach(var sheet in sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var canvas=document.BeginPage((float)(sheet.Paper.WidthMm*PdfPointsPerMm),
                    (float)(sheet.Paper.HeightMm*PdfPointsPerMm));
                canvas.Scale(PdfPointsPerMm/DesignPixelsPerMm);
                DrawSheet(canvas,sheet,appearance,cancellationToken);
                document.EndPage();
            }
            cancellationToken.ThrowIfCancellationRequested();
            document.Close();
        }
        var pdf=output.ToArray();
        Array.Clear(output.GetBuffer(),0,(int)output.Length);
        return pdf;
    }

    /// <summary>Every physical page is exported as its own 600-DPI PNG in a ZIP;
    /// only the explicitly chosen destination is written to permanent storage.</summary>
    public static byte[] ExportRasterZip(IReadOnlyList<CardPrintSheet> sheets,
        CardAppearance appearance,CancellationToken cancellationToken=default)
    {
        if(sheets.Count==0) throw new InvalidOperationException("Нет выбранных карточек.");
        using var output=new MemoryStream();
        using(var archive=new ZipArchive(output,ZipArchiveMode.Create,leaveOpen:true))
        {
            foreach(var sheet in sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] png=RenderSheetPng(sheet,appearance,600,cancellationToken);
                try
                {
                    var entry=archive.CreateEntry($"sheet-{sheet.SheetNumber:0000}-{(sheet.Reverse?"back":"front")}.png",
                        CompressionLevel.NoCompression);
                    using var stream=entry.Open();
                    stream.Write(png);
                }
                finally { Array.Clear(png); }
            }
        }
        var zip=output.ToArray();
        Array.Clear(output.GetBuffer(),0,(int)output.Length);
        return zip;
    }
}

