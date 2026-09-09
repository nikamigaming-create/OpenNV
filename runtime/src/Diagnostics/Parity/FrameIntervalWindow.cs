namespace OpenNV.Runtime.Diagnostics.Parity;

/// <summary>Bounded host draw cadence; no images or headset timing claims.</summary>
internal sealed class FrameIntervalWindow
{
    private readonly double[] _intervals = new double[512];
    private double? _previousMilliseconds;
    private int _next;
    private int _count;

    internal void Record(double monotonicMilliseconds)
    {
        if (_previousMilliseconds is { } previous)
        {
            _intervals[_next] = monotonicMilliseconds - previous;
            _next = (_next + 1) % _intervals.Length;
            _count = Math.Min(_count + 1, _intervals.Length);
        }
        _previousMilliseconds = monotonicMilliseconds;
    }

    internal FrameIntervalSummary? Capture()
    {
        if (_count == 0) return null;
        var sorted = _intervals.AsSpan(0, _count).ToArray();
        Array.Sort(sorted);
        double Percentile(double fraction) => sorted[(int)Math.Ceiling(fraction * _count) - 1];
        return new(_count, Percentile(0.5), Percentile(0.95), Percentile(0.99), sorted[^1]);
    }
}

internal sealed record FrameIntervalSummary(int Samples, double MedianMilliseconds,
    double P95Milliseconds, double P99Milliseconds, double MaximumMilliseconds);
