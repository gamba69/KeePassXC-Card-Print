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
            int last = (_next + Window - 1) % Window;
            return new PreviewTelemetrySnapshot(DurationsMs[last],
                samples[(int)Math.Ceiling(_count * .95) - 1],
                _hits + _misses == 0 ? null : 100d * _hits / (_hits + _misses));
        }
    }
}

internal readonly record struct PreviewTelemetrySnapshot(double? LastMs, double? P95Ms, double? CacheHitPct);
