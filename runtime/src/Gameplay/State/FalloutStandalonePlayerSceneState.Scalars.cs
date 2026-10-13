using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerSceneScalarCall(Guid Main, long MainOrdinal, long Entered, long Changed,
    FalloutPlayerSceneScalarFields Before, FalloutPlayerSceneScalarFields After, uint? ScaledTimerBits,
    int Stores, bool Returned);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerSceneScalarFields _sceneScalars = FalloutPlayerSceneScalarFields.Constructed;
    private FalloutPlayerSceneScalarCall? _sceneScalarCall;

    private void UpdateSceneScalars(Guid main, long ordinal, Func<float>? readScaledTimer)
    {
        var before = _sceneScalars;
        var entered = Next();
        _sceneScalarCall = new(main, ordinal, entered, entered, before, before, null, 0, false);
        Store(before with { FirstScratch = 0 });
        Store(_sceneScalars with { SecondScratch = 0 });
        var scaledTimer = 0f;
        if (before.SubtractsScaledTimer)
        {
            var fault = _reentry;
            scaledTimer = (readScaledTimer ?? throw new NotSupportedException("source-Player-scene-current-Main-scaled-timer-unbound"))();
            RequireThread();
            if (_reentry != fault) throw new InvalidOperationException("Player scalar timer swallowed a scene owner reentry refusal.");
            _sceneScalarCall = _sceneScalarCall with { ScaledTimerBits = BitConverter.SingleToUInt32Bits(scaledTimer), Changed = Next() };
        }
        Store(before.Update(scaledTimer));
        _sceneScalarCall = _sceneScalarCall with { Returned = true, Changed = Next() };

        void Store(FalloutPlayerSceneScalarFields fields)
        {
            _sceneScalars = fields;
            _sceneScalarCall = _sceneScalarCall! with { After = fields, Stores = _sceneScalarCall.Stores + 1, Changed = Next() };
        }
    }

    private static void ValidateSceneScalars(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.SceneScalars != FalloutPlayerSceneScalarFields.Constructed)
            throw new InvalidDataException("Player scalar fields admitted an unowned nonconstructor Actor/Player writer.");
        if (saved.SceneScalarCall is not { } call)
        {
            if (saved.Prelude?.FailedAt is FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player scene queue lost its returned scalar stores.");
            return;
        }
        if (saved.Prelude is not { FailedAt: FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prefix ||
            saved.ActorProcessQuery is not { AnimationReturned: true, Failure: null } queries ||
            call.Main != prefix.Main || call.MainOrdinal != prefix.MainOrdinal || call.Entered <= queries.Changed ||
            call.Changed <= call.Entered || call.Changed > saved.Sequence || !call.Returned || call.Stores != 3 ||
            call.Before != FalloutPlayerSceneScalarFields.Constructed || call.After != saved.SceneScalars || call.ScaledTimerBits is not null)
            throw new InvalidDataException("Player scalar prefix lost its exact ordered stores, constructor values or zero-time branch.");
    }
}
