namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal void RequireOpacityInvocation(Guid main, long ordinal)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
        if (!_writing || _reading || _clock is not { Step: FalloutStandaloneSceneClockStep.VirtualEntered } clock ||
            clock.Main != main || clock.MainOrdinal != ordinal || _movementCall is not { Returned: true } movement ||
            movement.Main != main || movement.MainOrdinal != ordinal)
            throw new InvalidOperationException("Opacity child requires the actual returned movement child and entered Player/Main invocation.");
    }
    internal (Guid Main, long Ordinal) RequireCurrentOpacityInvocation()
    {
        var clock = _clock ?? throw new InvalidOperationException("Opacity timer lost its actual Player clock.");
        RequireOpacityInvocation(clock.Main, clock.MainOrdinal); return (clock.Main, clock.MainOrdinal);
    }
    internal FalloutStandaloneFirstPersonOwner? ReadOpacityFirstPerson()
    {
        _ = RequireCurrentOpacityInvocation();
        if (_first is null) return null;
        if (_first.Released is not null || _livingFirst is null || !ReadLiving(_livingFirst))
            throw new NotSupportedException("source-Player-opacity-current-first-person-publication-retired-or-unbound");
        return _first;
    }
}
