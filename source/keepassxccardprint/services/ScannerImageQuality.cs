using SkiaSharp;

namespace KeePassXCCardPrint.Services;

// Relative measurements, not calibrated optical measurements. All samples stay in RAM.
internal sealed record ScannerImageQuality(int Width, int Height, double Light, int Contrast,
    double Detail, double EdgeStrength, double? Motion, double Dark, double Bright)
{
    // Prefer well-defined edges, useful contrast, stable frames and avoid clipped darkness.
    internal double Score => Detail * Math.Min(1, Contrast / 70d) * (1 - .7 * Dark)
        / (1 + (Motion ?? 0) / 18d);
}

internal static class ScannerQualityMeter
{
    internal const int SampleWidth = 48, SampleHeight = 32;

    internal static ScannerImageQuality Measure(SKBitmap image, Span<byte> thumbnail, ReadOnlySpan<byte> previous)
    {
        if (thumbnail.Length != SampleWidth * SampleHeight) throw new ArgumentException(nameof(thumbnail));
        int w = image.Width, h = image.Height;
        if(w<1||h<1)throw new ArgumentException(nameof(image));
        var pixels=image.Pixels;
        try
        {
            Span<int> hist = stackalloc int[256]; hist.Clear();
            int step = Math.Max(1, Math.Max(w,h)/400), count = 0;
            double sum = 0, lap = 0, edges = 0;
            static int Gray(SKColor c) => (c.Red*77+c.Green*150+c.Blue*29)>>8;
            for (int y=1;y<h-1;y+=step) for (int x=1;x<w-1;x+=step)
            {
                int c = Gray(pixels[y*w+x]);
                int l = Gray(pixels[y*w+x-1]), r = Gray(pixels[y*w+x+1]);
                int t = Gray(pixels[(y-1)*w+x]), b = Gray(pixels[(y+1)*w+x]);
                hist[c]++; sum+=c; count++;
                double v = 4*c-l-r-t-b;
                lap+=v*v; edges+=Math.Abs(l-r)+Math.Abs(t-b);
            }
            int lo=0, hi=255, tail=Math.Max(1,count/20), n=0;
            while(lo<255 && (n+=hist[lo])<tail)lo++;
            n=0; while(hi>0 && (n+=hist[hi])<tail)hi--;
            int dark=0,bright=0;
            for(int i=0;i<32;i++)dark+=hist[i];
            for(int i=245;i<256;i++)bright+=hist[i];
            // Area average reduces aliasing when measuring inter-frame changes.
            for(int y=0;y<SampleHeight;y++)for(int x=0;x<SampleWidth;x++)
            {
                int x0=x*w/SampleWidth, x1=Math.Max(x0+1,(x+1)*w/SampleWidth);
                int y0=y*h/SampleHeight, y1=Math.Max(y0+1,(y+1)*h/SampleHeight);
                int a=0,k=0;
                for(int j=0;j<3;j++)for(int i=0;i<3;i++)
                { a+=Gray(pixels[Math.Min(h-1,y0+(y1-y0)*j/3)*w+Math.Min(w-1,x0+(x1-x0)*i/3)]); k++; }
                thumbnail[y*SampleWidth+x]=(byte)(a/k);
            }
            double? motion=null;
            if(previous.Length==thumbnail.Length)
            {
                double shift=0,delta=0;
                for(int i=0;i<thumbnail.Length;i++)shift+=thumbnail[i]-previous[i];
                shift/=thumbnail.Length;
                for(int i=0;i<thumbnail.Length;i++)delta+=Math.Abs(thumbnail[i]-previous[i]-shift);
                motion=delta/thumbnail.Length;
            }
            return new(w,h,sum/Math.Max(1,count),Math.Max(0,hi-lo),Math.Sqrt(lap/Math.Max(1,count)),
                edges/Math.Max(1,count),motion,(double)dark/Math.Max(1,count),(double)bright/Math.Max(1,count));
        }
        finally { Array.Clear(pixels); }
    }
}
