using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode;
using ZXing.QrCode.Internal;

namespace KeePassXCCardPrint.Services;

internal enum ScannerSensitivity { Fast, Adaptive, Maximum }
internal enum ScanPass { Original, Global, Contrast, Sharp, Local, Inverted, Region, Enlarged, Reduced }
internal sealed record ScanDiagnostics(int Width, int Height, int Attempts, ScanPass Pass, int FinderPoints,
    bool Located, ScanPass? SuccessfulPass, float ModulePixels, int Contrast, double Sharpness, int Decoded, long Milliseconds, bool Complete, ScannerQrQuality? Qr = null, ScannerAreaQuality? SelectedArea = null);

internal static class ScannerDecoder
{
    private readonly record struct Area(int X, int Y, int W, int H);
    private static unsafe void FillLuma(SKBitmap bitmap,byte[] luma)
    {
        // Video backends return opaque BGRA; avoid allocating a second full
        // SKColor[] image just to compute the luminance plane for QR search.
        if(bitmap.ColorType==SKColorType.Bgra8888&&bitmap.AlphaType==SKAlphaType.Opaque&&bitmap.GetPixels()!=IntPtr.Zero)
        {
            int w=bitmap.Width,h=bitmap.Height,stride=bitmap.RowBytes;byte* pixels=(byte*)bitmap.GetPixels();
            for(int y=0;y<h;y++)
            {
                byte* row=pixels+y*stride;
                for(int x=0;x<w;x++)luma[y*w+x]=(byte)((row[x*4+2]*77+row[x*4+1]*150+row[x*4]*29)>>8);
            }
            GC.KeepAlive(bitmap);return;
        }
        var colors=bitmap.Pixels;
        try
        {
            for(int i=0;i<colors.Length;i++)
            {
                var p=colors[i];int gray=(p.Red*77+p.Green*150+p.Blue*29)>>8;
                luma[i]=(byte)((gray*p.Alpha+255*(255-p.Alpha)+127)/255);
            }
        }
        finally{Array.Clear(colors);}
    }
    internal static List<ScannerHit> Decode(SKBitmap bitmap) => Decode(bitmap, ScannerSensitivity.Adaptive, false, 0, out _);

