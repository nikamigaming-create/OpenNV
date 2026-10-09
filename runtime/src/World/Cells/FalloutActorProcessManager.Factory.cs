using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessManager
{
    internal void EnsureHigh(FalloutFormKey actor, long epoch, string owner)
    {
        Operation(actor, owner, () =>
        {
            var state = RequireActor(actor); RequireEpoch(state, epoch);
            if (state.Retired || state.Level is null) throw new InvalidOperationException("Retired actor has no original High factory entry.");
            // Source already-High entry preserves its process epoch and caches.
            // Native 3D reconstruction is a separate operation, not a new tier.
            if (state.Level == FalloutDetectionProcessLevel.High) return;
            if (state.Construction is null)
                throw new NotSupportedException("Original actor constructor registration owner is absent.");
            if (state.Construction.Boundary() is { } constructionBoundary)
                throw new NotSupportedException(constructionBoundary);
            if (_factory is { Phase: not FalloutActorProcessFactoryPhase.Complete })
                throw new InvalidOperationException("Another actual actor process factory remains unfinished.");
            _factory = new(actor, epoch, checked(epoch + 1), state.Level.Value, FalloutDetectionProcessLevel.High,
                FalloutActorProcessSourceOperation.EnsureHigh, owner, FalloutActorProcessFactoryPhase.Created,
                _perception.CaptureSourceProcessActor(actor), null, null, null);
            Next(); ContinueFactory();
        });
    }

    // Explicit continuation requires the same real invocation and epoch. A
    // captured failure never silently restarts the already consumed prefix.
    internal void ContinueSourceFactory(FalloutFormKey actor, long beforeEpoch, string owner)
    {
        Operation(actor, owner, () =>
        {
            if (_factory is null || _factory.Actor != actor || _factory.BeforeEpoch != beforeEpoch || _factory.Owner != owner)
                throw new InvalidDataException("Process continuation belongs to another source invocation.");
            if (_factory.Failure is not null)
                throw new NotSupportedException("Failed process factory requires its original failed-owner recovery: " + _factory.Failure);
            ContinueFactory();
        });
    }

    private void ContinueFactory()
    {
        try
        {
            var next = _factory ?? throw new InvalidOperationException("No source process factory exists.");
            var state = RequireActor(next.Actor);
            if (next.Phase == FalloutActorProcessFactoryPhase.Created)
            {
                RequireEpoch(state, next.BeforeEpoch);
                if (state.Registered && !_cohort.Remove(next.Actor, next.Before))
                    throw new InvalidDataException("Factory old process lost its actual source registration.");
                _actors[next.Actor] = state with { Registered = false };
                SetFactory(FalloutActorProcessFactoryPhase.RemovedOldRegistration);
            }
            if (_factory!.Phase == FalloutActorProcessFactoryPhase.RemovedOldRegistration)
            {
                // Constructors own empty new detection/light state. The
                // original common copy must finish before the old owner dies.
                var parts = _perception.ConstructSourceHighProcess(next.Actor);
                _factory = _factory with { NewCache = parts.Cache, NewLight = parts.Light };
                SetFactory(FalloutActorProcessFactoryPhase.ConstructedNew);
            }
            if (_factory!.Phase == FalloutActorProcessFactoryPhase.ConstructedNew)
            {
                InvokeSourceOwner(() => _inputs.CopyCommon(_factory));
                SetFactory(FalloutActorProcessFactoryPhase.CopiedCommon);
            }
            if (_factory!.Phase == FalloutActorProcessFactoryPhase.CopiedCommon)
            {
                // Low/Middle processes have no detection cache to copy into
                // High. A foreign common field cannot be fabricated here.
                if (_factory.Before == FalloutDetectionProcessLevel.High)
                    throw new NotSupportedException("This reached factory is the actual non-High to High arm.");
                SetFactory(FalloutActorProcessFactoryPhase.CopiedPerception);
            }
            if (_factory!.Phase == FalloutActorProcessFactoryPhase.CopiedPerception)
            {
                InvokeSourceOwner(() => _inputs.RetireOld(_factory));
                SetFactory(FalloutActorProcessFactoryPhase.RetiredOld);
            }
            if (_factory!.Phase == FalloutActorProcessFactoryPhase.RetiredOld)
            {
                _perception.PublishSourceHighProcess(_factory);
                _actors[next.Actor] = RequireActor(next.Actor) with
                { Epoch = next.NewEpoch, Level = next.After, Boundary = null, DetectionTimer = 0f,
                    DetectionGeneration = 0, DetectionUpdated = false, LastTimerOwner = "original-high-constructor-positive-zero" };
                SetFactory(FalloutActorProcessFactoryPhase.PublishedNew);
            }
            if (_factory!.Phase == FalloutActorProcessFactoryPhase.PublishedNew)
            {
                if (next.Actor != _player)
                {
                    _cohort.Add(next.Actor, next.After);
                    _actors[next.Actor] = RequireActor(next.Actor) with { Registered = true };
                }
                SetFactory(FalloutActorProcessFactoryPhase.RegisteredNew);
            }
            if (_factory!.Phase == FalloutActorProcessFactoryPhase.RegisteredNew)
            {
                InvokeSourceOwner(() => _inputs.InitializeNew(_factory));
                SetFactory(FalloutActorProcessFactoryPhase.InitializedNew);
            }
            if (_factory!.Phase == FalloutActorProcessFactoryPhase.InitializedNew)
                SetFactory(FalloutActorProcessFactoryPhase.Complete);
        }
        catch (Exception error)
        {
            if (_factory is not null) _factory = _factory with
            { Failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message };
            throw;
        }
    }
    private void SetFactory(FalloutActorProcessFactoryPhase phase)
    { _factory = _factory! with { Phase = phase }; Next(); }
    private void InvokeSourceOwner(Action invoke)
    {
        var faults = _faults.Count; invoke();
        if (_faults.Count != faults) throw new InvalidOperationException("Source process child owner caught an actual reentry failure.");
    }
}
