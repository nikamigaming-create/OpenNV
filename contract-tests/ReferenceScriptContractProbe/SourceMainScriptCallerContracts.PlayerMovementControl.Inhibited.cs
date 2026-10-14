using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunInhibitedPlayerMovementControl(FalloutPlayerMovementControlSource source, string stack)
    {
        var process = Guid.NewGuid();
        FalloutPlayerSourceCaller? entered = null;
        var inhibition = new FalloutPlayerInhibitionState(FalloutPlayerFollowingContinuationSource.Reviewed(source), stack,
            process, Guid.NewGuid(), Guard);
        var actor = new FalloutCombatActorIdentity(new("authored.esm", 0x14), new("authored.esm", 7),
            "ENGINE_PLAYER", 0, new('1', 64), "NPC_", 0, new('2', 64), true);
        var command = new FalloutPackageCommandFieldInvocation(Guid.NewGuid(), actor, Guid.NewGuid(), 1, Environment.CurrentManagedThreadId);
        inhibition.StorePackageAssignmentFourth(command, actual =>
        {
            if (actual != command || actual.ThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Authored Fourth writer lost its actual component scope.");
        });
        // No published sample, configuration or orientation provider exists.
        // The real inhibited branch must not attempt any of those getters.
        var input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null));
        var controls = new FalloutPlayerMovementControlState(source, stack, process, inhibition: inhibition);
        var beforeInput = (input.Sample, input.Revision, input.NativeReaders, input.Failure);
        Reject(() => input.Capture());
        Call(controls, 1);
        var saved = controls.Capture();
        Require(saved is { Failure: null, CacheStores: 0, OrientationStores: 0,
            Call: { Returned: true, Result: false, Inhibited: true, Step: FalloutPlayerMovementControlStep.Returned,
                InputFactory: null, Configuration: null, FieldsRead: null, Axes: null, WorldFov: null, Yaw: null, Pitch: null } } &&
            saved.Call.InhibitionReads is { Count: 4 } reads && reads.Select(read => read.Value).SequenceEqual(new byte[] { 0, 0, 0, 1 }) &&
            (input.Sample, input.Revision, input.NativeReaders, input.Failure) == beforeInput,
            "Inhibited controls failed their source false return, read a skipped provider or fabricated input/cache/orientation work.");
        Reject(() => input.Capture());
        Reject(() => FalloutPlayerMovementControlState.Validate(saved with
        { Call = saved.Call! with { InhibitionReads = saved.Call!.InhibitionReads!.Select(read => read with { Value = 0 }).ToArray(), Inhibited = false } }));
        Reject(() => FalloutPlayerMovementControlState.Validate(saved with { Call = saved.Call! with { InputFactory = input.Factory } }));
        Reject(() => FalloutPlayerMovementControlState.Validate(saved with { Call = saved.Call! with { CacheStores = 2 }, CacheStores = 2 }));
        Reject(() => FalloutPlayerMovementControlState.Validate(saved with { Call = saved.Call! with { Result = true } }));
        Reject(() => FalloutPlayerMovementControlState.Validate(saved with { Call = saved.Call! with { Inhibited = null } }));
        Call(controls, 2);
        Require(controls.Capture() is { CacheStores: 0, OrientationStores: 0, Call: { MainOrdinal: 2, Returned: true } },
            "A legitimate next inhibited Main replayed work or could not reuse its current byte owner.");
        saved = controls.Capture();
        controls.Retire(); inhibition.Retire();
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            var history = JsonSerializer.Serialize(saved.Call);
            var retained = JsonSerializer.Deserialize<FalloutPlayerMovementControlSnapshot>(JsonSerializer.Serialize(saved))!;
            process = Guid.NewGuid();
            inhibition = new(FalloutPlayerFollowingContinuationSource.Reviewed(source), stack, process, Guid.NewGuid(), Guard, retained.InhibitionOwner);
            controls = new(source, stack, process, retained, inhibition: inhibition);
            var cold = controls.Capture();
            Require(cold.Factory != saved.Factory && cold.CapturedProcess != saved.CapturedProcess &&
                JsonSerializer.Serialize(cold.Call) == history && cold.CacheStores == 0 && cold.OrientationStores == 0,
                "Cold inhibited controls replayed the prior return or changed retained call/provider history.");
            Call(controls, saved.Call!.MainOrdinal + 1);
            saved = controls.Capture();
            Require(saved.Call!.Process == process && saved.Call.InhibitionReads!.All(read => read.Process == process && read.Factory == inhibition.Factory) &&
                saved.CacheStores == 0 && saved.OrientationStores == 0 &&
                (input.Sample, input.Revision, input.NativeReaders, input.Failure) == beforeInput,
                "Cold inhibited controls borrowed a historical field lifetime or executed skipped input work.");
            Reject(() => input.Capture());
            controls.Retire(); inhibition.Retire();
        }
        input.Retire();
        Console.WriteLine("OPENNV_PLAYER_INHIBITED_CONTROL_PASS actualFourth=true falseReturn=true noLaterProvidersOrStores=true repeat=true coldNoReplay=true gameplay=UNEXECUTED");

        void Guard(FalloutPlayerInhibitionConsumer consumer, FalloutPlayerSourceCaller caller)
        {
            if (consumer != FalloutPlayerInhibitionConsumer.Movement || entered != caller)
                throw new InvalidOperationException("Authored inhibited controls lost their actual entered caller.");
        }
        void Call(FalloutPlayerMovementControlState owner, long ordinal)
        {
            entered = new(Guid.NewGuid(), ordinal, Guid.NewGuid());
            try { Require(!owner.ExecutePrefix(entered.Main, ordinal, 0x3ca3d70a, 0xcc00, input, invocation: entered.Invocation),
                "Inhibited controls changed the source existing-local false return."); }
            finally { entered = null; }
        }
    }
}