    internal static List<ScannerHit> Decode(SKBitmap bitmap, ScannerSensitivity sensitivity, bool live, int sequence,
        out ScanDiagnostics diagnostics, Action<ScanDiagnostics>? progress = null, ScannerSearchRegion? selection = null)
    {
        int w = bitmap.Width, h = bitmap.Height;
        if (w < 1 || h < 1 || (long)w * h > 32_000_000) throw new System.FormatException();
        var watch = Stopwatch.StartNew();
        var luma = new byte[w*h];
        var hits = new List<ScannerHit>();
        int attempts = 0, finderPoints = 0, contrast = 0;
        float modulePixels = 0;
        double sharpness = 0;
        bool located = false; ScanPass? successfulPass = null;
        ScanPass pass = ScanPass.Original;
        var regions = new List<Area>();
        ScannerQrQuality? qr = null;
        ScannerAreaQuality? areaQuality=null;
        ScanDiagnostics Snapshot(bool complete) => new(w,h,attempts,pass,finderPoints,located,successfulPass,modulePixels,contrast,sharpness,hits.Count,watch.ElapsedMilliseconds,complete,qr,areaQuality);
        int budget = sensitivity == ScannerSensitivity.Fast ? 1 : live ? (sensitivity == ScannerSensitivity.Maximum ? 12 : 7) : (sensitivity == ScannerSensitivity.Maximum ? 38 : 24);
        long timeBudget = live ? (sensitivity == ScannerSensitivity.Maximum ? 1100 : 650) : (sensitivity == ScannerSensitivity.Maximum ? 9000 : 5000);
        bool CanTry() => attempts < budget && (attempts == 0 || watch.ElapsedMilliseconds < timeBudget);
        try
        {
            FillLuma(bitmap,luma);
            (int lo, int hi) = Range(luma);
            contrast = hi-lo;
            sharpness = Sharpness(luma,w,h);
            var full = new Area(0,0,w,h);
            var search = selection is { } sel ? ToArea(sel,w,h) : full;
            if(selection is not null)areaQuality=RegionQuality(luma,w,search);
            // Geometry and quality come from the ORIGINAL luminance, never an enlarged or sharpened variant.
            void MeasureQr(ResultPoint[]? points, int width, int height, Area area, bool decoded, int dimension = 0)
            {
                if (points is null || points.Length < 3) return;
                var p = points.Take(3).ToArray();
                double Distance(ResultPoint a, ResultPoint b) => Math.Sqrt(Math.Pow((a.X-b.X)*area.W/width,2)+Math.Pow((a.Y-b.Y)*area.H/height,2));
                double sideA=Distance(p[0],p[1]), sideB=Distance(p[1],p[2]);
                double finderSize=p.OfType<FinderPattern>().Select(f=>(double)f.EstimatedModuleSize*Math.Min((double)area.W/width,(double)area.H/height)).DefaultIfEmpty(0).Average();
                bool estimated=dimension==0;
                if (estimated && finderSize>0)
                {
                    double approx=(sideA+sideB)/2/finderSize+7;
                    dimension=21+4*(int)Math.Round((approx-21)/4);
                    if(dimension<21||dimension>177)dimension=0;
                }
                if(dimension<21||dimension>177||(dimension-21)%4!=0)return;
                double module=Math.Min(sideA,sideB)/(dimension-7);
                if(!double.IsFinite(module)||module<=0)return;
                var bounds=Bounds(points,width,height,area,w,h);
                if(bounds is not { } r)return;
                // Measure inside the finder-center quadrilateral bounding box, excluding the large outer padding.
                float x4=p[0].X+p[2].X-p[1].X,y4=p[0].Y+p[2].Y-p[1].Y;
                var inner=ToArea(new ScannerSearchRegion(
                    (area.X+Math.Min(p.Min(a=>a.X),x4)*area.W/width)/w,
                    (area.Y+Math.Min(p.Min(a=>a.Y),y4)*area.H/height)/h,
                    (Math.Max(p.Max(a=>a.X),x4)-Math.Min(p.Min(a=>a.X),x4))*area.W/width/w,
                    (Math.Max(p.Max(a=>a.Y),y4)-Math.Min(p.Min(a=>a.Y),y4))*area.H/height/h),w,h);
                var q=RegionQuality(luma,w,inner);
                var candidate=new ScannerQrQuality(new((float)r.X/w,(float)r.Y/h,(float)r.W/w,(float)r.H/h),
                    (float)(module*dimension),dimension,estimated,module,q.Detail,q.Light,q.Contrast,q.Edge,decoded);
                // Prefer decoded geometry, then sampled dimensions over finder-only guesses; within one class report the smaller module.
                if(qr is null || (decoded&&!qr.Decoded) || (decoded==qr.Decoded && !estimated&&qr.EstimatedDimension) ||
                    (decoded==qr.Decoded && estimated==qr.EstimatedDimension && module<qr.ModulePixels))qr=candidate;
            }

            void Try(byte[] data, int width, int height, Area area, ScanPass method, bool global = false)
            {
                if (!CanTry()) return;
                attempts++; pass = method; progress?.Invoke(Snapshot(false));
                var points = new HashSet<(int X,int Y)>(); float estimated = 0;
                var source = new RGBLuminanceSource(data,width,height,RGBLuminanceSource.BitmapFormat.Gray8);
                var input = new BinaryBitmap(global ? new GlobalHistogramBinarizer(source) : new HybridBinarizer(source));
                var hints = new Dictionary<DecodeHintType,object> {
                    [DecodeHintType.TRY_HARDER] = true,
                    [DecodeHintType.NEED_RESULT_POINT_CALLBACK] = (ResultPointCallback)(point => {
                        if (point is FinderPattern fp) { points.Add(((int)Math.Round(fp.X),(int)Math.Round(fp.Y))); estimated = Math.Max(estimated, fp.EstimatedModuleSize * area.W / width); }
                    })
                };
                // GlobalHistogramBinarizer may return null for a flat image.
                // QRCodeMultiReader assumes a non-null matrix.
                if (input.BlackMatrix is null) return;
                Result[] found = [];
                try { found = new QRCodeMultiReader().decodeMultiple(input,hints) ?? []; }
                catch (ReaderException) { }
                if (found.Length == 0)
                {
                    try { var single = new ZXing.QrCode.QRCodeReader().decode(input,hints); if (single is not null) found = [single]; }
                    catch (ReaderException) { }
                }
                finderPoints = Math.Max(finderPoints,points.Count);
                modulePixels = Math.Max(modulePixels,estimated);
                // Check geometric detection independently of decoding, only on failed passes.
                if (found.Length == 0 && points.Count >= 3 && qr is not { Decoded:true })
                {
                    try
                    {
                        var matrix = input.BlackMatrix;
                        var candidate = matrix is null ? null : new Detector(matrix).detect(hints);
                        if (candidate is not null)
                        {
                            located = true;
                            MeasureQr(candidate.Points,width,height,area,false,candidate.Bits.Width);
                            var region = Bounds(candidate.Points,width,height,area,w,h);
                            if (region is { } r && regions.Count<8) { r=Intersect(r,search); if(r.W>0 && r.H>0 && !regions.Contains(r)) regions.Add(r); }
                        }
                    }
                    catch (ReaderException) { }
                }
                foreach (var result in found)
                {
                    var region = Bounds(result.ResultPoints,width,height,area,w,h);
                    if (region is not { } r) continue;
                    located = true; successfulPass ??= method;
                    MeasureQr(result.ResultPoints,width,height,area,true);
                    foreach (var fp in result.ResultPoints.OfType<FinderPattern>()) modulePixels = Math.Max(modulePixels, fp.EstimatedModuleSize * area.W / width);
                    var hit = ToHit(result,r,w,h);
                    if (hits.Any(existing => Same(existing,hit))) hit.Dispose(); else hits.Add(hit);
                }
                progress?.Invoke(Snapshot(false));
            }
            void Variant(Area area, ScanPass method)
            {
                if (!CanTry()) return;
                int width = area.W, height = area.H;
                byte[] data = area == full ? luma : Crop(luma,w,area);
                byte[] transformed = data;
                try
                {
                    switch (method)
                    {
                        case ScanPass.Contrast: transformed = Stretch(data); break;
                        case ScanPass.Sharp: transformed = Sharpen(data,width,height); break;
                        case ScanPass.Local: transformed = LocalThreshold(data,width,height); break;
                        case ScanPass.Inverted: transformed = Invert(data); break;
                        case ScanPass.Enlarged:
                            if ((long)width*height*4 > 12_000_000) return;
                            transformed = Resize(data,width,height,width*2,height*2); width*=2; height*=2; break;
                        case ScanPass.Reduced:
                            if (width < 64 || height < 64) return;
                            transformed = Resize(data,width,height,width/2,height/2); width/=2; height/=2; break;
                    }
                    Try(transformed,width,height,area,method,method == ScanPass.Global);
                }
                finally
                {
                    if (!ReferenceEquals(transformed,data)) CryptographicOperations.ZeroMemory(transformed);
                    if (!ReferenceEquals(data,luma)) CryptographicOperations.ZeroMemory(data);
                }
            }
            Variant(search,ScanPass.Original);
            if (sensitivity != ScannerSensitivity.Fast)
            {
                // No early exit for static sources: additional passes can find other codes.
                // A live frame with a decoded result takes one extra inverted pass, if affordable.
                if (live && hits.Count > 0) { Variant(search,ScanPass.Inverted); if (sensitivity == ScannerSensitivity.Maximum) foreach (var tile in SearchTiles(search)) Variant(tile,ScanPass.Region); }
                else
                {
                    var strategies = new[] { ScanPass.Global, ScanPass.Contrast, ScanPass.Sharp, ScanPass.Local, ScanPass.Inverted, ScanPass.Reduced, ScanPass.Enlarged };
                    // Rotate the recovery order across camera frames, so expensive passes cannot starve the rest.
                    int offset = live ? (int)((uint)sequence % (uint)strategies.Length) : 0;
                    if (regions.Count > 0)
                        foreach (var r in regions.ToArray()) { Variant(r,ScanPass.Region); Variant(r,ScanPass.Enlarged); }
                    var tiles = SearchTiles(search).ToArray();
                    if(tiles.Length>0&&hits.Count==0)Variant(tiles[live?(int)((uint)sequence%(uint)tiles.Length):0],ScanPass.Region);
                    for (int i = 0; i < strategies.Length && CanTry(); i++)
                    {
                        Variant(search,strategies[(i+offset)%strategies.Length]);
                        // Interleave overlapping original-resolution crops with full-frame retries.
                        if (tiles.Length > 0)
                        {
                            var tile = tiles[(i + (live ? (int)((uint)sequence % (uint)tiles.Length) : 0)) % tiles.Length];
                            Variant(tile,ScanPass.Region);
                        }
                        if (live && hits.Count > 0) break;
                    }
                    if (!live || hits.Count == 0)
                        foreach (var tile in tiles)
                        {
                            if (!CanTry()) break;
                            Variant(tile,ScanPass.Enlarged);
                            if (sensitivity == ScannerSensitivity.Maximum) { Variant(tile,ScanPass.Local); Variant(tile,ScanPass.Inverted); }
                        }
                }
            }
            diagnostics = Snapshot(true);
            return hits;
        }
        catch { foreach (var hit in hits) hit.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(luma); }
    }

