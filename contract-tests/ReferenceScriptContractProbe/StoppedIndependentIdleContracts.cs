using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class StoppedIndependentIdleContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        FalloutFormKey Key(uint id) => new("Pose.esm", id);
        string Hash(uint id) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(id)));
        (float Time, string Value)[] keys = [(0, "begin"), (.1f, "StartLoop"), (.5f, "pulse"), (1.5f, "EndLoop"), (2, "end")];
        FalloutIdleAnimationPlayback Clock() => new(0, 2, 1, 2, keys, byte.MaxValue);
        var warm = Clock(); warm.Advance(860.375);
        var residual = new FalloutActorResidualPose("meshes/fixture/skeleton.nif", new string('b', 64),
            [new(0, "Uncovered", [1.125f, 2, 3], [0, 0, 0, 1], [1, 1, 1])]);
        var animation = new FalloutActorPackageIdleAnimation(Key(0x34), Hash(0x34),
            "meshes/fixture/independent.kf", new string('a', 64), warm.Capture(), 2, residual);
        var independent = new FalloutActorStoppedIdleAnimation("dialogue-response", animation);
        var failure = new FalloutActorPackageBindingFailure(Key(0x20), Hash(0x20),
            "Selected procedure is stopped before begin.", [2, 3, 4], [1, 0, 0, 0, 1, 0, 0, 0, 1], false,
            0x123456789abcdef0, 3.25, null, new(0, null, null, null), IndependentIdle: independent);
        using var world = new FalloutReferenceWorld(records);
        var actor = world.Get(Key(0x900));
        actor.Animation.Change("meshes/fixture/mtidle.kf", new string('c', 64)); actor.Animation.Advance(12.125);
        actor.PackageBindingFailure = failure; actor.ProcedureCaptureBlocker = failure.Error;
        var captured = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records); cold.Restore(captured);
        var saved = cold.Get(actor.Reference).PackageBindingFailure!;
        Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(captured) &&
            saved.IndependentIdle!.Animation.Clock == warm.Capture() && saved.AiRandomState == failure.AiRandomState &&
            cold.Get(actor.Reference).Animation.ElapsedSeconds == 12.125 && cold.PendingProcedureCaptureCount == 0,
            "Independent stopped IDLE lost its fault, loop phase, random stream or separate base clock.");
        var resumed = Clock(); resumed.Restore(saved.IndependentIdle!.Animation.Clock);
        var warmKeys = new List<FalloutIdleAnimationInterval>(); var coldKeys = new List<FalloutIdleAnimationInterval>();
        warm.Advance(3.125, warmKeys.Add); resumed.Advance(3.125, coldKeys.Add);
        Require(warm.Capture() == resumed.Capture() && warmKeys.SequenceEqual(coldKeys) &&
            !coldKeys[0].IncludeFrom && resumed.CompletedRepeats > animation.Clock.CompletedRepeats,
            "Cold independent loop replayed its selected prefix or lost future text-key crossings.");
        var copied = saved.Copy();
        Require(!ReferenceEquals(copied.IndependentIdle!.Animation.ResidualPose!.Bones[0].Position,
            saved.IndependentIdle.Animation.ResidualPose!.Bones[0].Position), "Independent residual pose retained mutable aliases.");
        foreach (var invalid in new[]
        {
            failure with { MovingBasePose = true },
            failure with { IndependentIdle = independent with { Owner = "package-idle" } },
            failure with { IndependentIdle = independent with { Animation = animation with { IdleSha256 = new string('0', 64) } } },
            failure with { IndependentIdle = independent with { Animation = animation with { Resource = "meshes/fixture/wrong.kf" } } },
            failure with { IndependentIdle = independent with { Animation = animation with { ResidualPose = null } } },
            failure with { IndependentIdle = independent with { Animation = animation with { Clock = animation.Clock with { Complete = true } } } },
            failure with { IndependentIdle = independent with { Animation = animation with { Idle = Key(0x33), IdleSha256 = Hash(0x33), Resource = "meshes/fixture/with-object.kf" } } },
        })
        {
            using var rejected = new FalloutReferenceWorld(records);
            Reject(() => rejected.Restore(captured.Select(value => value with { PackageBindingFailure = invalid }).ToArray()));
            Require(rejected.InstanceCount == 0, "Invalid independent IDLE partially restored the cold world.");
        }
        Reject(() => animation.Validate(records, Key(0x21)));
        Reject(() => FalloutReferenceSnapshot.Validate(captured.Select(value => value with
        { Engagement = new(Key(0x902), "idle", 1, false, "meshes/fixture/combat-idle.kf", new string('d', 64),
            [2, 3, 4], [0, 0, 0, 1]) }).ToArray()));
        Console.WriteLine("OPENNV_STOPPED_INDEPENDENT_IDLE_CONTRACT_PASS sourceIdle=true independentOfPackageCollection=true endlessPhase=true futureTextKeys=true baseClock=true rng=true faultRetained=true residualCopy=true invalidAtomic=true attachmentsRefused=true nativePose=unverified");
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid independent stopped IDLE was admitted.");
    }
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
}
