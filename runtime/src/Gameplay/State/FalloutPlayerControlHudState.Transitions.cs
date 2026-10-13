namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerControlHudState
{
    private void EnterMenuTransition(Guid invocation, Action check)
    {
        var duration = _ui.ReadFloat(_root, "fadein");
        if (!float.IsNaN(duration) && duration == 0) duration = _ui.ReadFloat(_root, "fadeout");
        _call = _call! with { DurationBits = BitConverter.SingleToUInt32Bits(duration), Changed = Next() };
        var scope = _threadContext.EnterMenuAllocation(invocation, _factory, check);
        _call = _call with { Allocation = scope, Changed = Next() };
        try
        {
            // Source replacement removes the first matching object, preserving
            // the order of every other current interface transition.
            _context.ReplaceMenuTransition(_factory, _call.DurationBits.Value);
            Next(); check();
            scope = _threadContext.ReturnMenuAllocation(scope, check);
            _call = _call with { Allocation = scope, Changed = Next() };
        }
        catch (Exception error)
        {
            if (!scope.Returned)
            {
                _threadContext.RetainMenuAllocationFailure(scope, error);
                _call = _call with { Allocation = scope with { Failure = error.Message }, Changed = Next() };
            }
            throw;
        }
        _menuMode = 8; Next(); Step("menu-transition-frame");
        var frame = _context.ReadTransitionFrame(); check();
        if (frame.Clock.Source != _source.Controls.Main.EngineSha256 || frame.Clock.Process != _process || frame.Clock.Epoch < 1 ||
            frame.Clock.Sequence < 0 || (frame.ControlMode == 4) != (frame.DivisorBits is not null))
            throw new InvalidDataException("HUD transition lost its genuine current source clock/control-mode getter.");
        _call = _call with { Frame = frame, Changed = Next() };
        _ = _context.AdvanceMenuTransitions(frame); check(); Next();
    }
}
