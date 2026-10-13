using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerControlConfigurationAndOrientation(FalloutPlayerMovementControlSource source, string stack)
    {
        Require(FalloutPlayerControlFieldsState.CommandMask(new(false, [])) == 0x0d &&
            FalloutPlayerControlFieldsState.CommandMask(new(true, [])) == 0x7f,
            "FO3 control commands changed their omitted-argument defaults or independent mask mapping.");
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
