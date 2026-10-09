using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class PlayerPhysicalActivityContracts
{
    // Authored state tests. These exercise no original executable, native
    // skeleton, physics solver, sleep-time effects or original plugin.
    internal static void Run()
    {
        NativePublication(); DistinctSleepFlag(); KnockdownRecovery(); CallbackPrefixAndCold(); SnapshotRefusals();
        Console.WriteLine("OPENNV_PLAYER_PHYSICAL_ACTIVITY_CONTRACT_PASS authoredState=true publication=true " +
            "independentSleep=true recoveryQuery1=true callbackPrefix=true coldNoReplay=true native=unexecuted");
    }

    private static FalloutPlayerPhysicalSource Source => new(new("Authored.esm", 7), new('1', 64),
        new("Authored.esm", 7), new('2', 64), new('3', 64), FalloutPlayerPhysicalSource.CurrentContractSha256);
    private static float[] Pose => [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4];
    private static FalloutPlayerPhysicalPose Bones => new(new('5', 64), Pose, Pose,
        [new(0, 4, "AuthoredRoot", -1, [0, 0, 0, 0, 0, 0, 1, 1, 1, 1])]);
    private static FalloutFurnitureClipSnapshot Clip(int id) => new(new("Authored.esm", (uint)id),
        new('6', 64), "meshes/authored" + id + ".kf", new('7', 64));
    private static FalloutPlayerFurnitureSnapshot Bed(FalloutPlayerFurniturePhase phase)
    {
        var marker = new FalloutNifFurniturePosition(new(0, 0, 0), 0, 1, 1);
        var seat = new FalloutFurnitureSeat(new("Authored.esm", 30), 0, 1, marker, [0, 0, 0], 0,
            FalloutPlayerFurnitureKind.Sleeping);
        var clips = new Dictionary<int, FalloutFurnitureClipSnapshot> { [1] = Clip(41), [2] = Clip(42), [4] = Clip(44) };
        return new(new("Authored.esm", 31), new('8', 64), new("Authored.esm", 32), new('9', 64),
            "meshes/authored-bed.nif", new('a', 64), seat, seat.Kind, phase, Pose, Pose, Pose, null, 0, 0, 0,
            phase == FalloutPlayerFurniturePhase.Approaching ? null : new(clips[phase == FalloutPlayerFurniturePhase.Occupied ? 1 : (int)phase], 0, true),
            clips, Pose, new(new("Authored.esm", 32), [2, 3, 4], [0, 0, 0]));
    }
    private static FalloutPlayerPhysicalSnapshot Capture(FalloutPlayerPhysicalActivity owner,
        FalloutPlayerFurnitureSnapshot? furniture = null, FalloutPlayerKnockdownSnapshot? knockdown = null) =>
        new(owner.Source, owner.Attempt, owner.Sleeping, 0,
            new(new("Authored.esm", 0x14), 0, []), furniture, knockdown, owner.Failure,
            furniture is not null || knockdown is not null ? Bones : null);
    private static FalloutPlayerPhysicalSnapshot Cold(FalloutPlayerPhysicalSnapshot value) =>
        JsonSerializer.Deserialize<FalloutPlayerPhysicalSnapshot>(JsonSerializer.Serialize(value))!;

    private static void NativePublication()
    {
        var owner = new FalloutPlayerPhysicalActivity(Source);
        Require(owner.Observe(FalloutAdvancementActivityFact.PlayerAwake).State == FalloutAdvancementActivityState.Unowned &&
            owner.Observe(FalloutAdvancementActivityFact.PlayerUpright).State == FalloutAdvancementActivityState.Unowned,
            "Unpublished constructor state masqueraded as live activity.");
        Reject(() => _ = owner.IsPcSleeping); Reject(() => _ = owner.KnockedState);
        owner.PublishNativeOwner();
        Require(!owner.IsPcSleeping && owner.SleepingState == 0 && owner.KnockedState == 0 &&
            owner.Observe(FalloutAdvancementActivityFact.PlayerAwake).State == FalloutAdvancementActivityState.Satisfied &&
            owner.Observe(FalloutAdvancementActivityFact.PlayerUpright).State == FalloutAdvancementActivityState.Satisfied,
            "Admitted original normal state did not remain distinct from unpublished readiness.");
        Reject(owner.PublishNativeOwner); owner.RetireNativeOwner();
        Require(owner.Observe(FalloutAdvancementActivityFact.PlayerAwake).State == FalloutAdvancementActivityState.Unowned,
            "Native retirement retained a live activity receipt.");
    }
    private static void DistinctSleepFlag()
    {
        var owner = new FalloutPlayerPhysicalActivity(Source); owner.PublishNativeOwner();
        owner.Execute("chair", () => owner.CommitFurniture(FalloutPlayerFurnitureKind.Sitting, FalloutPlayerFurniturePhase.Occupied));
        Require(owner.SleepingState == 0 && !owner.IsPcSleeping, "Sitting became an independent sleep flag.");
        owner.Execute("leave-chair", () => owner.CommitFurniture(FalloutPlayerFurnitureKind.Sitting, FalloutPlayerFurniturePhase.None));
        owner.BeginSleepClock(() => { });
        Require(owner.IsPcSleeping && owner.SleepingState == 0,
            "The independent source time flag was incorrectly restricted to an actor bed pose.");
        owner.EndSleepClock(() => { });
        foreach (var phase in new[] { FalloutPlayerFurniturePhase.Approaching, FalloutPlayerFurniturePhase.Entering,
            FalloutPlayerFurniturePhase.Occupied })
        {
            owner.Execute("bed:" + phase, () => owner.CommitFurniture(FalloutPlayerFurnitureKind.Sleeping, phase));
            if (phase == FalloutPlayerFurniturePhase.Approaching) Reject(() => _ = owner.SleepingState);
            else Require(owner.SleepingState == (int)phase, "The source actor bed query phase changed.");
            Require(!owner.IsPcSleeping, "The actor bed phase became the independent player sleeping flag.");
        }
        var begins = 0; var ends = 0;
        owner.BeginSleepClock(() => begins++);
        Require(owner.IsPcSleeping && begins == 1 &&
            owner.Observe(FalloutAdvancementActivityFact.PlayerAwake).State == FalloutAdvancementActivityState.Held,
            "An actual begin callback did not own the sleep flag.");
        Reject(() => owner.BeginSleepClock(() => begins++));
        Reject(() => owner.CommitFurniture(FalloutPlayerFurnitureKind.Sleeping, FalloutPlayerFurniturePhase.Exiting));
        owner.EndSleepClock(() => ends++);
        owner.Execute("wake-exit", () => owner.CommitFurniture(FalloutPlayerFurnitureKind.Sleeping, FalloutPlayerFurniturePhase.Exiting));
        Require(!owner.IsPcSleeping && owner.SleepingState == 4 && begins == 1 && ends == 1,
            "Wake or exit replayed a time callback or conflated its distinct flag.");
    }
    private static void KnockdownRecovery()
    {
        var owner = new FalloutPlayerPhysicalActivity(Source); owner.PublishNativeOwner();
        owner.Execute("fall", () => owner.CommitKnockdown(FalloutPlayerKnockdownPhase.Simulating));
        Require(owner.KnockedState == 1 && !owner.IsPcSleeping &&
            owner.Observe(FalloutAdvancementActivityFact.PlayerUpright).State == FalloutAdvancementActivityState.Held,
            "Knockdown had no distinct physical activity state.");
        owner.Execute("recover", () => owner.CommitKnockdown(FalloutPlayerKnockdownPhase.Recovering));
        Require(owner.KnockedState == 1, "Recovery invented an original knocked query value2.");
        var snapshot = Capture(owner, knockdown: new(owner.KnockdownPhase, "meshes/authored-skeleton.nif", Bones.SkeletonSha256,
            Pose, null, new(Clip(50), .625, false)));
        var cold = new FalloutPlayerPhysicalActivity(Source, Cold(snapshot));
        Reject(() => _ = cold.KnockedState);
        cold.PublishNativeOwner(); Require(cold.KnockedState == 1 && cold.Attempt == owner.Attempt,
            "Cold publication consumed another fall/recovery operation.");
        cold.Execute("native-finite-recovery", () => cold.CommitKnockdown(FalloutPlayerKnockdownPhase.Upright));
        Require(cold.KnockedState == 0, "Finite recovery retained a knocked query.");
    }
    private static void CallbackPrefixAndCold()
    {
        var owner = new FalloutPlayerPhysicalActivity(Source); owner.PublishNativeOwner();
        owner.Execute("occupy-bed", () => owner.CommitFurniture(FalloutPlayerFurnitureKind.Sleeping, FalloutPlayerFurniturePhase.Occupied));
        var calls = 0;
        Reject(() => owner.BeginSleepClock(() => { calls++; throw new IOException("Authored committed time prefix failed."); }));
        Require(owner.Sleeping && owner.Failure?.Operation == "begin-source-sleep-clock" && calls == 1,
            "An ordinary IOException lost the actual committed sleep operation.");
        var snapshot = Capture(owner, Bed(FalloutPlayerFurniturePhase.Occupied));
        snapshot.Validate();
        var cold = new FalloutPlayerPhysicalActivity(Source, Cold(snapshot)); cold.PublishNativeOwner();
        Reject(() => cold.Execute("replay", () => calls++));
        Require(calls == 1 && cold.Sleeping && cold.Failure == owner.Failure && cold.Attempt == owner.Attempt &&
            cold.Observe(FalloutAdvancementActivityFact.PlayerAwake).State == FalloutAdvancementActivityState.Unowned,
            "Cold failure publication replayed or cleared an actual committed callback prefix.");
    }
    private static void SnapshotRefusals()
    {
        var owner = new FalloutPlayerPhysicalActivity(Source); owner.PublishNativeOwner();
        var empty = Capture(owner); empty.Validate();
        Reject(() => new FalloutPlayerPhysicalActivity(Source with { RuntimeSha256 = new('b', 64) }, empty));
        (empty with { Sleeping = true }).Validate(); // A time flag is independent of actor bed phases.
        Reject(() => (empty with { Sounds = empty.Sounds with { Reference = new("Authored.esm", 15) } }).Validate());
        var bed = Bed(FalloutPlayerFurniturePhase.Occupied);
        var active = empty with { Furniture = bed, NativePose = Bones };
        active.Validate();
        Reject(() => (active with { NativePose = null }).Validate());
        Reject(() => (active with { Furniture = bed with { Animation = bed.Animation! with { AttemptedKeyOrdinal = 3, AttemptedKeyCycle = 0 } } }).Validate());
        Reject(() => (Bones with { Bones = [] }).Validate());
        Reject(() => (Bones with { Bones = [Bones.Bones[0] with { SourceBlock = -1 }] }).Validate());
        Reject(() => (bed with { Seat = bed.Seat with { Marker = bed.Seat.Marker with { Offset = new(float.NaN, 0, 0) } } }).Validate());
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException or IOException) { return; }
        throw new InvalidOperationException("Player physical contract admitted an invalid/unowned operation.");
    }
    private static void Require(bool condition, string error)
    { if (!condition) throw new InvalidOperationException(error); }
}
