using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SkiaSharp;

namespace KeePassXCCardPrint.Services;

// PDFium is called exclusively through its stable C API. No reflection, COM RCWs,
// external conversion process, JavaScript execution, or temporary rendered files.
internal sealed class ScannerPdf : IDisposable
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private GCHandle _pin;
    private byte[]? _bytes;
    private nint _document;
    internal int Count { get; }

    internal ScannerPdf(byte[] data)
    {
        _bytes = data;
        try
        {
            lock (Gate)
            {
                if (!_initialized) { FPDF_InitLibrary(); _initialized = true; }
                _pin = GCHandle.Alloc(data, GCHandleType.Pinned);
                _document = FPDF_LoadMemDocument64(_pin.AddrOfPinnedObject(), (nuint)data.Length, 0);
                if (_document == 0) throw new FormatException();
                Count = FPDF_GetPageCount(_document);
                if (Count <= 0) throw new FormatException();
            }
        }
        catch { Dispose(); throw; }
    }

    internal SKBitmap Render(int index, bool highQuality = false)
    {
        lock (Gate)
        {
            if (_document == 0 || index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            nint page = FPDF_LoadPage(_document, index);
            if (page == 0) throw new FormatException();
            try
            {
                double w = FPDF_GetPageWidth(page), h = FPDF_GetPageHeight(page);
                if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0) throw new FormatException();
                double scale = Math.Min((highQuality ? 450.0 : 300.0)/72, 5500 / Math.Max(w,h));
                int width = Math.Max(1,(int)Math.Ceiling(w*scale)), height = Math.Max(1,(int)Math.Ceiling(h*scale));
                var result = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                try
                {
                    nint bitmap = FPDFBitmap_CreateEx(width, height, 4, result.GetPixels(), result.RowBytes);
                    if (bitmap == 0) throw new InvalidOperationException();
                    try { FPDFBitmap_FillRect(bitmap,0,0,width,height,0xffffffff); FPDF_RenderPageBitmap(bitmap,page,0,0,width,height,0,0); }
                    finally { FPDFBitmap_Destroy(bitmap); }
                    return result;
                }
                catch { result.Dispose(); throw; }
            }
            finally { FPDF_ClosePage(page); }
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_document != 0) { FPDF_CloseDocument(_document); _document = 0; }
            if (_pin.IsAllocated) _pin.Free();
            if (_bytes is not null) { CryptographicOperations.ZeroMemory(_bytes); _bytes = null; }
        }
    }
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_InitLibrary();
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern nint FPDF_LoadMemDocument64(nint data, nuint size, nint password);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern int FPDF_GetPageCount(nint doc);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern nint FPDF_LoadPage(nint doc, int index);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern double FPDF_GetPageWidth(nint page);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern double FPDF_GetPageHeight(nint page);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern nint FPDFBitmap_CreateEx(int width, int height, int format, nint pixels, int stride);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDFBitmap_FillRect(nint bmp, int left, int top, int width, int height, uint color);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_RenderPageBitmap(nint bmp,nint page,int x,int y,int w,int h,int rotate,int flags);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDFBitmap_Destroy(nint bmp);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_ClosePage(nint page);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_CloseDocument(nint doc);
}

internal static class ScannerNative
{
    [DllImport("user32.dll")] private static extern int MessageBeep(uint type);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern int OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern int EmptyClipboard();
    [DllImport("user32.dll")] private static extern int CloseClipboard();
    internal static void Beep() => _ = MessageBeep(0);
    // Recheck the sequence while holding the Win32 clipboard lock, so another
    // application cannot replace its contents between the check and clearing.
    internal static bool TryClearClipboard(uint sequence)
    {
        if (sequence == 0 || GetClipboardSequenceNumber() != sequence || OpenClipboard(0) == 0) return false;
        try { return GetClipboardSequenceNumber() == sequence && EmptyClipboard() != 0; }
        finally { _ = CloseClipboard(); }
    }
}
