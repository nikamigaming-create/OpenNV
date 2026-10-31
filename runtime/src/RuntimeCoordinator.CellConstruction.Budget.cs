using System.Diagnostics;
using Godot;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    // Charge completed synchronous work, never worker waits or drawn frames.
    // Nested assembly and the reference loop share the same frame allowance.
    private sealed class NativeCellConstructionBudget
    {
        private ulong _frame = ulong.MaxValue;
        private double _frameMilliseconds;
        private double _lastYieldWorkMilliseconds = -1;
        internal double WorkMilliseconds { get; private set; }
        internal double MaximumFrameMilliseconds { get; private set; }
        internal int WorkFrames { get; private set; }
        internal int YieldRequests { get; private set; }

        internal long BeginStep()
        {
            var frame = Engine.GetProcessFrames();
            if (frame != _frame)
            {
                _frame = frame;
                _frameMilliseconds = 0;
                WorkFrames++;
            }
            return Stopwatch.GetTimestamp();
        }

        internal double CompleteStep(long started)
        {
            var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _frameMilliseconds += milliseconds;
            WorkMilliseconds += milliseconds;
            MaximumFrameMilliseconds = Math.Max(MaximumFrameMilliseconds, _frameMilliseconds);
            return milliseconds;
        }

        internal bool RequestYield()
        {
            if (DisplayServer.GetName() == "headless" || Engine.GetProcessFrames() != _frame ||
                _frameMilliseconds < 8 || WorkMilliseconds == _lastYieldWorkMilliseconds) return false;
            _lastYieldWorkMilliseconds = WorkMilliseconds;
            YieldRequests++;
            return true;
        }
    }
}
