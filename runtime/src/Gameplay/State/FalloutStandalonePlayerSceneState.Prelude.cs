namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    // Distinct original producer lanes. They are not the presentation POV,
    // Pip-Boy mode, Main scene byte, source camera override or tree pause.
    private byte _viewRequest, _viewRequestArgument, _countdownEnabled;
    private uint _countdownBits = 0x40a00000, _referenceScratch;
    private FalloutStandaloneScenePreludeCall? _prelude;
    internal void ExecuteInitialVirtualPrefix(Guid main, long ordinal, Action releaseSharedOwnedChild)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(releaseSharedOwnedChild);
        if (_clock is not { Step: FalloutStandaloneSceneClockStep.VirtualEntered, ArgumentBits: not null } clock ||
            clock.Main != main || clock.MainOrdinal != ordinal || _prelude is not null)
            throw new InvalidOperationException("Player initial virtual prefix changed its actual clock/Main invocation.");
        var fault = _reentry; _writing = true;
        _prelude = new(main, ordinal, Next(), _sequence, FalloutStandaloneScenePreludeStep.ViewRequests, null, null);
        try
        {
            if (_viewRequest != 0) throw new NotSupportedException("source-Player-original-two-byte-view-request-child-unowned");
            Changed(FalloutStandaloneScenePreludeStep.OwnedChildRelease); releaseSharedOwnedChild(); Check();
            Changed(FalloutStandaloneScenePreludeStep.Countdown);
            if (_countdownEnabled != 0) throw new NotSupportedException("source-Player-countdown-independent-cached-menu-and-global-cinematic-query-unowned");
            Changed(FalloutStandaloneScenePreludeStep.ReferenceScratchStore); _referenceScratch = 0; Next();
            Changed(FalloutStandaloneScenePreludeStep.ReferenceQuery);
            // Even with the constructor-null stored reference, the original
            // query first enters a Player child before testing that reference.
            // A null field cannot skip it or complete the full virtual update.
            throw new NotSupportedException("source-Player-scene-parent-reference-query-before-null-reference-test-unowned");
        }
        catch (Exception error)
        {
            RetainClockFailure(error);
            _prelude = _prelude! with
            {
                FailedAt = _prelude!.Step,
                Step = FalloutStandaloneScenePreludeStep.Failed,
                Changed = _sequence,
                Failure = _failure
            }; throw;
        }
        finally { _writing = false; }
        void Changed(FalloutStandaloneScenePreludeStep step) => _prelude = _prelude! with { Step = step, Changed = Next() };
        void Check()
        {
            RequireThread();
            if (_reentry != fault) throw new InvalidOperationException("Shared Player child release swallowed a scene owner reentry refusal.");
        }
    }
}
