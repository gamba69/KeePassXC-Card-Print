using System.Diagnostics;

namespace KeePassXCCardPrint.Services;

/// <summary>Metadata-only, bounded counters: never inspect or retain card contents.</summary>
internal static class PreviewTelemetry
{
    private const int Window = 30;
    private static readonly object Gate = new();
    private static readonly double[] DurationsMs = new double[Window];
    private static int _next, _count;
    private static long _hits, _misses;

    internal static void CacheLookup(bool hit)
    {
        lock (Gate) { if (hit) _hits++; else _misses++; }
    }

    internal static void Completed(long startTimestamp)
    {
        double elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        if (!double.IsFinite(elapsed) || elapsed < 0) return;
        lock (Gate)
        {
            DurationsMs[_next] = elapsed;
            _next = (_next + 1) % Window;
            if (_count < Window) _count++;
        }
    }

    // Type-7 quantile: interpolate between adjacent unrounded measurements.
    // Both the median and P95 use the same definition for small sample windows.
    internal static double PercentileSorted(ReadOnlySpan<double> sortedSamples, double percentile)
    {
        if (sortedSamples.IsEmpty) throw new ArgumentException("Samples are required.", nameof(sortedSamples));
        if (!double.IsFinite(percentile) || percentile < 0 || percentile > 1)
            throw new ArgumentOutOfRangeException(nameof(percentile));
        double position = (sortedSamples.Length - 1) * percentile;
        int lower = (int)position;
        int upper = Math.Min(lower + 1, sortedSamples.Length - 1);
        return sortedSamples[lower] + (sortedSamples[upper] - sortedSamples[lower]) * (position - lower);
    }

    internal static PreviewTelemetrySnapshot Snapshot()
    {
        lock (Gate)
        {
            if (_count == 0)
                return new PreviewTelemetrySnapshot(null, null, _hits + _misses == 0 ? null :
                    100d * _hits / (_hits + _misses));
            var samples = new double[_count];
            Array.Copy(DurationsMs, samples, _count);
            Array.Sort(samples);
            return new PreviewTelemetrySnapshot(PercentileSorted(samples, .5),
                PercentileSorted(samples, .95),
                _hits + _misses == 0 ? null : 100d * _hits / (_hits + _misses));
        }
    }
}

internal readonly record struct PreviewTelemetrySnapshot(double? MedianMs, double? P95Ms, double? CacheHitPct);
