using System.Diagnostics;
using Godot;
using Environment = System.Environment;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed class RuntimeFramePhaseCounter(int expectedThread)
{
    private readonly object _gate = new();
    private long _begins, _ends, _calls, _ticks, _maximum, _unpairedBegins, _unpairedEnds, _invalidPairs, _threadMismatches, _clockRegressions;
    private long _start, _last;
    private int _beginThread, _endThread;
    private bool _pending, _valid;
    internal void Begin(bool valid = true) => BeginAt(Stopwatch.GetTimestamp(), Environment.CurrentManagedThreadId, valid);
    internal void End(bool valid = true) => EndAt(Stopwatch.GetTimestamp(), Environment.CurrentManagedThreadId, valid);
    internal void BeginAt(long timestamp, int thread, bool valid = true)
    {
        lock (_gate)
        {
            _begins++; if (_pending) _unpairedBegins++;
            if (thread != expectedThread) _threadMismatches++;
            _start = timestamp; _beginThread = thread; _pending = true; _valid = valid && thread == expectedThread;
        }
    }
    internal void EndAt(long timestamp, int thread, bool valid = true)
    {
        lock (_gate)
        {
            _ends++; _endThread = thread;
            if (thread != expectedThread) _threadMismatches++;
            if (!_pending) { _unpairedEnds++; return; }
            _pending = false;
            if (timestamp < _start) _clockRegressions++;
            if (!_valid || !valid || thread != expectedThread || thread != _beginThread || timestamp < _start) { _invalidPairs++; return; }
            var ticks = timestamp - _start; _calls++; _ticks += ticks; _last = ticks; _maximum = Math.Max(_maximum, ticks);
        }
    }
    internal void Close()
    {
        lock (_gate) { if (_pending) _unpairedBegins++; _pending = false; }
    }
    internal object Capture()
    {
        lock (_gate) return new
        {
            calls = _calls,
            begins = _begins,
            ends = _ends,
            totalMilliseconds = _ticks * (1000.0 / Stopwatch.Frequency),
            maximumMilliseconds = _maximum * (1000.0 / Stopwatch.Frequency),
            pending = _pending,
            lastMilliseconds = _last * (1000.0 / Stopwatch.Frequency),
            unpairedBegins = _unpairedBegins,
            unpairedEnds = _unpairedEnds,
            invalidPairs = _invalidPairs,
            threadMismatches = _threadMismatches,
            clockRegressions = _clockRegressions,
            expectedThreadId = expectedThread,
            lastBeginThreadId = _beginThread,
            lastEndThreadId = _endThread
        };
    }
}

internal sealed partial class RuntimeFramePhaseMarker : Node
{
    internal RuntimeFramePhaseCounter ProcessPhase = null!, PhysicsPhase = null!;
    internal bool Head, CoverageValid;
    public override void _Process(double delta) { if (Head) ProcessPhase.Begin(CoverageValid); else ProcessPhase.End(CoverageValid); }
    public override void _PhysicsProcess(double delta) { if (Head) PhysicsPhase.Begin(CoverageValid); else PhysicsPhase.End(CoverageValid); }
}