    private static Area Intersect(Area a,Area b)
    {
        int x=Math.Max(a.X,b.X),y=Math.Max(a.Y,b.Y);
        return new(x,y,Math.Max(0,Math.Min(a.X+a.W,b.X+b.W)-x),Math.Max(0,Math.Min(a.Y+a.H,b.Y+b.H)-y));
    }
    private static Area ToArea(ScannerSearchRegion region,int w,int h)
    {
        int x=Math.Clamp((int)Math.Floor(region.X*w),0,w-1),y=Math.Clamp((int)Math.Floor(region.Y*h),0,h-1);
        int right=Math.Clamp((int)Math.Ceiling((region.X+region.Width)*w),x+1,w);
        int bottom=Math.Clamp((int)Math.Ceiling((region.Y+region.Height)*h),y+1,h);
        return new(x,y,right-x,bottom-y);
    }
    private static IEnumerable<Area> SearchTiles(Area area)
    {
        if(area.W>=640&&area.H>=480)
        {
            int tw=area.W/2,th=area.H/2;
            // Smaller, overlapping crops improve finder search without inventing new sensor pixels.
            yield return new(area.X+(area.W-tw)/2,area.Y+(area.H-th)/2,tw,th);
            for(int y=0;y<3;y++)for(int x=0;x<3;x++)
                if(x!=1||y!=1)yield return new(area.X+x*(area.W-tw)/2,area.Y+y*(area.H-th)/2,tw,th);
        }
        foreach(var t in Tiles(area.W,area.H))yield return new(t.X+area.X,t.Y+area.Y,t.W,t.H);
    }
    private static ScannerAreaQuality RegionQuality(byte[] source,int stride,Area area)
    {
        Span<int> hist=stackalloc int[256]; hist.Clear();
        double lap=0,light=0,edge=0; int count=0;
        int step=Math.Max(1,Math.Max(area.W,area.H)/500);
        for(int y=area.Y+1;y<area.Y+area.H-1;y+=step)for(int x=area.X+1;x<area.X+area.W-1;x+=step)
        {
            int i=y*stride+x,c=source[i],l=source[i-1],r=source[i+1],t=source[i-stride],b=source[i+stride];
            double v=4*c-l-r-t-b; lap+=v*v; edge+=Math.Abs(l-r)+Math.Abs(t-b); hist[c]++;light+=c;count++;
        }
        if(count==0)return new(0,0,0,0);
        int tail=Math.Max(1,count/20),lo=0,hi=255,n=0;
        while(lo<255&&(n+=hist[lo])<tail)lo++;
        n=0;while(hi>0&&(n+=hist[hi])<tail)hi--;
        return new(Math.Sqrt(lap/count),light/count,Math.Max(0,hi-lo),edge/count);
    }
    private static bool Same(ScannerHit a, ScannerHit b)
    {
        float intersection = Math.Max(0,Math.Min(a.X+a.Width,b.X+b.Width)-Math.Max(a.X,b.X)) * Math.Max(0,Math.Min(a.Y+a.Height,b.Y+b.Height)-Math.Max(a.Y,b.Y));
        return intersection > .45f*Math.Min(a.Width*a.Height,b.Width*b.Height) && a.Payload.Bytes.AsSpan().SequenceEqual(b.Payload.Bytes);
    }
    private static Area? Bounds(ResultPoint[]? pts,int width,int height,Area area,int w,int h)
    {
        if (pts is null || pts.Length < 3) return null;
        float x4 = pts[0].X + pts[2].X - pts[1].X, y4 = pts[0].Y + pts[2].Y - pts[1].Y;
        float minX = Math.Min(pts.Min(p=>p.X),x4), maxX = Math.Max(pts.Max(p=>p.X),x4);
        float minY = Math.Min(pts.Min(p=>p.Y),y4), maxY = Math.Max(pts.Max(p=>p.Y),y4);
        float margin = Math.Max(5,Math.Max(maxX-minX,maxY-minY)*.18f);
        int x = Math.Clamp((int)Math.Floor(area.X+(minX-margin)*area.W/width),0,w-1);
        int y = Math.Clamp((int)Math.Floor(area.Y+(minY-margin)*area.H/height),0,h-1);
        int right = Math.Clamp((int)Math.Ceiling(area.X+(maxX+margin)*area.W/width),x+1,w);
        int bottom = Math.Clamp((int)Math.Ceiling(area.Y+(maxY+margin)*area.H/height),y+1,h);
        return new Area(x,y,right-x,bottom-y);
    }
    private static ScannerHit ToHit(Result result,Area r,int w,int h)
    {
        byte[]? binary = null;
        if (result.ResultMetadata is not null && result.ResultMetadata.TryGetValue(ResultMetadataType.BYTE_SEGMENTS, out var obj) && obj is IEnumerable<byte[]> segments)
        {
            var joined = segments.SelectMany(s => s).ToArray();
            try
            {
                var decoded = new UTF8Encoding(false, true).GetString(joined);
                if (decoded.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r'))) binary = joined;
            }
            catch (DecoderFallbackException) { binary = joined; }
            if (binary is null) CryptographicOperations.ZeroMemory(joined);
        }
        return new ScannerHit(ScannerPayload.Parse(result.Text ?? "",binary), (float)r.X/w,(float)r.Y/h,(float)r.W/w,(float)r.H/h);
    }
    private static IEnumerable<Area> Tiles(int w,int h)
    {
        if (w < 320 || h < 240) yield break;
        int tw = w*2/3, th = h*2/3;
        yield return new Area((w-tw)/2,(h-th)/2,tw,th);
        yield return new Area(0,0,tw,th); yield return new Area(w-tw,0,tw,th);
        yield return new Area(0,h-th,tw,th); yield return new Area(w-tw,h-th,tw,th);
    }
    private static byte[] Crop(byte[] source,int stride,Area a)
    {
        var result = new byte[a.W*a.H];
        for (int y=0;y<a.H;y++) Array.Copy(source,(a.Y+y)*stride+a.X,result,y*a.W,a.W);
        return result;
    }
    private static (int Low,int High) Range(byte[] source)
    {
        Span<int> hist = stackalloc int[256]; hist.Clear();
        foreach (byte value in source) hist[value]++;
        int trim = source.Length/100, low=0, high=255, count=0;
        while (low<255 && (count+=hist[low])<=trim) low++;
        count=0; while (high>0 && (count+=hist[high])<=trim) high--;
        return (low,Math.Max(low,high));
    }
    private static byte[] Invert(byte[] source)
    {
        var result = new byte[source.Length];
        for (int i=0;i<source.Length;i++) result[i]=(byte)(255-source[i]);
        return result;
    }
    private static byte[] Stretch(byte[] source)
    {
        var (lo,hi) = Range(source); int range=Math.Max(1,hi-lo);
        var result=new byte[source.Length];
        for (int i=0;i<source.Length;i++) result[i]=(byte)Math.Clamp((source[i]-lo)*255/range,0,255);
        return result;
    }
    private static double Sharpness(byte[] source,int w,int h)
    {
        // Mean absolute Laplacian on a sparse grid. Relative indicator, not a focus measurement.
        long sum=0,count=0;
        for (int y=1;y<h-1;y+=3) for (int x=1;x<w-1;x+=3)
        { int i=y*w+x; sum+=Math.Abs(4*source[i]-source[i-1]-source[i+1]-source[i-w]-source[i+w]); count++; }
        return count==0?0:(double)sum/count;
    }
    private static byte[] Sharpen(byte[] source,int w,int h)
    {
        var result=source.ToArray();
        for (int y=1;y<h-1;y++) for (int x=1;x<w-1;x++)
        { int i=y*w+x; int lap=4*source[i]-source[i-1]-source[i+1]-source[i-w]-source[i+w]; result[i]=(byte)Math.Clamp(source[i]+lap/2,0,255); }
        return result;
    }
    private static byte[] LocalThreshold(byte[] source,int w,int h)
    {
        // Sliding window mean, O(w*h) time and O(w) scratch memory, no giant integral image.
        int radius=Math.Clamp(Math.Min(w,h)/32,8,64);
        var columns=new int[w]; var result=new byte[source.Length];
        for(int y=0;y<=Math.Min(radius,h-1);y++) for(int x=0;x<w;x++) columns[x]+=source[y*w+x];
        for(int y=0;y<h;y++)
        {
            int rows=Math.Min(h-1,y+radius)-Math.Max(0,y-radius)+1, sum=0;
            for(int x=0;x<=Math.Min(w-1,radius);x++) sum+=columns[x];
            for(int x=0;x<w;x++)
            {
                int cols=Math.Min(w-1,x+radius)-Math.Max(0,x-radius)+1;
                result[y*w+x]=source[y*w+x] < sum/(rows*cols)-6 ? (byte)0 : (byte)255;
                if(x-radius>=0) sum-=columns[x-radius];
                if(x+radius+1<w) sum+=columns[x+radius+1];
            }
            if(y-radius>=0) for(int x=0;x<w;x++) columns[x]-=source[(y-radius)*w+x];
            if(y+radius+1<h) for(int x=0;x<w;x++) columns[x]+=source[(y+radius+1)*w+x];
        }
        Array.Clear(columns); return result;
    }
    private static byte[] Resize(byte[] source,int w,int h,int nw,int nh)
    {
        var result=new byte[nw*nh];
        for(int y=0;y<nh;y++) for(int x=0;x<nw;x++)
        {
            // Bilinear sampling for enlargement; box average for the 2:1 reduction.
            if(nw<w && nh<h)
            { int i=y*2*w+x*2; result[y*nw+x]=(byte)((source[i]+source[i+1]+source[i+w]+source[i+w+1]+2)/4); continue; }
            int x0=x/2,y0=y/2,x1=Math.Min(w-1,x0+1),y1=Math.Min(h-1,y0+1);
            int fx=x%2,fy=y%2;
            result[y*nw+x]=(byte)((source[y0*w+x0]*(2-fx)*(2-fy)+source[y0*w+x1]*fx*(2-fy)+source[y1*w+x0]*(2-fx)*fy+source[y1*w+x1]*fx*fy+2)/4);
        }
        return result;
    }
}
