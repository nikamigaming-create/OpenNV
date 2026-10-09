using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunStandalonePlayerSceneContracts()
    {
        var immediate = new FalloutImmediateScriptSource(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3));
        var source = FalloutStandalonePlayerSceneSource.Read(FalloutMainScriptCallerSource.Read(immediate));
        Reject(() => (source with { Contract = new('a', 64) }).Validate());
        var first = new FalloutStandalonePlayerSceneState(source, "authored-scene-stack", Guid.NewGuid());
        var initial = first.Capture();
        Require(initial.SceneMode == 0 && initial.MainHold == 0 && initial.PlayerView == 0 && initial.FirstPerson is null &&
            initial.SceneBracket == 0 && initial.Numerator == 0x3f800000 && initial.Denominator == 0x3f800000 && initial.CountdownBits == 0x40a00000,
            "Main/Player constructors invented an active scene, held flag, view writer, native body or scene clock.");
        Reject(() => FalloutStandalonePlayerSceneState.Validate(initial with { MainHold = 1 }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(initial with { PlayerView = 1 }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(initial with { SceneBracket = 1 }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(initial with { Denominator = 0 }));
        var native = new AuthoredPlayerSceneObject();
        var stored = first.PublishFirstPerson(native.Publication(first), native.ReadLiving);
        Require(first.ReadFirstPersonSelected() && !first.ReadSceneMode() && !first.ReadMainHold(),
            "Direct source first-person selection was inferred from view/tree/global gameplay flags.");
        Reject(() => first.PublishFirstPerson(new AuthoredPlayerSceneObject().Publication(first), () => true));
        var saved = first.Capture(); FalloutStandalonePlayerSceneState.RequirePlayable(saved);
        Reject(() => new FalloutStandalonePlayerSceneState(source, "foreign-selection", Guid.NewGuid(), saved));
        Reject(() => new FalloutStandalonePlayerSceneState(source, saved.Stack, saved.CapturedProcess, saved));
        var cold = new FalloutStandalonePlayerSceneState(source, saved.Stack, Guid.NewGuid(), saved);
        Reject(() => cold.ReadFirstPersonSelected());
        var coldNative = new AuthoredPlayerSceneObject(native.NativeRoot);
        Reject(() => cold.PublishFirstPerson(coldNative.Publication(cold) with { Factory = native.Factory }, coldNative.ReadLiving));
        var rebound = cold.PublishFirstPerson(coldNative.Publication(cold), coldNative.ReadLiving);
        Require(cold.ReadFirstPersonSelected() && rebound.Publication.Process != stored.Publication.Process &&
            rebound.Publication.NativeRoot == stored.Publication.NativeRoot && rebound.Publication.Factory != stored.Publication.Factory,
            "Cold scene reused a pointer/factory instead of rebinding the actual source role in a new process.");
        Reject(() => cold.ReleaseFirstPerson(stored));
        cold.ReleaseFirstPerson(rebound); cold.Retire(); coldNative.Retire();
        first.ReleaseFirstPerson(stored); first.Retire(); native.Retire();

        var camera = new FalloutStandalonePlayerSceneState(source, saved.Stack, Guid.NewGuid());
        var consumer = new AuthoredFreeCameraConsumers();
        Require(camera.ToggleFreeCamera(1, consumer) && consumer.Stores.SequenceEqual(new byte[] { 1 }) &&
            camera.Capture().MainHold == 1 && camera.Capture().CameraPosition == new FalloutStandaloneCameraPosition(0x3f800000, 0x40000000, 0x40a00000),
            "Free-camera source stores were reordered or its captured height replaced by presentation camera state.");
        consumer.ThrowOnPosition = true;
        Require(!camera.ToggleFreeCamera(1, consumer) && consumer.Stores.SequenceEqual(new byte[] { 1, 0 }) && !camera.ReadMainHold(),
            "Leaving free-camera reread enter-only position or retained the hold byte.");
        var failed = new FalloutStandalonePlayerSceneState(source, saved.Stack, Guid.NewGuid());
        Reject(() => failed.ToggleFreeCamera(2, new AuthoredFreeCameraConsumers { ThrowOnManager = true }));
        var failure = failed.Capture();
        Require(failure.SceneMode == 1 && failure.MainHold == 1 && failure.Camera?.FailedAt == FalloutStandaloneCameraStep.InputManagerChild &&
            failure.CameraPosition == new FalloutStandaloneCameraPosition(0, 0, 0), "A failed manager child rolled back source byte stores or fabricated camera capture.");
        Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(failure));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(failure with { SceneMode = 0 }));
        failed.Retire(); camera.Retire();

        var clock = new FalloutStandalonePlayerSceneState(source, saved.Stack, Guid.NewGuid());
        var main = Guid.NewGuid();
        Require(clock.StoreSceneClock(main, 7, 0x3e800000) == 0x3e800000 && clock.EnterSceneVirtual(main, 7) == 0x3e800000,
            "Selected unit clock changed the actual delivered Float32 bits.");
        var released = 0;
        Reject(() => clock.ExecuteInitialVirtualPrefix(main, 7, () => released++));
        var stopped = clock.Capture();
        Require(released == 1 && stopped.SceneBracket == 1 && stopped.Clock?.Step == FalloutStandaloneSceneClockStep.Failed &&
            stopped.Prelude?.FailedAt == FalloutStandaloneScenePreludeStep.ReferenceQuery,
            "Actual shared-child prefix was skipped/repeated or the unreturned virtual fabricated the source decrement.");
        Reject(() => clock.StoreSceneClock(main, 7, 0x3e800000));
        Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(stopped));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(stopped with { SceneBracket = 0 }));
        clock.Retire();
        var reentry = new FalloutStandalonePlayerSceneState(source, saved.Stack, Guid.NewGuid());
        var objectWithCallback = new AuthoredPlayerSceneObject();
        Reject(() => reentry.PublishFirstPerson(objectWithCallback.Publication(reentry), () =>
        {
            try { reentry.Retire(); } catch (InvalidOperationException) { }
            return objectWithCallback.ReadLiving();
        }));
        Require(reentry.Capture().FirstPerson is null, "Swallowed native lifetime callback reentry published a false scene owner.");
        reentry.Retire(); objectWithCallback.Retire();
        Console.WriteLine("OPENNV_FO3_SOURCE_PLAYER_SCENE_PASS authoredOnly=true independentMainBytes=true directFirstPerson=true " +
            "coldFreshFactory=true orderedCameraStores=true sharedChildPrefix=true unreturnedVirtualBracketRetained=true nativeGameplay=UNEXECUTED");
    }
    private sealed class AuthoredPlayerSceneObject
    {
        private static ulong _next;
        private bool _living = true;
        internal Guid Factory { get; } = Guid.NewGuid();
        internal ulong NativeRoot { get; }
        internal AuthoredPlayerSceneObject(ulong? native = null) => NativeRoot = native ?? ++_next;
        internal bool ReadLiving() => _living;
        internal void Retire() => _living = false;
        internal FalloutStandaloneFirstPersonPublication Publication(FalloutStandalonePlayerSceneState state) =>
            new(Factory, state.Process, state.Source.Identity, state.Stack, NativeRoot, "meshes/authored/first-person.nif", new('b', 64), "authored-actual-scene-object");
    }
    private sealed class AuthoredFreeCameraConsumers : IFalloutStandaloneFreeCameraConsumers
    {
        public string Owner => "authored-ordered-free-camera-consumers";
        internal List<byte> Stores { get; } = [];
        internal bool ThrowOnManager, ThrowOnPosition;
        public void StoreInputManagerCameraByte(byte value)
        { Stores.Add(value); if (ThrowOnManager) throw new InvalidOperationException("authored manager failure"); }
        public FalloutStandaloneCameraPosition ReadPosition() => ThrowOnPosition ? throw new InvalidOperationException("leave must not call position") : new(0x3f800000, 0x40000000, 0x40400000);
        public uint ReadScaledCameraHeightBits() => 0x40000000;
        public FalloutStandaloneCameraAngles ReadAngles() => new(0x3f000000, 0xbf000000);
    }
}
