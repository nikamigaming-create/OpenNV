using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class PlayerWorldOrientationContracts
{
    internal static void Run()
    {
        using var ordinary = new Fixture();
        var root = ordinary.Root.Capture();
        Require(root is { ConstructorReturned: true, CurrentFovBits: 0x42280000, Stores: 1,
            ConstructorStep: FalloutWorldProjectionConstructorStep.Returned, Camera: { Attached: true, Updates: 3, FrustumStores: 1, Matrix.Count: 16 } } &&
            root.Camera.Frustum.Near == Bits(5) && root.Camera.Frustum.Far == Bits(10240) &&
            root.ConstructorSetting!.Number == 42 && ordinary.Settings.Find(FalloutIniCollection.Main, FalloutPlayerControlConfiguration.WorldFovName)!.Number == 75,
            "Ordinary World omitted its real source frustum/matrix/attachment, or confused constructor FOV with the independent control denominator.");
        Require(ordinary.Reference.Capture() is { XBits: 0, YBits: 0, ZBits: 0, Stores: 0, PlayerFovReturned: true, PlayerFovBits: 0x42280000 } &&
            ordinary.Root.Capture().Stores == 1 && ordinary.TargetReads == 1,
            "Player constructor replaced inherited raw zero fields, duplicated offset state or replayed equal World projection work.");
        ordinary.Root.SetFov(Guid.NewGuid(), "authored-current-ratio-ingress", FalloutWorldFovIngress.PlayerFov, Bits(42), false, null, true, ordinary.Settings);
        Require(ordinary.Root.Capture() is { Stores: 1, CameraRatioBits: 0x3f800000 } && ordinary.TargetReads == 1,
            "Ordered-equal ratio update rebuilt frustum/camera state or omitted the source Float32 ratio store.");
        Require(!FalloutWorldProjectionOwner.ShouldStore(0x80000000, 0, false, false) &&
            !FalloutWorldProjectionOwner.ShouldStore(0, 0x80000000, false, false) &&
            FalloutWorldProjectionOwner.ShouldStore(0x80000000, 0, true, false) &&
            FalloutWorldProjectionOwner.ShouldStore(Bits(42), 0x7fc00001, false, false) &&
            !FalloutWorldProjectionOwner.ShouldStore(Bits(42), Bits(1), true, true),
            "World setter replaced ordered Float32 equality, force, unordered or supplied-camera store semantics.");
        ordinary.Reference.PublishPlacement(Guid.NewGuid(), "authored-original-raw-pending", FalloutReferencePlacementIngress.RawPending,
            Bits(-0.25f), Bits(0.25f), Bits(0.5f));
        var yaw = ordinary.Reference.ReadCurrentReferenceYaw(ordinary.Main, 1);
        var commandConstructor = Guid.NewGuid();
        var rawRotation = ordinary.Reference.ReadPackageCommandRawRotationZ(commandConstructor);
        var physicalRotation = ordinary.Reference.ReadPhysicalReferenceRotation(commandConstructor);
        Require(physicalRotation.X == Bits(-0.25f) && physicalRotation.Y == Bits(0.25f) && physicalRotation.Z == Bits(0.5f),
            "Physical construction borrowed rendered orientation or the separate Player yaw offset.");
        Require(rawRotation.ZBits == Bits(0.5f) && rawRotation.ZBits != FalloutPlayerControlOrientation.Yaw(yaw, ordinary.Fields.ReadYawOffset()) &&
            rawRotation.Process == ordinary.Reference.Process && rawRotation.Factory == ordinary.Reference.Factory &&
            rawRotation.Constructor == commandConstructor && rawRotation.Reference == ordinary.Reference.Binding.Reference,
            "Package raw rotation borrowed derived yaw, another source field or a fake Player frame invocation.");
        Require(FalloutPlayerControlOrientation.Yaw(yaw, ordinary.Fields.ReadYawOffset()) == Bits(0.75f) &&
            ordinary.Reference.ReadCurrentReferencePitch(ordinary.Main, 1).Bits == Bits(-0.25f),
            "Current reference getters imported a scene basis, lost distinct placement axes or omitted independent NPC tilt/Player offset.");
        Cold(ordinary);
        CampaignSaveContracts(ordinary);
        using var changed = new Fixture();
        Reject(() => changed.Root.SetFov(Guid.NewGuid(), "authored-console-world-child", FalloutWorldFovIngress.ConsoleFov,
            Bits(60), false, null, false, changed.Settings, changed.Consumers));
        var failedWorld = changed.Root.Capture();
        Require(failedWorld is { CurrentFovBits: 0x42700000, Stores: 2, Call: { Stored: true, Returned: false, Step: FalloutWorldProjectionStep.Frustum } } &&
            failedWorld.Failure == "source-current-SceneGraph-world-process-far-distance-unowned" && changed.TargetReads == 1,
            "A later specialized clip refusal erased the preceding current FOV store or substituted the startup formula.");
        var failedPublication = new OpenNV.Runtime.World.Cells.FalloutReferenceWorldProjectionSnapshot(
            OpenNV.Runtime.World.Cells.FalloutReferenceWorldProjectionSnapshot.CurrentSchema, changed.Publication.Capture(), null, null);
        failedPublication.Validate();
        Reject(() => FalloutWorldProjectionSaveContract.RequireComplete(failedPublication));
        Require(changed.Root.SaveBlocker == failedWorld.Failure &&
            JsonSerializer.SerializeToElement(changed.Root.State).GetProperty("fovBits").GetUInt32() == Bits(60),
            "Projection save refusal or telemetry erased the actual committed failing FOV prefix.");
        var failedCold = new FalloutWorldProjectionOwner(changed.Source, changed.Stack, Guid.NewGuid(), () => true, RoundTrip(failedWorld));
        Require(failedCold.Capture().CurrentFovBits == Bits(60) && failedCold.Capture().Call == failedWorld.Call,
            "Cold failed projection replayed the old setter or discarded its actual source store.");
        Reject(() => failedCold.SetFov(Guid.NewGuid(), "authored-retry", FalloutWorldFovIngress.WorldRefresh, Bits(42), true, null, false, changed.Settings));
        Require(failedCold.Capture().Stores == 2, "Cold failure was retried as a new returned setter."); failedCold.Retire();
        RawSetterPrefixes(); ForeignAndIncomplete();
        Console.WriteLine("OPENNV_PLAYER_WORLD_ORIENTATION_PASS actualLogicalScene=true sourceConstructorProjection=true currentFov=true rawReferenceFields=true failedStoresRetained=true freshColdNoReplay=true completeSaveAssociation=true malformedStateRejected=true specializedClipAndChangeMask2=UNOWNED gameplay=UNEXECUTED");
    }

    private static void Cold(Fixture warm)
    {
        var root = RoundTrip(warm.Root.Capture()); var publication = RoundTrip(warm.Publication.Capture());
        var reference = RoundTrip(warm.Reference.Capture()); var fields = RoundTrip(warm.Fields.Capture());
        var world = Guid.NewGuid(); var process = Guid.NewGuid(); var actualMain = Guid.NewGuid();
        var restoredRoot = new FalloutWorldProjectionOwner(warm.Source, warm.Stack, world, () => true, root);
        var mutableRotation = root.Camera.Local.Rotation.ToArray();
        var mutablePlanes = root.Culling.Planes.ToArray();
        var external = root with { Camera = root.Camera with { Local = root.Camera.Local with { Rotation = mutableRotation } },
            Culling = root.Culling with { Planes = mutablePlanes } };
        var isolated = new FalloutWorldProjectionOwner(warm.Source, warm.Stack, Guid.NewGuid(), () => true, external);
        mutableRotation[0] = 0x3f800000;
        mutablePlanes[0] = mutablePlanes[0] with { Distance = 0x3f800000 };
        Require(isolated.Capture().Camera.Local.Rotation[0] == root.Camera.Local.Rotation[0] && isolated.Capture().Culling.Planes[0].Distance == 0,
            "External cold payload arrays mutated the actual restored logical camera/culling owners.");
        isolated.Retire();
        var restoredPublication = new FalloutWorldProjectionPublication(warm.Source, warm.Stack, world, () => true);
        restoredPublication.Restore(publication, restoredRoot);
        var restoredFields = new FalloutPlayerControlFieldsState(warm.Source.Controls, warm.Stack, process, fields);
        var restoredReference = new FalloutPlayerReferenceOrientationOwner(warm.Source, warm.Stack, process, reference.Binding,
            restoredPublication, restoredFields, () => true, (main, ordinal) => Require(main == actualMain && ordinal == 1, "Foreign cold Main."), reference);
        Require(restoredRoot.Factory != root.Factory && restoredReference.Factory != reference.Factory &&
            restoredRoot.Capture().Camera.Updates == root.Camera.Updates && restoredRoot.Capture().Stores == root.Stores &&
            restoredReference.Capture().Stores == reference.Stores && restoredReference.Capture().PlayerFovInvocation == reference.PlayerFovInvocation && warm.TargetReads == 1,
            "Cold source owners reused old identities or replayed constructor, target queries, placement or callbacks.");
        Require(restoredReference.Capture().Publications is { Count: 2 } publications &&
            publications[0].Factory == reference.Factory && publications[1].PreviousFactory == reference.Factory,
            "Cold reference rotation lost the actual prior factory needed by historical command reads.");
        Reject(() => FalloutPlayerReferenceOrientationOwner.Validate(reference with { Publications = null }));
        Require(restoredReference.ReadCurrentWorldFov(actualMain, 1).Bits == Bits(42) &&
            restoredReference.ReadCurrentReferencePitch(actualMain, 1).Bits == Bits(-0.25f), "Cold current getters omitted restored field bits.");
        Require(restoredReference.ReadPhysicalReferenceRotation(Guid.NewGuid()) == warm.Reference.ReadPhysicalReferenceRotation(Guid.NewGuid()),
            "Cold physical construction changed independent raw reference rotation fields.");
        Require(restoredReference.Capture().ProjectionWorld == world &&
            restoredReference.Capture().ProjectionRootFactory == restoredRoot.Factory &&
            restoredReference.Capture().ProjectionWorld != reference.ProjectionWorld,
            "Cold reference owner retained a historical World/root as its current publication.");
        Reject(() => new FalloutPlayerReferenceOrientationOwner(warm.Source, warm.Stack, process, reference.Binding,
            restoredPublication, restoredFields, () => true, (_, _) => { }, reference with { ProjectionRootFactory = Guid.NewGuid() }));
        warm.Reference.RequireControlFields(warm.Fields);
        var alternateFields = new FalloutPlayerControlFieldsState(warm.Source.Controls, warm.Stack, reference.CapturedProcess);
        Reject(() => warm.Reference.RequireControlFields(alternateFields));
        Reject(() => FalloutWorldProjectionOwner.Validate(root with { Camera = root.Camera with { Matrix = new uint[16] } }));
        Reject(() => FalloutWorldProjectionOwner.Validate(root with { Camera = root.Camera with { World = root.Camera.World with { Translation = new(0x3f800000, 0, 0) } } }));
        Reject(() => FalloutWorldProjectionOwner.Validate(root with { Camera = root.Camera with { MinimumNear = 0 } }));
        Reject(() => FalloutPlayerReferenceOrientationOwner.Validate(reference with { YBits = 0 }));
        Reject(() => FalloutPlayerReferenceOrientationOwner.Validate(reference with { Binding = reference.Binding with { IsCharacter = false } }));
        Reject(() => new FalloutWorldProjectionOwner(warm.Source, warm.Stack, root.CapturedWorld, () => true, root));
        var wrongRoot = new FalloutWorldProjectionOwner(warm.Source, warm.Stack, Guid.NewGuid(), () => true);
        var wrongPublication = new FalloutWorldProjectionPublication(warm.Source, warm.Stack, wrongRoot.World, () => true);
        Reject(() => wrongPublication.Restore(publication, wrongRoot)); wrongRoot.Retire();
        restoredReference.Retire(); restoredPublication.RetireCurrent();
    }

    private static void RawSetterPrefixes()
    {
        using var raw = new Fixture();
        Reject(() => raw.Reference.SetRawRotation(Guid.NewGuid(), "authored-raw-X-setter", FalloutReferenceRotationAxis.X, Bits(0.25f)));
        var failed = RoundTrip(raw.Reference.Capture());
        Require(failed is { XBits: 0x3e800000, Stores: 1, Failure: FalloutPlayerReferenceOrientationOwner.ChangeBoundary,
            Call: { ChangeMask: 2, Step: FalloutReferenceOrientationStep.ChangeCallback, Consumer: null, Returned: false } },
            "Raw X change callback failure erased its preceding field store or manufactured a return.");
        Reject(() => FalloutPlayerReferenceOrientationOwner.Validate(failed with { XBits = 0 }));
        Reject(() => FalloutWorldProjectionSaveContract.ValidatePlayer(CompleteProjection(raw), failed,
            raw.Source.Controls.Main, raw.Stack, failed.CapturedProcess));
        using var rawYaw = new Fixture();
        var argument = Bits(-20);
        Reject(() => rawYaw.Reference.SetRawRotation(Guid.NewGuid(), "authored-raw-Z-setter", FalloutReferenceRotationAxis.Z, argument));
        Require(rawYaw.Reference.Capture().ZBits == FalloutPlayerControlOrientation.NormalizeBits(argument) &&
            BitConverter.UInt32BitsToSingle(rawYaw.Reference.Capture().ZBits) < 0 && rawYaw.Reference.Capture().Stores == 2 &&
            FalloutPlayerControlOrientation.NormalizeBits(0x80000000) == 0x80000000,
            "Raw Z normalization became a positive modulo, lost negative zero or omitted its independent second store.");
        using var nonfinite = new Fixture();
        Reject(() => nonfinite.Reference.SetRawRotation(Guid.NewGuid(), "authored-nonfinite-raw-Z", FalloutReferenceRotationAxis.Z, 0x7fc00003));
        Require(nonfinite.Reference.Capture() is { ZBits: 0x7fc00003, Stores: 1, Call.Step: FalloutReferenceOrientationStep.Stored },
            "Exceptional yaw normalization erased its actual preceding raw store.");
        using var delta = new Fixture();
        Reject(() => delta.Reference.EnterPitchDelta(Guid.NewGuid(), "authored-current-pitch-delta", Bits(0.1f)));
        Require(delta.Reference.Capture() is { XBits: 0, Stores: 0, Call: { ArgumentBits: 0x3dcccccd, Step: FalloutReferenceOrientationStep.Admission, Axis: null } },
            "Unowned pitch admission skipped the actual Float32 argument or directly overwrote raw X.");
    }

    private static void ForeignAndIncomplete()
    {
        using var fixture = new Fixture();
        var incomplete = new FalloutWorldProjectionOwner(fixture.Source, fixture.Stack, fixture.Root.World, () => true);
        Reject(() => fixture.Publication.PublishReturned(incomplete)); incomplete.Retire();
        var supplied = new FalloutWorldProjectionOwner(fixture.Source, fixture.Stack, Guid.NewGuid(), () => true);
        Reject(supplied.ConstructWithSuppliedCamera);
        Require(supplied.Capture() is { CurrentFovBits: null, Stores: 0, ConstructorReturned: false }, "Supplied-camera arm inherited ordinary FOV."); supplied.Retire();
        Exception? error = null;
        var thread = new Thread(() => { try { fixture.Reference.Capture(); } catch (Exception caught) { error = caught; } });
        thread.Start(); thread.Join(); Require(error is InvalidOperationException, "Foreign thread captured current orientation.");
        var bad = new FalloutWorldProjectionOwner(fixture.Source, fixture.Stack, Guid.NewGuid(), () => true);
        var services = new FalloutWorldProjectionConsumers(bad, FalloutWorldProjectionInputs.Read(fixture.Source, fixture.Stack, fixture.Settings),
            () => new(fixture.Source.Identity, fixture.Stack, fixture.Root.World, Guid.NewGuid(), false, null, null, "authored-foreign-world-target"));
        Reject(() => bad.ConstructOrdinary(fixture.Settings, services));
        Require(bad.Capture() is { Stores: 1, ConstructorReturned: false, Camera.FrustumStores: 0 }, "Foreign target query crossed source frustum publication."); bad.Retire();
    }

    private sealed class Fixture : IDisposable
    {
        internal FalloutPlayerWorldOrientationSource Source { get; }
        internal string Stack { get; } = new('2', 64);
        internal Guid Main { get; } = Guid.NewGuid();
        internal FalloutNumericIniSettings Settings { get; }
        internal FalloutWorldProjectionOwner Root { get; }
        internal FalloutWorldProjectionConsumers Consumers { get; }
        internal FalloutWorldProjectionPublication Publication { get; }
        internal FalloutPlayerControlFieldsState Fields { get; }
        internal FalloutPlayerReferenceOrientationOwner Reference { get; }
        internal int TargetReads { get; private set; }
        internal Fixture()
        {
            var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
                FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
            Source = FalloutPlayerWorldOrientationSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(main));
            Settings = new([new(FalloutPlayerWorldOrientationSource.ConstructorFovName, FalloutIniCollection.Main, 0x42960000),
                new(FalloutPlayerControlConfiguration.WorldFovName, FalloutIniCollection.Main, 0x42960000),
                new(FalloutWorldProjectionInputs.MinimumName, FalloutIniCollection.Main, 0x44d48000),
                new(FalloutWorldProjectionInputs.MaximumName, FalloutIniCollection.Main, 0x46200000),
                new(FalloutWorldProjectionInputs.FractionName, FalloutIniCollection.Main, 0x3f800000),
                new(FalloutWorldProjectionInputs.NearName, FalloutIniCollection.Main, 0x40a00000)],
                [new("authored-winning-Main-INI", FalloutIniCollection.Main, [new("Display", "fDefaultFOV", "42")])], Stack + ":authored:" + main.EngineSha256);
            var world = Guid.NewGuid(); var process = Guid.NewGuid(); var targetOwner = Guid.NewGuid();
            Root = new(Source, Stack, world, () => true);
            Consumers = new(Root, FalloutWorldProjectionInputs.Read(Source, Stack, Settings), () =>
            { TargetReads++; return new(Source.Identity, Stack, world, targetOwner, false, null, null, "authored-current-absent-renderer"); });
            Root.ConstructOrdinary(Settings, Consumers); Publication = new(Source, Stack, world, () => true); Publication.PublishReturned(Root);
            Fields = new(Source.Controls, Stack, process);
            Reference = new(Source, Stack, process, new(new("authored.esm", 0x14), new("authored.esm", 7), "NPC_",
                FalloutPlayerControlOrientation.NpcTiltYaw, true), Publication, Fields, () => true,
                (invocation, ordinal) => Require(invocation == Main && ordinal == 1, "Authored Main invocation changed."));
            Reference.InitializePlayerFov(Settings);
        }
        public void Dispose() { Reference.Retire(); Publication.RetireCurrent(); }
    }
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException or ObjectDisposedException) { return; }
        throw new InvalidOperationException("Invalid source world/reference operation was accepted.");
    }
}
