using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal void BindInputManager(FalloutDirectInputState input)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(input);
        if (_inputManager is not null || _movementControl is not null || input.Source != _stack)
            throw new InvalidOperationException("Player input manager changed its selected source or current singleton factory.");
        var current = input.ReadManagerOwner();
        if (!ReferenceEquals(current, input) || current.Source != _stack || current.Factory == Guid.Empty ||
            !FalloutDirectInputState.ValidProvider(current.Provider, current.Window))
            throw new InvalidOperationException("Player input publication lost its genuine current source/factory owner.");
        // Owned data can prepare the scene on a loading worker. The control
        // owner is constructed only after the real input singleton verifies
        // this gameplay thread; preparation never captures that live lease.
        if (_controlFields.HasActualInput) _controlFields.RequireActualInput(current);
        else _controlFields.BindActualInput(current);
        var controls = new FalloutPlayerMovementControlState(FalloutPlayerMovementControlSource.Reviewed(Source.Main),
            _stack, _process, _movementCall?.Control, _controlFields);
        if (_referenceOrientation is not null) controls.BindOrientation(_referenceOrientation);
        _inputManager = current; _movementControl = controls; _thread ??= Environment.CurrentManagedThreadId;
    }
    internal void BindMovementControlConfiguration(FalloutNumericIniSettings settings)
    {
        RequireIdle();
        (_movementControl ?? throw new InvalidOperationException("Player control configuration requires actual input-thread publication."))
            .BindConfiguration(settings);
    }
    private FalloutDirectInputState ReadInputManager(Guid main, long ordinal)
    {
        if (_inputManager is not { } owner)
            throw new NotSupportedException("source-Player-scene-current-input-manager-product-factory-unbound");
        var current = owner.ReadManagerOwner();
        if (!ReferenceEquals(current, owner) || current.Source != _stack)
            throw new InvalidOperationException("Player getter returned a foreign source input manager.");
        _inputManagerQuery = new(main, ordinal, _process, _stack, current.Factory, current.Window,
            current.Sample, current.Revision, Next(), _sequence, true, current.Provider);
        return current;
    }
    private static void ValidateInputManagerQuery(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.InputManagerQuery is not { } query)
        {
            if (saved.Prelude?.FailedAt is FalloutStandaloneScenePreludeStep.ActorProcessQuery or FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player Actor process query lost its returned actual input manager.");
            return;
        }
        if (saved.Prelude is not { FailedAt: FalloutStandaloneScenePreludeStep.ActorProcessQuery or FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prefix ||
            saved.ThreadContextEntry is not { ScopeEntered: true } context || query.Main != prefix.Main ||
            query.MainOrdinal != prefix.MainOrdinal || query.Process != context.Process || query.Stack != saved.Stack ||
            query.Factory == Guid.Empty || !FalloutDirectInputState.ValidProvider(query.Provider, query.Window) || query.Revision < 0 || !query.Returned ||
            query.Entered <= prefix.Entered || query.Changed < query.Entered || query.Changed > saved.Sequence)
            throw new InvalidDataException("Player input-manager receipt lost its living source factory or exact failed prefix.");
    }
}
