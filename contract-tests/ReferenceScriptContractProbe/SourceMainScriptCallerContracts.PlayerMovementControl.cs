using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerMovementControl()
    {
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        var source = FalloutPlayerMovementControlSource.Reviewed(main);
        var stack = new string('2', 64); var process = Guid.NewGuid(); var invocation = Guid.NewGuid();
        var input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null));
        var control = AuthoredMovementControl(source, stack, process);
        var initial = control.Capture();
        Require(initial is { CacheStores: 0, Call: null, Failure: null } && initial.Inhibition == FalloutPlayerControlInhibition.Constructed,
            "Player control construction replayed input or global cache stores.");
        Reject(() => input.ReadRelativeMouseAxis(1));
        input.Publish(new byte[256], new byte[8], 7, -11, -120);
        var before = input.Capture();
        Reject(() => ExecuteAuthoredMovement(control, invocation, 1, 0x3ca3d70a, 0xcc00, input));
        var failed = control.Capture();
        Require(failed is { CacheStores: 2, Failure: FalloutPlayerMovementControlState.ConfigurationBoundary } &&
            failed.Call is { Returned: false, Step: FalloutPlayerMovementControlStep.Configuration } call &&
            call.InputFactory == input.Factory && call.InputProvider == FalloutInputProvider.HeadlessCommands && call.InputWindow == 0 &&
            call.MouseReads.Select(read => read.Value).SequenceEqual(new[] { 7, -11, -120 }) &&
            call.MouseReads.Select(read => read.Axis).SequenceEqual(new uint[] { 1, 2, 3 }) &&
            input.Sample == before.Sample && input.Revision == before.Revision,
            "Player control prefix fabricated polling, skipped cache stores, lost axis signs/order or claimed its later source getters returned.");
        input.DeviceUnavailable("authored-unavailable-device");
        Require(input.ReadRelativeMouseAxis(1) == 7 && input.ReadRelativeMouseAxis(2) == -11 && input.ReadRelativeMouseAxis(3) == -120 &&
            input.ReadRelativeMouseAxis(0) == 0 && input.ReadRelativeMouseAxis(uint.MaxValue) == 0,
            "Relative mouse getter polled, cleared the retained cache or changed source out-of-range results.");
        var saved = JsonSerializer.Deserialize<FalloutPlayerMovementControlSnapshot>(JsonSerializer.Serialize(failed))!;
        var cold = AuthoredMovementControl(source, stack, Guid.NewGuid(), saved);
        var coldBefore = cold.Capture();
        Require(coldBefore.Factory != saved.Factory && coldBefore.CapturedProcess != saved.CapturedProcess &&
            coldBefore.CacheStores == saved.CacheStores && coldBefore.Call!.InputFactory == saved.Call!.InputFactory &&
            coldBefore.Call.MouseReads.SequenceEqual(saved.Call.MouseReads),
            "Cold Player controls promoted an old input factory or replayed cache/input operations.");
        Reject(() => ExecuteAuthoredMovement(cold, Guid.NewGuid(), 2, 0, 0, input));
        Require(cold.Capture().Sequence == coldBefore.Sequence, "Failed cold controls replayed their reached source prefix.");
        Reject(() => FalloutPlayerMovementControlState.Validate(saved with { CacheStores = 1 }));
        Reject(() => FalloutPlayerMovementControlState.Validate(saved with { Call = saved.Call! with { Returned = true } }));
        Reject(() => FalloutPlayerMovementControlState.Validate(saved with
        { Call = saved.Call! with { MouseReads = saved.Call.MouseReads.Reverse().ToArray() } }));
        Reject(() => AuthoredMovementControl(source, stack, saved.CapturedProcess, saved));
        var foreign = FalloutDirectInputState.CreateHeadless(new('3', 64), new(_ => null));
        var foreignControl = AuthoredMovementControl(source, stack, process);
        Reject(() => ExecuteAuthoredMovement(foreignControl, Guid.NewGuid(), 1, 0, 0, foreign));
        Require(foreignControl.Capture() is { CacheStores: 0, Call.InputFactory: null },
            "Foreign current input crossed the cache-clear boundary.");
        var unavailable = FalloutDirectInputState.CreateHeadless(stack, new(_ => null));
        var unavailableControl = AuthoredMovementControl(source, stack, process);
        Reject(() => ExecuteAuthoredMovement(unavailableControl, Guid.NewGuid(), 1, 0, 0, unavailable));
        Require(unavailableControl.Capture() is { CacheStores: 2, Call.Step: FalloutPlayerMovementControlStep.MouseAxes } missing &&
            missing.Call.MouseReads.Count == 0 && missing.Call.InputFactory == unavailable.Factory,
            "Unpublished input became zero axes or erased the preceding genuine cache stores.");
        var wrongArguments = AuthoredMovementControl(source, stack, process);
        Reject(() => ExecuteAuthoredMovement(wrongArguments, Guid.NewGuid(), 1, 0, 4, input));
        Require(wrongArguments.Capture() is { Sequence: 0, Call: null }, "Wrong source arguments entered Player controls.");
        Exception? threadFailure = null;
        var thread = new Thread(() => { try { control.Capture(); } catch (Exception error) { threadFailure = error; } });
        thread.Start(); thread.Join();
        Require(threadFailure is InvalidOperationException, "Foreign thread captured another actual Player control call.");
        control.Retire(); cold.Retire(); foreignControl.Retire(); unavailableControl.Retire(); wrongArguments.Retire();
        input.Retire(); foreign.Retire(); unavailable.Retire();
        RunPlayerControlConfigurationAndOrientation(source, stack);
        Console.WriteLine("OPENNV_PLAYER_MOVEMENT_CONTROL_PASS genuineCachedMouse=true orderedCacheStores=true noDevicePoll=true failedPrefix=true coldNoReplay=true laterGettersRefused=true gameplay=UNEXECUTED");
    }
}
