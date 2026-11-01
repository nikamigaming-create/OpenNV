namespace OpenNV.Runtime.Gameplay.State;

// GetVATSValue reads the shared VATS action context, not the condition subject's
// actor process. Native initializes the result to zero and leaves it there when
// no action context exists, before interpreting either selector argument.
internal sealed class FalloutVatsConditionState
{
    private Func<bool>? _contextActive;

    internal void BindContext(Func<bool> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        if (_contextActive is not null) throw new InvalidOperationException("VATS context already has a live owner.");
        _contextActive = active;
    }

    internal float Query(uint selector, uint value)
    {
        // New worlds and restored saves start without an action context. Active
        // VATS already refuses saving until its attack continuation is owned.
        if (_contextActive?.Invoke() != true) return 0;
        throw new NotSupportedException($"GetVATSValue selector {selector}/{value} requires its current VATS action-context value owner.");
    }

    internal float Mode() => _contextActive?.Invoke() != true ? 0 :
        throw new NotSupportedException("Active VATS camera mode requires its native phase owner.");

    internal float TargetHeight() => _contextActive?.Invoke() != true ? 0 :
        throw new NotSupportedException("Active VATS target height requires its attack-camera target owner.");
}
