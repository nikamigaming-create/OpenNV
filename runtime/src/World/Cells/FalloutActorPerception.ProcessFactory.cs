using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPerception
{
    internal IReadOnlyList<FalloutFormKey> ConstructedSourceActors
    {
        get { RequireStable(); RequireIdle(); return _actors.Keys.ToArray(); }
    }
    internal void AdmitCurrentSourceProcessEpoch(FalloutFormKey actor, long epoch, FalloutDetectionProcessLevel current, string owner)
    {
        Mutate(() =>
        {
            RequireIdle(); var state = RequireActor(actor); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
            if (state.Retired || state.ProcessEpoch != epoch || state.Process is not { HasProcess: true } process || process.Level != current)
                throw new InvalidDataException("Current source process admission changed the actual actor/class/epoch.");
            state.ProcessBoundary = null; Next();
        });
    }
    internal FalloutPerceptionActorSnapshot CaptureSourceProcessActor(FalloutFormKey actor)
    {
        RequireStable(); RequireIdle();
        return ActorSnapshots().Single(value => value.Source.Reference == actor);
    }
    internal (FalloutDetectionCacheSnapshot Cache, FalloutDetectionLightSnapshot Light) ConstructSourceHighProcess(FalloutFormKey actor)
    {
        RequireStable(); RequireIdle(); _ = RequireActor(actor);
        return (new FalloutDetectionCache(actor, FalloutDetectionProcessLevel.High, _sourceActorBound, _selectedActor).Capture(),
            new FalloutDetectionLight(actor, _selectedActor).Capture());
    }

    internal void PublishSourceHighProcess(FalloutActorProcessFactorySnapshot factory)
    {
        Mutate(() =>
        {
            RequireIdle(); var state = RequireActor(factory.Actor);
            if (factory.Phase != FalloutActorProcessFactoryPhase.RetiredOld || factory.After != FalloutDetectionProcessLevel.High ||
                factory.Before == FalloutDetectionProcessLevel.High || state.Retired || state.ProcessEpoch != factory.BeforeEpoch ||
                factory.NewEpoch != checked(factory.BeforeEpoch + 1) || state.Source != factory.BeforePerception.Source ||
                state.Process?.Level != factory.Before || factory.NewCache is null || factory.NewLight is null)
                throw new InvalidDataException("Actual process publication lost its source copy/retirement/new-constructor prefix.");
            // Validate all new state before changing the published owner.
            var cache = new FalloutDetectionCache(factory.Actor, FalloutDetectionProcessLevel.High, _sourceActorBound, _selectedActor);
            cache.Restore(factory.NewCache, _seconds);
            var light = new FalloutDetectionLight(factory.Actor, _selectedActor); light.Restore(factory.NewLight);
            state.Cache = cache; state.Light = light; state.Process = new(true, FalloutDetectionProcessLevel.High);
            state.ProcessEpoch = factory.NewEpoch; state.ProcessBoundary = null;
            if (!_high.Contains(factory.Actor, FalloutFormKeyComparer.Instance)) _high.Add(factory.Actor);
            _processHistory.Add(new(factory.Actor, factory.NewEpoch, FalloutPerceptionProcessOperation.SourceFactoryRequest,
                factory.Before, factory.After, factory.Owner, Next()));
        });
    }

    // Called only at the actual High producer entry. The original clears its
    // two pending directions before even its early-return tests.
    internal void BeginOriginalHighProducer(FalloutFormKey actor, long epoch)
    {
        Mutate(() =>
        {
            RequireIdle(); var state = RequireActor(actor); RequireProcess(state);
            if (state.ProcessEpoch != epoch || state.Process!.Level != FalloutDetectionProcessLevel.High)
                throw new InvalidDataException("Original High producer belongs to a retired or non-High process.");
            state.Cache!.ClearPendingForOriginalProducer(); Next();
        });
    }
}
