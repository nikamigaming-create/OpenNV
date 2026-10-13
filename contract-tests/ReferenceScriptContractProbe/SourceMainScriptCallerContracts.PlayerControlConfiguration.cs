using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerControlConfigurationAndOrientation(FalloutPlayerMovementControlSource source, string stack)
    {
        Require(FalloutPlayerControlFieldsState.CommandMask(new(false, [])) == 0x0d &&
            FalloutPlayerControlFieldsState.CommandMask(new(true, [])) == 0x7f,
            "FO3 control commands changed their omitted-argument defaults or independent mask mapping.");
        var disabled = new FalloutPlayerControlCommand(false, []).ApplyStandalone(FalloutPlayerControlState.AllEnabled);
        Require(disabled is { Movement: false, PipBoy: false, Fighting: false, PointOfView: true,
            Looking: true, RolloverText: true, Sneaking: true } &&
            new FalloutPlayerControlCommand(true, []).ApplyStandalone(disabled) == FalloutPlayerControlState.AllEnabled &&
            !new FalloutPlayerControlCommand(false, [false, false, false, true]).ApplyStandalone(FalloutPlayerControlState.AllEnabled).PointOfView,
            "FO3 semantic input state disagreed with the source command defaults or explicit POV argument.");
        RunObservedPlayerControlCommands(source, stack);
        var input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null)); input.Publish(new byte[256], new byte[8], 0, 0, 0);
        var current = new FalloutPlayerMovementControlState(source, stack, Guid.NewGuid());
        current.BindConfiguration(Ini(invert: true));
        Reject(() => current.ExecutePrefix(Guid.NewGuid(), 1, 0x3ca3d70a, 0, input));
        var missing = current.Capture();
        Require(missing is { Failure: FalloutPlayerMovementControlState.WorldFovBoundary,
            Call: { Configuration.InvertY.Number: 1, FieldsRead.DisabledWord: 0, Axes: { X: 0, Y: 0, Wheel: 0 }, WorldFov: null } },
            "Zero cached axes bypassed actual layered configuration, independent Player fields or world-projection getter.");
        var completed = new FalloutPlayerMovementControlState(source, stack, Guid.NewGuid());
        completed.BindConfiguration(Ini(invert: false)); var main = Guid.NewGuid();
        var owner = new AuthoredControlOrientation(source, stack, completed.Capture().CapturedProcess, main);
        completed.BindOrientation(owner);
        Require(!completed.ExecutePrefix(main, 1, 0x3ca3d70a, 0, input), "Actual zero-axis source control child changed its false return.");
        var returned = completed.Capture();
        Require(owner.Reads.SequenceEqual(new[] { "fov", "yaw", "pitch" }) && returned is
            { Failure: null, OrientationStores: 2, Call: { Returned: true, Result: false, Step: FalloutPlayerMovementControlStep.Returned } } &&
            returned.Call.FovScaleBits == BitConverter.SingleToUInt32Bits(42f / 75f) &&
            returned.YawCache == BitConverter.SingleToUInt32Bits(0.75f) && returned.PitchCache == BitConverter.SingleToUInt32Bits(-0.25f),
            "Zero-axis Boolean return omitted actual FOV/yaw/pitch reads, changed raw source orientation or skipped ordered cache stores.");
        Reject(() => FalloutPlayerMovementControlState.Validate(returned with { Call = returned.Call! with { FovScaleBits = 0 } }));
        Reject(() => FalloutPlayerMovementControlState.Validate(returned with { Call = returned.Call! with { Axes = new(1, 0, 0) } }));
        Reject(() => FalloutPlayerMovementControlState.Validate(returned with { OrientationStores = 1 }));
        var saved = JsonSerializer.Deserialize<FalloutPlayerMovementControlSnapshot>(JsonSerializer.Serialize(returned))!;
        var cold = new FalloutPlayerMovementControlState(source, stack, Guid.NewGuid(), saved);
        Require(owner.Reads.Count == 3 && cold.Capture().Call == saved.Call && cold.Capture().Fields!.Factory != saved.Fields!.Factory,
            "Cold controls replayed historical world/reference reads or promoted the previous Player field factory.");
        RunReturnedPlayerControlColdPublication();
        var suppressed = new FalloutPlayerMovementControlState(source, stack, Guid.NewGuid());
        suppressed.BindConfiguration(Ini(invert: true));
        var effects = new AuthoredControlFlagEffects(source, stack, suppressed.Capture().CapturedProcess);
        suppressed.Fields.ApplyCommand(Guid.NewGuid(), "authored-source-script", new(false, [false, false, false, false, true, false, false]), effects);
        Require(effects.Calls.SequenceEqual(new[] { "input:1" }) && suppressed.Fields.ReadDisabledWord() == 2,
            "Looking flag copied host controls or omitted the genuine input-type service after its stores.");
        input.Publish(new byte[256], new byte[8], 11, int.MinValue, 120);
        var suppressionMain = Guid.NewGuid();
        var suppressedOwner = new AuthoredControlOrientation(source, stack, suppressed.Capture().CapturedProcess, suppressionMain);
        suppressed.BindOrientation(suppressedOwner);
        Require(!suppressed.ExecutePrefix(suppressionMain, 1, 0, 0, input) && suppressed.Capture().Call!.Axes == new FalloutPlayerControlAxes(0, 0, 0),
            "Independent looking-disable bit failed to suppress all three axes after source inversion.");
        var inverted = new FalloutPlayerMovementControlState(source, stack, Guid.NewGuid()); inverted.BindConfiguration(Ini(invert: true));
        input.Publish(new byte[256], new byte[8], 0, int.MinValue, 0);
        var invertedMain = Guid.NewGuid(); var invertedOwner = new AuthoredControlOrientation(source, stack, inverted.Capture().CapturedProcess, invertedMain);
        inverted.BindOrientation(invertedOwner); Reject(() => inverted.ExecutePrefix(invertedMain, 1, 0, 0, input));
        Require(inverted.Capture() is { Failure: FalloutPlayerMovementControlState.MotionBoundary,
            OrientationStores: 2, Call: { Axes.Y: int.MinValue, Returned: false } },
            "Source Int32 invert-Y overflow changed its wrapped value or bypassed later orientation writer ownership.");
        var failedFlags = new FalloutPlayerControlFieldsState(source, stack, Guid.NewGuid());
        Reject(() => failedFlags.ApplyCommand(Guid.NewGuid(), "authored-source-script", new(false, [])));
        var failedFieldSnapshot = failedFlags.Capture();
        Require(failedFieldSnapshot is { DisabledWord: 0x0d, FlagStores: 2, Failure: FalloutPlayerControlFieldsState.InputTypeBoundary,
            Call: { Step: FalloutPlayerControlFlagStep.InputType, EffectsFactory: null, Returned: false, InputType: 12 } },
            "Unbound source flag services erased preceding real stores or claimed a complete returned script command.");
        var fieldCold = new FalloutPlayerControlFieldsState(source, stack, Guid.NewGuid(), failedFieldSnapshot);
        Reject(() => fieldCold.ReadDisabledWord());
        Require(fieldCold.Capture().FlagStores == 2, "Cold failed flag writer replayed its actual stores.");
        var poisoned = new FalloutPlayerControlFieldsState(source, stack, Guid.NewGuid());
        var foreignEffects = new AuthoredControlFlagEffects(source, stack, Guid.NewGuid());
        Reject(() => poisoned.ApplyCommand(Guid.NewGuid(), "authored-source-script", new(true, []), foreignEffects));
        Require(poisoned.Capture().Call!.EffectsFactory is null && foreignEffects.Calls.Count == 0,
            "Foreign Player flag effects admitted input or Actor services.");
        var tilt = owner.ReadCurrentReferenceYaw(main, 1) with { ReferenceYawBits = BitConverter.SingleToUInt32Bits(0.5f),
            ReferenceRollBits = BitConverter.SingleToUInt32Bits(0.25f), BaseFlags = FalloutPlayerControlOrientation.NpcTiltYaw };
        Require(FalloutPlayerControlOrientation.Yaw(tilt, 0) == BitConverter.SingleToUInt32Bits(0.75f) &&
            FalloutPlayerControlOrientation.Yaw(tilt with { BaseFlags = 0 }, 0) == BitConverter.SingleToUInt32Bits(0.5f),
            "NPC source tilt flag changed its separate reference-field addition.");
        var veryNegative = tilt with { ReferenceYawBits = BitConverter.SingleToUInt32Bits(-20f), BaseFlags = 0 };
        Require(BitConverter.UInt32BitsToSingle(FalloutPlayerControlOrientation.Yaw(veryNegative, 0)) < 0,
            "Source angle signed remainder was replaced by a positive modulo.");
        current.Retire(); completed.Retire(); cold.Retire(); suppressed.Retire(); inverted.Retire(); input.Retire();
        Console.WriteLine("OPENNV_PLAYER_CONTROL_CONFIGURATION_PASS typedIni=true actualFlags=true orderedWriterEffects=true zeroAxesReadFovYawPitch=true coldNoReplay=true currentRuntimeProducers=UNBOUND gameplay=UNEXECUTED");

        FalloutNumericIniSettings Ini(bool invert) => new(
            [new(FalloutPlayerControlConfiguration.InvertYName, FalloutIniCollection.Prefs, 0),
                new(FalloutPlayerControlConfiguration.WorldFovName, FalloutIniCollection.Main, FalloutPlayerControlConfiguration.WorldFovDefaultBits)],
            [new("authored-source-Prefs-layer", FalloutIniCollection.Prefs, [new("Controls", "bInvertYValues", invert ? "1" : "0")])],
            stack + ":authored-owned-executable:" + source.Main.EngineSha256);
    }

    private static void RunReturnedPlayerControlColdPublication()
    {
        var stack = new string('2', 64);
        using var warm = new StandaloneFixture(authoredStack: stack);
        var source = FalloutPlayerMovementControlSource.Reviewed(warm.Host.Source);
        var initial = warm.Owner.Capture();
        var warmInput = FalloutDirectInputState.CreateHeadless(stack, new(_ => null));
        warmInput.Publish(new byte[256], new byte[8], 0, 0, 0);
        var warmControl = new FalloutPlayerMovementControlState(source, stack, initial.CapturedProcess,
            fields: warm.Owner.StandalonePlayerScene.ControlFields);
        warmControl.Fields.BindActualInput(warmInput);
        warmControl.BindConfiguration(Settings(false));
        var warmOrientation = new MainInvocationControlOrientation(source, stack, initial.CapturedProcess, warm.Owner);
        warmControl.BindOrientation(warmOrientation);
        warm.Host.DuringPlayer = invocation => warmOrientation.Enter(invocation,
            () => Require(!warmControl.ExecutePrefix(invocation.Identity, invocation.Ordinal, 0x3ca3d70a, 0, warmInput),
                "Warm current Main changed the source zero-axis return."));
        using (warm.Bind()) warm.Owner.ExecuteMainScriptCaller(71, .02f).GetAwaiter().GetResult();
        var saved = JsonSerializer.Deserialize<FalloutPlayerMovementControlSnapshot>(JsonSerializer.Serialize(warmControl.Capture())) ??
            throw new InvalidDataException("Returned controls omitted their serialized cold state.");
        var history = JsonSerializer.Serialize(saved.Call);
        using var cold = new StandaloneFixture(warm.Owner.Capture(), warm.Fade.Capture(), authoredStack: stack);
        var process = cold.Owner.Capture().CapturedProcess;
        var input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null));
        input.Publish(new byte[256], new byte[8], 0, 0, 0);
        var control = new FalloutPlayerMovementControlState(source, stack, process, saved, cold.Owner.StandalonePlayerScene.ControlFields);
        control.Fields.BindActualInput(input);
        var orientation = new MainInvocationControlOrientation(source, stack, process, cold.Owner);
        Reject(() => control.BindOrientation(warmOrientation));
        Reject(() => control.BindOrientation(new AuthoredControlOrientation(source, stack + "-foreign", process, Guid.NewGuid())));
        Reject(() => control.BindConfiguration(Settings(true)));
        Exception? threadFailure = null;
        var thread = new Thread(() =>
        {
            try { control.BindOrientation(orientation); }
            catch (Exception error) { threadFailure = error; }
        });
        thread.Start(); thread.Join();
        Require(threadFailure is InvalidOperationException, "Cold orientation publication acquired a foreign thread.");
        control.BindOrientation(orientation); control.BindConfiguration(Settings(false));
        Reject(() => control.BindOrientation(orientation));
        Reject(() => control.BindConfiguration(Settings(false)));
        Require(JsonSerializer.Serialize(control.Capture().Call) == history && orientation.Reads.Count == 0 &&
            warmOrientation.Reads.Count == 3 && control.Capture().CacheStores == saved.CacheStores,
            "Cold dependency publication mutated the historical call or replayed input/orientation stores.");
        var rejectedInput = new FalloutPlayerMovementControlState(source, stack, process, saved, control.Fields);
        cold.Host.DuringPlayer = invocation => orientation.Enter(invocation, () =>
        {
            Reject(() => rejectedInput.ExecutePrefix(invocation.Identity, invocation.Ordinal, 0x3ca3d70a, 0, warmInput));
            Require(!control.ExecutePrefix(invocation.Identity, invocation.Ordinal, 0x3ca3d70a, 0, input),
                "Cold current Main changed the source zero-axis return.");
        });
        using (cold.Bind()) cold.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult();
        var returned = control.Capture();
        var call = returned.Call ?? throw new InvalidDataException("Cold current controls omitted their returned call.");
        var currentMain = cold.Owner.CaptureMainScriptCaller().LastCall ??
            throw new InvalidDataException("Cold control call omitted the actual current Main parent.");
        var fov = call.WorldFov ?? throw new InvalidDataException("Cold current control return omitted its genuine FOV getter.");
        var readFields = call.FieldsRead ?? throw new InvalidDataException("Cold current control return omitted its genuine Player fields.");
        Require(call is { Returned: true, Result: false, Failure: null, MainOrdinal: 2 } &&
            call.Main == currentMain.Invocation && call.Process == process && call.InputFactory == input.Factory &&
            readFields.Process == process && fov.Lease.Process == process &&
            fov.Lease.Factory == orientation.Factory && returned.Factory != saved.Factory &&
            returned.CacheStores == saved.CacheStores + 2 && returned.OrientationStores == saved.OrientationStores + 2 &&
            currentMain.DeliveryFrame == 1 && orientation.Reads.SequenceEqual(new[] { "fov", "yaw", "pitch" }) &&
            warmOrientation.Reads.Count == 3 && JsonSerializer.Serialize(saved.Call) == history,
            "Cold controls reused a historical input/process/getter, lost source Main order or replayed the old call.");
        var failed = rejectedInput.Capture();
        Require(failed.Call is { Returned: false, Step: FalloutPlayerMovementControlStep.InputOwner, InputFactory: null } &&
            failed.CacheStores == saved.CacheStores && failed.Configuration == saved.Configuration,
            "Historical input crossed the fresh current field publication or erased retained configuration.");
        Reject(() => rejectedInput.BindConfiguration(Settings(false)));
        Reject(() => rejectedInput.BindOrientation(orientation));
        Reject(() => control.BindConfiguration(Settings(false)));
        Reject(() => control.BindOrientation(orientation));
        var sequence = returned.Sequence;
        Reject(() => control.ExecutePrefix(Guid.NewGuid(), call.MainOrdinal, 0, 0, input));
        Reject(() => control.ExecutePrefix(call.Main, call.MainOrdinal + 1, 0, 0, input));
        Require(control.Capture().Sequence == sequence, "A replayed current Main entered controls or changed its completed receipt.");
        cold.Host.DuringPlayer = invocation => orientation.Enter(invocation,
            () => Require(!control.ExecutePrefix(invocation.Identity, invocation.Ordinal, 0x3ca3d70a, 0, input),
                "The next actual current Main could not reuse its published control dependencies."));
        using (cold.Bind()) cold.Owner.ExecuteMainScriptCaller(2, .02f).GetAwaiter().GetResult();
        Require(control.Capture() is { Call: { Returned: true, MainOrdinal: 3 }, CacheStores: 6, OrientationStores: 6 } &&
            orientation.Reads.Count == 6, "Fresh binding was reopened or legitimate current Main calls were blocked.");
        warmControl.Retire(); control.Retire(); rejectedInput.Retire(); warmInput.Retire(); input.Retire();
        Console.WriteLine("OPENNV_PLAYER_CONTROL_COLD_PUBLICATION_PASS actualMain=true freshInputAndOrientation=true " +
            "historicalCallUnchanged=true bindingEndsOnEntry=true failedCurrentRebindRefused=true gameplay=UNEXECUTED");

        FalloutNumericIniSettings Settings(bool inverted) => new(
            [new(FalloutPlayerControlConfiguration.InvertYName, FalloutIniCollection.Prefs, 0),
                new(FalloutPlayerControlConfiguration.WorldFovName, FalloutIniCollection.Main, FalloutPlayerControlConfiguration.WorldFovDefaultBits)],
            [new("authored-source-Prefs-layer", FalloutIniCollection.Prefs, [new("Controls", "bInvertYValues", inverted ? "1" : "0")])],
            stack + ":authored-owned-executable:" + source.Main.EngineSha256);
    }

    private sealed class MainInvocationControlOrientation(FalloutPlayerMovementControlSource source, string stack,
        Guid process, FalloutActorProcessRuntimeState mainOwner) : IFalloutPlayerControlOrientationOwner
    {
        public FalloutPlayerMovementControlSource Source => source;
        public string Stack => stack;
        public Guid Process => process;
        public Guid Factory { get; } = Guid.NewGuid();
        internal List<string> Reads { get; } = [];
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private FalloutMainScriptInvocation? _current;
        internal void Enter(FalloutMainScriptInvocation invocation, Action call)
        {
            RequireCurrent(); invocation.Require(FalloutMainScriptCallerStep.Player);
            if (_current is not null || !ReferenceEquals(invocation.Owner, mainOwner) || invocation.Process != process)
                throw new InvalidOperationException("Authored control orientation lost its actual current Main owner.");
            _current = invocation;
            try { call(); }
            finally { _current = null; }
        }
        public void RequireCurrent()
        {
            if (_thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Authored current Main orientation changed its thread.");
        }
        public void RequireCurrent(Guid main, long ordinal)
        {
            RequireCurrent();
            var current = _current ?? throw new InvalidOperationException("Authored orientation has no actual entered Main child.");
            current.Require(FalloutMainScriptCallerStep.Player);
            if (current.Identity != main || current.Ordinal != ordinal || current.Process != process)
                throw new InvalidOperationException("Authored orientation borrowed a foreign Main invocation.");
        }
        public FalloutPlayerControlWorldFovRead ReadCurrentWorldFov(Guid main, long ordinal)
        { RequireCurrent(main, ordinal); Reads.Add("fov"); return new(Lease(main, ordinal), BitConverter.SingleToUInt32Bits(42f)); }
        public FalloutPlayerControlYawRead ReadCurrentReferenceYaw(Guid main, long ordinal)
        { RequireCurrent(main, ordinal); Reads.Add("yaw"); return new(Lease(main, ordinal), BitConverter.SingleToUInt32Bits(0.75f), 0, true, new("authored.esm", 7), "NPC_", 0); }
        public FalloutPlayerControlPitchRead ReadCurrentReferencePitch(Guid main, long ordinal)
        { RequireCurrent(main, ordinal); Reads.Add("pitch"); return new(Lease(main, ordinal), BitConverter.SingleToUInt32Bits(-0.25f)); }
        private FalloutPlayerControlReadLease Lease(Guid main, long ordinal) =>
            new(source.Identity, stack, process, Factory, main, ordinal, 0, "authored-actual-current-Main-fields");
    }

    private sealed class AuthoredControlOrientation(FalloutPlayerMovementControlSource source, string stack,
        Guid process, Guid main) : IFalloutPlayerControlOrientationOwner
    {
        public FalloutPlayerMovementControlSource Source => source;
        public string Stack => stack;
        public Guid Process => process;
        public Guid Factory { get; } = Guid.NewGuid();
        internal List<string> Reads { get; } = [];
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public void RequireCurrent()
        {
            if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Authored orientation changed its actual thread.");
        }
        public void RequireCurrent(Guid invocation, long ordinal)
        {
            RequireCurrent();
            if (invocation != main || ordinal != 1) throw new InvalidOperationException("Authored orientation changed actual Main invocation.");
        }
        public FalloutPlayerControlWorldFovRead ReadCurrentWorldFov(Guid invocation, long ordinal)
        { RequireCurrent(invocation, ordinal); Reads.Add("fov"); return new(Lease(invocation, ordinal), BitConverter.SingleToUInt32Bits(42f)); }
        public FalloutPlayerControlYawRead ReadCurrentReferenceYaw(Guid invocation, long ordinal)
        { RequireCurrent(invocation, ordinal); Reads.Add("yaw"); return new(Lease(invocation, ordinal), BitConverter.SingleToUInt32Bits(0.75f), 0, true, new("authored.esm", 7), "NPC_", 0); }
        public FalloutPlayerControlPitchRead ReadCurrentReferencePitch(Guid invocation, long ordinal)
        { RequireCurrent(invocation, ordinal); Reads.Add("pitch"); return new(Lease(invocation, ordinal), BitConverter.SingleToUInt32Bits(-0.25f)); }
        private FalloutPlayerControlReadLease Lease(Guid invocation, long ordinal) =>
            new(source.Identity, stack, process, Factory, invocation, ordinal, 0, "authored-current-source-fields");
    }
    private sealed class AuthoredControlFlagEffects(FalloutPlayerMovementControlSource source, string stack,
        Guid process) : IFalloutPlayerControlFlagConsumers
    {
        public FalloutPlayerMovementControlSource Source => source;
        public string Stack => stack;
        public Guid Process => process;
        public Guid Factory { get; } = Guid.NewGuid();
        internal List<string> Calls { get; } = [];
        public void RequireCurrent() { }
        public void SetInputType(int type) => Calls.Add("input:" + type);
        public void DisableFighting() => Calls.Add("fighting");
        public void DisablePointOfView() => Calls.Add("POV");
        public void ClearSneaking() => Calls.Add("sneaking");
    }
}
