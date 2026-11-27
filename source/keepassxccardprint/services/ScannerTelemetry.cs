namespace KeePassXCCardPrint.Services;

// Coordinates in the original image (not zoomed/rotated preview coordinates).
internal sealed record ScannerSearchRegion(float X, float Y, float Width, float Height);
internal sealed record ScannerAreaQuality(double Detail,double Light,int Contrast,double Edge);
internal sealed record ScannerQrQuality(ScannerSearchRegion Region, float SidePixels, int Modules,
    bool EstimatedDimension, double ModulePixels, double Detail, double Light, int Contrast,
    double EdgeStrength, bool Decoded);

// Only numeric measurements are retained; the rolling window never retains frames or payloads.
internal sealed class ScannerMetricWindow
{
    internal const long WindowMs = 30_000;
    private readonly Queue<(long Time, double Value)> _samples = new();
    internal void Add(long now, double? value)
    {
        Trim(now);
        if (value is double v && double.IsFinite(v)) _samples.Enqueue((now,v));
        while (_samples.Count > 360) _samples.Dequeue();
    }
    internal void Trim(long now)
    { while (_samples.TryPeek(out var v) && now-v.Time > WindowMs) _samples.Dequeue(); }
    internal void Clear() => _samples.Clear();
    internal int Count => _samples.Count;
    internal double? Percentile(double fraction)
    {
        if (_samples.Count == 0) return null;
        var sorted = _samples.Select(v=>v.Value).Order().ToArray();
        int i = Math.Clamp((int)Math.Ceiling(fraction*sorted.Length)-1,0,sorted.Length-1);
        return sorted[i];
    }
    internal (long Time, double Value)[] Points() => _samples.ToArray();
}
