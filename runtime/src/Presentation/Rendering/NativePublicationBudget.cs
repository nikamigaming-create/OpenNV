using System.Diagnostics;
using Godot;

namespace OpenNV.Runtime.Presentation.Rendering;

// All ordinary world upload owners spend the same allowance. Native operations
// are indivisible: record their actual cost, including an overrun, before any
// other owner admits work. This is admission control, not a hard GPU deadline.
internal static class NativePublicationBudget
{
    private static ulong _frame = ulong.MaxValue;
    private static double _spent;
    private static bool _nearPending, _distantPending;
    private static bool _priorityStarted;

    internal static void SetPending(bool distant, bool pending)
    {
        if (distant) _distantPending = pending; else _nearPending = pending;
    }
    internal const double Milliseconds = 2;

    internal static bool TryBegin(bool distant, out long started)
    {
        var frame = Engine.GetProcessFrames();
        if (_frame != frame) { _frame = frame; _spent = 0; _priorityStarted = false; }
        started = Stopwatch.GetTimestamp();
        // Alternate first admission when both streams are active, so a slow
        // near operation cannot indefinitely starve distant refinement.
        var priorityDistant = (frame & 1) != 0;
        if (_nearPending && _distantPending && distant != priorityDistant && !_priorityStarted) return false;
        if (_spent >= Milliseconds) return false;
        if (distant == priorityDistant) _priorityStarted = true;
        return true;
    }

    internal static void Complete(long started) => _spent += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
