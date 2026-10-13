using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal void BindInputManager(FalloutDirectInputState input)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(input);
        if (_inputManager is not null || input.Source != _stack)
            throw new InvalidOperationException("Player input manager changed its selected source or current singleton factory.");
        _inputManager = input.ReadManagerOwner(); _thread ??= Environment.CurrentManagedThreadId;
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
