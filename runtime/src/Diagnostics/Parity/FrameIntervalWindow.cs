namespace OpenNV.Runtime.Diagnostics.Parity;

/// <summary>Bounded host draw cadence; no images or headset timing claims.</summary>
internal sealed class FrameIntervalWindow
{
    internal const int TargetFramesPerSecond = 144;
    internal const double FrameBudgetMilliseconds = 1000.0 / TargetFramesPerSecond;
    internal const double FlatFrameBudgetMilliseconds = 1000.0 / 60;
    private readonly double[] _intervals = new double[512];
    private double? _previousMilliseconds;
    private int _next;
    private int _count;
    private long _totalSamples, _totalOver60, _totalOver144;
    private double _totalMaximum;

    internal void Reset()
    {
        _previousMilliseconds = null;
        _next = 0;
        _count = 0;
        _totalSamples = _totalOver60 = _totalOver144 = 0;
        _totalMaximum = 0;
    }

    internal void Record(double monotonicMilliseconds)
    {
        if (_previousMilliseconds is { } previous)
        {
            var interval = monotonicMilliseconds - previous;
            if (!double.IsFinite(interval) || interval < 0)
                throw new InvalidDataException("Host frame clock is not finite and monotonic.");
            _intervals[_next] = interval;
            ++_totalSamples;
            if (interval > FlatFrameBudgetMilliseconds) ++_totalOver60;
            if (interval > FrameBudgetMilliseconds) ++_totalOver144;
            _totalMaximum = Math.Max(_totalMaximum, interval);
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
        return new(_count, Percentile(0.5), Percentile(0.95), Percentile(0.99), sorted[^1],
            TargetFramesPerSecond, FrameBudgetMilliseconds, sorted.Count(interval => interval > FrameBudgetMilliseconds),
            _totalSamples, _totalMaximum, FlatFrameBudgetMilliseconds, _totalOver60, _totalOver144);
    }
}

internal sealed record FrameIntervalSummary(int Samples, double MedianMilliseconds,
    double P95Milliseconds, double P99Milliseconds, double MaximumMilliseconds,
    int TargetFramesPerSecond, double BudgetMilliseconds, int OverBudgetIntervals,
    long TotalSamples, double TotalMaximumMilliseconds, double FlatBudgetMilliseconds,
    long TotalOverFlatBudgetIntervals, long TotalOver144BudgetIntervals)
{
    public double OverBudgetFraction => (double)OverBudgetIntervals / Samples;
}
