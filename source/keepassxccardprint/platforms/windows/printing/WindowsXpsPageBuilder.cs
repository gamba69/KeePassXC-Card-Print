using KeePassXCCardPrint.Rendering;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Platforms.Windows.Printing;

/// <summary>Windows-specific XPS page construction; common layout knows only ICardCanvas.</summary>
internal static class WindowsXpsPageBuilder
{
    internal static XpsFixedPage BuildXpsPage(CardPrintSheet sheet,CardAppearance appearance,
        double frontX,double frontY,double backX,double backY,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // XPS/OPC image part names must be unique across physical pages.
        // A repeated KeePass icon on page two must not collide with page one.
        string resourcePrefix=$"/Resources/Pages/{sheet.SheetNumber:D6}{(sheet.Reverse ? "B" : "F")}/Images/";
        using var canvas=new XpsCardCanvas(sheet.Paper,resourcePrefix);
        double shiftX=sheet.Reverse?backX:frontX;
        double shiftY=sheet.Reverse?backY:frontY;
        if(!double.IsFinite(shiftX)||!double.IsFinite(shiftY) ||
           Math.Abs(shiftX)>15 || Math.Abs(shiftY)>15)
            throw new ArgumentOutOfRangeException(nameof(frontX),"Print calibration must be within +/- 15 mm.");
        canvas.Save();
        try
        {
            canvas.Translate((float)(shiftX*CardSkiaRenderer.PixelsPerMillimetre),(float)(shiftY*CardSkiaRenderer.PixelsPerMillimetre));
            PrintDocumentPipeline.DrawSheet(canvas,sheet,appearance,token);
        }
        finally {canvas.Restore();}
        byte[] markup=canvas.Complete();
        var images=canvas.Images.ToDictionary(kv=>kv.Key,kv=>(byte[])kv.Value.Clone(),StringComparer.Ordinal);
        var fonts=canvas.Fonts.ToDictionary(kv=>kv.Key,kv=>(byte[])kv.Value.Clone(),StringComparer.Ordinal);
        return new XpsFixedPage(sheet.Paper,markup,images,fonts,
            canvas.EmbeddedGlyphCount,canvas.OutlinedGlyphCount);
    }

}

/// <summary>Ephemeral in-memory XPS OM page input. Wiped after native submission.</summary>
internal sealed record XpsFixedPage(PaperGeometry Paper,byte[] Xml,
    IReadOnlyDictionary<string,byte[]> Images,IReadOnlyDictionary<string,byte[]> Fonts,
    int EmbeddedGlyphCount,int OutlinedGlyphCount) : IDisposable
{
    public void Dispose()
    {
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(Xml);
        foreach(var data in Images.Values)
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(data);
        foreach(var data in Fonts.Values)
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(data);
    }
}
