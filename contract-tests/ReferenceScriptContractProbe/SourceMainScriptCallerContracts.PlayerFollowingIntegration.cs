using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void BindMovementIntegrationFollowing(FalloutStandalonePlayerSceneState scene,
        FalloutActorProcessCommonState common, FalloutActorProcessRuntimeState actor, FalloutFormKey player, bool originalInput)
    {
        var source = FalloutPlayerFollowingContinuationSource.Reviewed(
            FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
        var consumers = new MovementFollowingConsumers(scene, common, actor, player, originalInput);
        scene.BindPlayerFollowingContinuation(source, (caller, observe) =>
            FalloutPlayerFollowingContinuation.Execute(source, scene.Stack, scene.Process, caller, consumers, observe));
    }

    private static void RequireMovementIntegrationFollowing(FalloutStandalonePlayerSceneSnapshot saved, bool originalInput)
    {
        var call = saved.FollowingContinuation!;
        var movement = saved.MovementCall!.Control!.Call!;
        var inhibition = saved.Inhibition!;
        Require(call is { Step: FalloutPlayerFollowingContinuationStep.Failed,
            Inhibited: false,
            InhibitionReads.Count: 4, Life: { Raw.Value: 0, Result: false },
            CurrentClearSelected: false, PrimaryClearSelected: false, Timer.Value: 0, ScalarSkipped: true } &&
            call.Caller.Invocation == saved.WeaponContinuation!.Invocation &&
            call.InhibitionReads.All(read => read.Factory == inhibition.Factory) &&
            movement.InhibitionReads is { Count: 4 } movementReads && movementReads.All(read => read.Factory == inhibition.Factory) &&
            movement.Invocation == saved.Prelude!.Invocation &&
            call.Current!.Fields.Publication.Factory != call.Primary!.Fields.Publication.Factory,
            "Following Player services lost shared fields, actual caller or independent package factories.");
        Require(call.FailedAt == (originalInput ? FalloutPlayerFollowingContinuationStep.ReadyItemGlobals :
                FalloutPlayerFollowingContinuationStep.ReadyItem) &&
            call.Failure == (originalInput ? FalloutPlayerFollowingContinuation.ReadyItemGlobalsBoundary :
                FalloutPlayerFollowingContinuation.ReadyItemBoundary),
            "Following Player service promoted its next unowned effect or lost its exact input boundary.");
        if (originalInput)
        {
            Require(call.ReadyItemQueries is { Count: 3 } queries && queries.Select(query => query.Mode).SequenceEqual([1, 0, 2]) &&
                queries.All(query => query is { Step: FalloutOriginalInputQueryStep.Returned, Count: 0, Failure: null,
                    Observations.Count: 1 } && query.Factory == saved.InputManagerQuery!.Factory),
                "Actual source input queries were skipped, replayed or replaced with pressed/Boolean aliases.");
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
            { FollowingContinuation = call with { ReadyItemQueries = [] } }));
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
            { FollowingContinuation = call with { ReadyItemQueries = call.ReadyItemQueries!.Select(query => query with { Mode = 3 }).ToArray() } }));
        }
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { FollowingContinuation = call with { Caller = call.Caller with { Invocation = Guid.NewGuid() } } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        {
            FollowingContinuation = call with
            { InhibitionReads = call.InhibitionReads.Select(read => read with { Factory = Guid.NewGuid() }).ToArray() }
        }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { FollowingContinuation = call with { Timer = call.Timer! with { Factory = Guid.NewGuid() } } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { Prelude = saved.Prelude! with { Invocation = Guid.NewGuid() } }));
        var package = call.Current!;
        foreach (var words in new[]
        {
            package.Fields.Fields with { Procedure = Guid.NewGuid() },
            package.Fields.Fields with { Extra = 1 },
            package.Fields.Fields with { Index = 0 },
            package.Fields.Fields with { TimeBits = 0 },
            package.Fields.Fields with { Flags = 1 }
        })
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
            { FollowingContinuation = call with { Current = package with { Fields = package.Fields with { Fields = words } } } }));
        Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(saved));
    }

    private static void RequireMovementIntegrationFollowingHistory(FalloutStandalonePlayerSceneSnapshot saved)
    {
        FalloutStandalonePlayerSceneState.Validate(saved);
        var call = saved.FollowingContinuation!;
        var read = call.InhibitionReads[0];
        var publications = saved.Inhibition!.Publications!;
        Require(call.PlayerProcess != saved.CapturedProcess && publications.Any(item =>
            item.Process == read.Process && item.Factory == read.Factory) &&
            publications[^1].Process == saved.CapturedProcess,
            "Cold following history lost its exact prior process/factory association.");
        var foreign = Guid.NewGuid();
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { ReferenceQuery = saved.ReferenceQuery! with { Process = foreign } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { ThreadContextEntry = saved.ThreadContextEntry! with { Process = foreign } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        {
            FollowingContinuation = call with
            { InhibitionReads = call.InhibitionReads.Select(item => item with { Factory = foreign }).ToArray() }
        }));
        Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(saved));
    }

    private sealed class MovementFollowingConsumers(FalloutStandalonePlayerSceneState scene,
        FalloutActorProcessCommonState common, FalloutActorProcessRuntimeState actor, FalloutFormKey player, bool originalInput)
        : IFalloutPlayerFollowingContinuationConsumers, IFalloutPlayerReadyItemConsumers
    {
        public void RequireCurrent(FalloutPlayerSourceCaller caller) => scene.RequireFollowingContinuationInvocation(caller);
        public FalloutPlayerInhibitionPredicate ReadInhibition(FalloutPlayerSourceCaller caller,
            Action<FalloutPlayerInhibitionRead> observe) => scene.ReadFollowingInhibition(caller, observe);
        public FalloutPlayerFollowingLifeRead ReadLife(bool argument)
        {
            var raw = actor.Life(player);
            return new(common.ReadSceneQueries(player, 1), raw, argument,
                FalloutPlayerFollowingContinuation.LifePredicate(raw.Require(), argument));
        }
        public FalloutProcessPackageRead ReadCurrentPackage() =>
            common.ReadSourcePackageSlot(player, 1, FalloutProcessPackageSlot.Current);
        public FalloutProcessPackageRead ReadPrimaryPackage() =>
            common.ReadSourcePackageSlot(player, 1, FalloutProcessPackageSlot.Primary);
        public FalloutPlayerFollowingTimerFlagRead ReadTimerByte(FalloutPlayerSourceCaller caller) =>
            scene.ReadFollowingTimerByte(caller);
        public FalloutOriginalInputQuery ReadReadyItem(FalloutPlayerSourceCaller caller, int mode,
            Action<FalloutOriginalInputQuery> observe) => originalInput
            ? scene.ReadFollowingSourceControl(caller, FalloutPlayerFollowingContinuationSource.ReadyItemControl, mode, observe)
            : throw new NotSupportedException(FalloutPlayerFollowingContinuation.ReadyItemBoundary);
    }

    private static void BindMovementIntegrationOriginalInput(FalloutStandalonePlayerSceneState scene, FalloutDirectInputState input)
    {
        var source = FalloutOriginalInputSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
        input.Controls.Set(FalloutPlayerFollowingContinuationSource.ReadyItemControl, 0x13);
        input.BindSourceControlQueries(source, new MovementOriginalInputPlatform(), scene.RequireOriginalInputInvocation,
            FalloutOriginalInputHookBinding.ManagedControls(input));
        input.PollSourceDevices(new MovementOriginalInputDevices(input));
    }

    // Explicit authored CPU/device transport; owned and native checks qualify
    // their separate platform lanes. This fixture never supplies product input.
    private sealed class MovementOriginalInputPlatform : IFalloutOriginalInputPlatform
    {
        public Guid Factory { get; } = Guid.NewGuid();
        public string Owner => "authored-following-input-platform";
        public void RequireCurrent() { }
        public void ConstructMouse(Action<int> storeSwap, Action<uint> storeDoubleClickMilliseconds)
        { storeSwap(0); storeDoubleClickMilliseconds(500); }
        public int ReadControllerState(Span<byte> destination) => 1167;
    }
    private sealed class MovementOriginalInputDevices(FalloutDirectInputState input) : IFalloutOriginalInputDevices
    {
        public bool Present(FalloutOriginalInputDevice device) => true;
        public void RequireCurrent(FalloutDirectInputState owner)
        {
            if (!ReferenceEquals(input, owner.ReadManagerOwner())) throw new InvalidOperationException("Authored input changed current ownership.");
        }
        public int Acquire(FalloutOriginalInputDevice device) => 0;
        public int Poll(FalloutOriginalInputDevice device) => 0;
        public int ReadState(FalloutOriginalInputDevice device, Span<byte> destination) { destination.Clear(); return 0; }
    }
}
