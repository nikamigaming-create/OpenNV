namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    // Distinct original producer lanes. They are not the presentation POV,
    // Pip-Boy mode, Main scene byte, source camera override or tree pause.
    private byte _viewRequest, _viewRequestArgument, _countdownEnabled;
    private uint _countdownBits = 0x40a00000, _referenceScratch;
    private FalloutStandaloneScenePreludeCall? _prelude;
    private FalloutPlayerReferenceHudCall? _referenceHudCall;
    private FalloutStandaloneActorScalarReset? _actorScalarReset;
    internal void ExecuteInitialVirtualPrefix(Guid main, long ordinal, Action releaseSharedOwnedChild,
        FalloutPlayerReferenceHudState? hud = null, Func<float>? readScaledTimer = null)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(releaseSharedOwnedChild);
        if (_clock is not { Step: FalloutStandaloneSceneClockStep.VirtualEntered, ArgumentBits: not null } clock ||
            clock.Main != main || clock.MainOrdinal != ordinal || _prelude is not null)
            throw new InvalidOperationException("Player initial virtual prefix changed its actual clock/Main invocation.");
        var fault = _reentry; _writing = true;
        _prelude = new(main, ordinal, Next(), _sequence, FalloutStandaloneScenePreludeStep.ViewRequests, null, null, Guid.NewGuid());
        try
        {
            if (_viewRequest != 0) throw new NotSupportedException("source-Player-original-two-byte-view-request-child-unowned");
            Changed(FalloutStandaloneScenePreludeStep.OwnedChildRelease); releaseSharedOwnedChild(); Check();
            Changed(FalloutStandaloneScenePreludeStep.Countdown);
            if (_countdownEnabled != 0) throw new NotSupportedException("source-Player-countdown-independent-cached-menu-and-global-cinematic-query-unowned");
            Changed(FalloutStandaloneScenePreludeStep.ReferenceScratchStore); _referenceScratch = 0; Next();
            Changed(FalloutStandaloneScenePreludeStep.ReferenceQuery);
            ReadInitialSceneReference(main, ordinal); Check();
            Changed(FalloutStandaloneScenePreludeStep.ReferenceFrame);
            // This consumer is called even for a returned null reference. It
            // owns independent HUD fields and must return before the stored
            // reference release or the rest of the actual Player virtual.
            if (hud is null) throw new NotSupportedException("source-Player-scene-reference-frame-HUD-consumer-unowned");
            if (hud.Process != _process || hud.Stack != _stack || hud.Source.Main != Source.Main)
                throw new InvalidOperationException("Player HUD child changed its actual Main/source/process owner.");
            try { hud.Update(main, ordinal, _referenceQuery!.Result, _sceneReferenceArgument); }
            finally
            {
                if (hud.LastCall is { } call && call.Main == main && call.MainOrdinal == ordinal)
                { _referenceHudCall = call; Next(); }
            }
            Check(); Changed(FalloutStandaloneScenePreludeStep.ReferenceRelease);
            if (_sceneReference is not null)
                throw new NotSupportedException("source-Player-scene-reference-node-actual-refcount-release-unowned");
            _sceneReference = null; Next();
            Changed(FalloutStandaloneScenePreludeStep.ActorScalarReset);
            _actorScalarReset = new(main, ordinal, Next(), _sequence, new uint?[4], 0, false);
            for (var index = 0; index < 4; ++index)
            {
                var fields = _actorScalarReset.Fields.ToArray();
                fields[index] = OpenNV.Runtime.Content.FalloutStandalonePlayerSceneSource.ActorSceneResetBits;
                _actorScalarReset = _actorScalarReset with { Fields = fields, Stores = index + 1, Changed = Next() };
            }
            _actorScalarReset = _actorScalarReset with { Returned = true, Changed = Next() };
            Changed(FalloutStandaloneScenePreludeStep.ThreadContext);
            _threadContextEntry = _threadContext.EnterPlayer(main, ordinal); Next();
            Changed(FalloutStandaloneScenePreludeStep.InputManagerQuery);
            _ = ReadInputManager(main, ordinal); Check();
            Changed(FalloutStandaloneScenePreludeStep.ActorProcessQuery);
            ReadActorProcessQueries(main, ordinal);
            Changed(FalloutStandaloneScenePreludeStep.ActorSceneContinuation);
            UpdateSceneScalars(main, ordinal, readScaledTimer);
            Changed(FalloutStandaloneScenePreludeStep.QueuedSceneNodes);
            UpdateQueuedSceneReferences(main, ordinal);
            Changed(FalloutStandaloneScenePreludeStep.ProcessSceneUpdate);
            UpdateProcessScene(main, ordinal);
            Changed(FalloutStandaloneScenePreludeStep.PostProcessContext);
            ReadPostProcessContext(main, ordinal);
            Changed(FalloutStandaloneScenePreludeStep.TimedActions);
            UpdateTimedActions(main, ordinal, readScaledTimer);
            Changed(FalloutStandaloneScenePreludeStep.PlayerSceneContinuation);
            UpdatePostActions(main, ordinal, readScaledTimer);
            UpdateActorDistances(main, ordinal);
            UpdateIdleAnimation(main, ordinal, readScaledTimer);
            UpdateMovementPrefix(main, ordinal);
            UpdateFollowingServices(main, ordinal, readScaledTimer);
            UpdatePlayerWeaponServices(main, ordinal);
            UpdatePlayerFollowingContinuation(main, ordinal);
            throw new InvalidOperationException("Following Player prefix returned across an unowned continuation.");
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
