using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static class StoppedPoseContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-stopped-pose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; Float(header, 0, 1.34f);
            var pkdt = new byte[12]; pkdt[4] = 6;
            var location = new byte[12]; UInt(location, 4, 0x901);
            var timing = new byte[8]; timing[0] = 71; timing[1] = 3; timing[2] = 6;
            BinaryPrimitives.WriteUInt16LittleEndian(timing.AsSpan(4), 9);
            var predicate = new byte[28]; Float(predicate, 4, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(predicate.AsSpan(8), 45); UInt(predicate, 12, 0x902);
            var refs = Join(Reference("ACHR", 0x900, 0x10), Reference("ACHR", 0x902, 0x11),
                Reference("REFR", 0x901, 0x40));
            var group = new byte[24 + refs.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); refs.CopyTo(group, 24);
            File.WriteAllBytes(Path.Combine(directory, "Pose.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Npc(0x10), Npc(0x11), Record("BPTD", 0x1d, Part()),
                Setting(0x61, "fAVDNPCHealthEnduranceOffset", -1), Setting(0x62, "fAVDNPCHealthEnduranceMult", 5.25f),
                Setting(0x63, "fAVDNPCHealthLevelMult", 5),
                Record("MISC", 0x12, Field("DATA", new byte[8])),
                Record("PACK", 0x20, Field("PKDT", pkdt)), Record("PACK", 0x22, Field("PKDT", pkdt)),
                Record("PACK", 0x25, Field("PKDT", pkdt), Field("CTDA", predicate)),
                Record("PACK", 0x21, Field("PKDT", pkdt), Field("PLDT", location),
                    Field("IDLC", [1]), Field("IDLA", BitConverter.GetBytes(0x30u))),
                Record("PACK", 0x24, Field("PKDT", pkdt), Field("PLDT", location),
                    Field("IDLC", [1]), Field("IDLA", BitConverter.GetBytes(0x33u))),
                Record("IDLE", 0x30, Field("DATA", timing), Field("MODL", Text("fixture/seat-overlay.kf"))),
                Record("IDLE", 0x31, Field("DATA", new byte[8]), Field("MODL", Text("fixture/chair-base.kf"))),
                Record("IDLE", 0x32, Field("DATA", timing), Field("MODL", Text("fixture/other-overlay.kf"))),
                Record("IDLE", 0x33, Field("DATA", timing), Field("MODL", Text("fixture/with-object.kf"))),
                Record("ANIO", 0x50, Field("DATA", BitConverter.GetBytes(0x33u)), Field("MODL", Text("fixture/object.nif"))),
                Record("FURN", 0x40, Field("MNAM", BitConverter.GetBytes(0x40000001u))),
                Record("CELL", 0x800, Field("DATA", [1])), group));
            using var records = FalloutPluginStack.Load(directory, ["Pose.esm"]);
            StoppedCombat(records);
            ActiveFurnitureIdle(records);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static FalloutFormKey Key(uint id) => new("Pose.esm", id);

    private static void StoppedCombat(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var cell = FalloutCellSceneReader.Read(records, Key(0x800)); world.LoadCell(cell);
        var state = world.Get(Key(0x900));
        var failure = new FalloutActorPackageBindingFailure(Key(0x20), Hash(records, 0x20),
            "Source procedure stopped before begin.", [2, 3, 4], [1, 0, 0, 0, 1, 0, 0, 0, 1], false,
            0x123456789abcdef0, 2.125, new(4, 10, 2, 7.5f), new(2, "POCA", Key(0x22), Hash(records, 0x22)));
        state.Animation.Change("meshes/fixture/mtidle.kf", new string('a', 64)); state.Animation.Advance(8.125);
        state.PackageBindingFailure = failure; state.ProcedureCaptureBlocker = failure.Error;
        state.Engagement = new(Key(0x902), "idle", 4.25, false, "meshes/fixture/combat-idle.kf", new string('b', 64),
            [2, 3, 4], [0, 0, 0, 1]);
        var living = Roundtrip(world.Capture());
        using (var coldLiving = new FalloutReferenceWorld(records))
        {
            coldLiving.Restore(living);
            Require(JsonSerializer.Serialize(coldLiving.Capture()) == JsonSerializer.Serialize(living),
                "Stopped combat idle lost its separate source fault, pose or phase.");
        }
        foreach (var invalid in new[]
        {
            state.Engagement with { Action = "pursue" }, state.Engagement with { StartPending = true, Seconds = 0 },
            state.Engagement with { Animation = null, AnimationHash = null }, state.Engagement with { Position = null, Rotation = null },
            state.Engagement with { Target = Key(0x12) }
        }) AtomicReject(records, Replace(living, state.Reference, value => value with { Engagement = invalid }));
        var hit = world.DamageActor(state.Reference, Key(0x902), 0, 125, 1, 1);
        Require(hit.Died && state.Injury!.Dead && state.DeathCount == 1, "Fixture did not perform an authoritative death transition.");
        state.Engagement = new(Key(0x902), "pursue", StartPending: true, Position: [2, 3, 4], Rotation: [0, 0, 0, 1]);
        var body = new FalloutRagdollBodyState(7, [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4], [.125f, 0, -.25f], [0, .5f, 0], false);
        state.Ragdoll = new(new string('c', 64), [body]);
        world.AdvanceDeathEvent(state.Reference, .25, 2, false);
        var dead = Roundtrip(world.Capture());
        world.UnloadCell(cell.Cell.FormKey);
        Require(JsonSerializer.Serialize(world.Capture()) == JsonSerializer.Serialize(dead),
            "Unloading a retained stopped corpse changed its source or independently captured pose.");
        using var cold = new FalloutReferenceWorld(records); cold.Restore(dead);
        Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(dead) &&
            cold.Get(state.Reference).PackageBindingFailure!.Error == failure.Error && cold.Get(state.Reference).DeathCount == 1 &&
            cold.Get(state.Reference).Injury is { DeathEventPending: true, DeathEventElapsed: .25 },
            "Cold stopped corpse lost source error, consumed retirement, random, combat history or death state.");
        var lifecycle = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Restore replayed a consumed source prefix."));
        lifecycle.RestoreRetirement(cold.Get(state.Reference).PackageBindingFailure!.Retirement);
        Require(lifecycle.Active is null && lifecycle.LastEvent == "POCA" && lifecycle.Revision == 2,
            "Cold corpse restarted its stopped procedure.");
        foreach (var invalid in new[]
        {
            Replace(dead, state.Reference, value => value with { Ragdoll = null }),
            Replace(dead, state.Reference, value => value with { KnockedDown = true }),
            Replace(dead, state.Reference, value => value with
            { Engagement = value.Engagement! with { WeaponHandling = new(true, [], 123, 456) } }),
            Replace(dead, state.Reference, value => value with { PackageBindingFailure = failure with { PackageSha256 = new string('0', 64) } }),
            Replace(dead, state.Reference, value => value with { Engagement = value.Engagement! with { Target = Key(0x12) } })
        }) AtomicReject(records, invalid);
        var corpseHit = cold.DamageActor(state.Reference, Key(0x902), 0, 10, 1, 1);
        Require(!corpseHit.Died && corpseHit.HealthDamage == 0 && cold.Get(state.Reference).DeathCount == 1,
            "Cold stopped corpse repeated death or restarted living damage.");
        state.PackageBindingFailure = null;
        state.SelectionFailure = new(Key(0x25), Hash(records, 0x25), 0, "Source predicate remains unowned.",
            [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4], failure.AiRandomState, failure.PollRemaining,
            failure.ScheduleTime, true, failure.Retirement, null);
        state.ProcedureCaptureBlocker = state.SelectionFailure.Error;
        var selectedDead = Roundtrip(world.Capture());
        using var selectedCold = new FalloutReferenceWorld(records); selectedCold.Restore(selectedDead);
        Require(JsonSerializer.Serialize(selectedCold.Capture()) == JsonSerializer.Serialize(selectedDead),
            "Stopped selection lost its separate source condition, fault or corpse/combat history.");
        AtomicReject(records, Replace(selectedDead, state.Reference, value => value with { Ragdoll = null }));
        AtomicReject(records, Replace(selectedDead, state.Reference, value => value with
        { SelectionFailure = value.SelectionFailure! with { Sha256 = new string('0', 64) } }));
        Console.WriteLine("OPENNV_STOPPED_POSE_CONTRACT_PASS sourceFailureVisible=true stoppedCombatIdle=true deathAndHistory=true consumedPrefix=true coldExact=true atomicRejection=true nativePhysics=unverified");
    }

    private static void ActiveFurnitureIdle(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        var owner = world.Get(Key(0x900)); var package = records.GetEffective(Key(0x21));
        var source = FalloutScriptPackage.Read(package);
        var selection = new FalloutIdleCollectionPlayback(source, new(), _ => true);
        Require(selection.Select() == Key(0x30), "Fixture lost its real source collection selection.");
        (float Time, string Value)[] keys = [(0, "begin"), (.5f, "StartLoop"), (.75f, "pulse"), (1.5f, "EndLoop"), (2, "end")];
        FalloutIdleAnimationPlayback Clock() => new(0, 2, 1, 2, keys, 4);
        var warmClock = Clock(); warmClock.Advance(3.25);
        var active = new FalloutActorPackageIdleAnimation(Key(0x30), Hash(records, 0x30),
            "meshes/fixture/seat-overlay.kf", new string('d', 64), warmClock.Capture(), 7, ResidualPoseContracts());
        var idles = new FalloutActorPackageIdleState(package.FormKey, Hash(records, 0x21), selection.Capture(),
            [new(Key(0x30), Hash(records, 0x30), 5.75f)], null, active);
        var assignment = new FalloutActorPackageAssignment(package.FormKey, idles.Sha256, true);
        float[] pose = [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4];
        var seat = new FalloutFurnitureSeat(Key(0x40), 0, 1, new(new(0, 0, 0), 0, 1, 1), [0, 0, 0], 0);
        var continuation = new FalloutActorFurnitureContinuation(assignment, 2, "POEA", package.FormKey,
            idles.Sha256, 3, 0, pose, package.FormKey, null, 0x12345678, 3.125, null, null,
            Key(0x901), Hash(records, 0x40), "meshes/fixture/chair.nif", new string('e', 64), seat, pose,
            new(Key(0x31), Hash(records, 0x31), "meshes/fixture/chair-base.kf", new string('f', 64)), IdleState: idles);
        owner.PackageAssignment = assignment; owner.FurnitureContinuation = continuation;
        owner.ProcedureCaptureBlocker = FalloutActorFurnitureContinuation.CaptureBlocker;
        owner.Animation.Change(continuation.Clip!.Resource, continuation.Clip.Sha256); owner.Animation.Advance(9.5);
        var saved = Roundtrip(world.Capture());
        using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
        var restored = cold.Get(owner.Reference).FurnitureContinuation!.IdleState!.ActiveAnimation!;
        Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved) &&
            cold.OwnsFurnitureSeat(Key(0x901), 0, owner.Reference) && restored.Clock.CompletedRepeats == 2 &&
            restored.Clock.SelectedAdditionalLoops == 4 && restored.Clock.RemainingAdditionalLoops == 2,
            "Cold occupied collection pose lost its seat, chosen repeats, phase, replay delay or base clock.");
        var coldClock = Clock(); coldClock.Restore(restored.Clock);
        foreach (var delta in new[] { .125, .5, 1.25, 2d })
        {
            var warmKeys = new List<string>(); var coldKeys = new List<string>();
            Action<FalloutIdleAnimationInterval> Crossed(List<string> target) => interval => target.AddRange(keys.Where(key =>
                (key.Time > interval.From || interval.IncludeFrom && key.Time == interval.From) && key.Time <= interval.To).Select(key => key.Value));
            var first = warmClock.Advance(delta, Crossed(warmKeys)); var second = coldClock.Advance(delta, Crossed(coldKeys));
            Require(first == second && warmKeys.SequenceEqual(coldKeys) && warmClock.Capture() == coldClock.Capture(),
                "Restored collection idle redrew loops or changed its remaining text-key suffix.");
        }
        Require(warmClock.Complete, "Fixture did not cover the finite outro and released pose.");
        foreach (var invalid in new[]
        {
            idles with { ActiveAnimation = active with { IdleSha256 = new string('0', 64) } },
            idles with { ActiveAnimation = active with { Resource = "meshes/fixture/wrong.kf" } },
            idles with { ActiveAnimation = active with { Idle = Key(0x32), IdleSha256 = Hash(records, 0x32) } },
            idles with { ActiveAnimation = active with { Clock = active.Clock with { SelectedAdditionalLoops = 6 } } },
            idles with { Collection = new(0, 0, 0, false) }, idles with { Collection = new(1, 1, 0, true) },
            idles with { Error = "Unowned overlay fault." },
            idles with { ActiveAnimation = active with { ResidualPose = null } },
            idles with { ActiveAnimation = active with { Clock = new(2, 4, 0, 4, false, true) } }
        }) AtomicReject(records, Replace(saved, owner.Reference, value => value with
        { FurnitureContinuation = continuation with { IdleState = invalid } }));
        Reject(() => Clock().Restore(active.Clock with { SourceSeconds = 2.5 }));
        Reject(() => Clock().Restore(active.Clock with { RemainingAdditionalLoops = 3 }));
        Reject(() => (active with { Idle = Key(0x33), IdleSha256 = Hash(records, 0x33),
            Resource = "meshes/fixture/with-object.kf" }).Validate(records, Key(0x24)));
        Console.WriteLine("OPENNV_OCCUPIED_IDLE_CONTRACT_PASS sourceCollection=true chosenLoops=true occupiedSeat=true baseClockIndependent=true residualComponents=true textKeySuffix=true finiteOutro=true atomicRejection=true nativePose=unverified");
    }

    private static FalloutActorResidualPose ResidualPoseContracts()
    {
        var coverage = FalloutNifTransformCoverage.Resolve(3,
        [
            new(0, 10, 1, FalloutNifTransformComponents.All), new(0, 20, 0, FalloutNifTransformComponents.All),
            new(1, 20, 0, FalloutNifTransformComponents.All), new(1, 20, 1, FalloutNifTransformComponents.Rotation),
            new(2, 10, 1, FalloutNifTransformComponents.Rotation), new(2, 10, 1, FalloutNifTransformComponents.Translation),
        ]);
        Require(coverage.SequenceEqual(new[] { FalloutNifTransformComponents.None, FalloutNifTransformComponents.Rotation,
            FalloutNifTransformComponents.Rotation | FalloutNifTransformComponents.Translation }),
            "Winning zero weight revived a lower-priority channel or erased a same-priority published component.");
        Reject(() => FalloutNifTransformCoverage.Resolve(1, [new(1, 0, 1, FalloutNifTransformComponents.All)]));
        Reject(() => FalloutNifTransformCoverage.Resolve(1, [new(0, 0, float.NaN, FalloutNifTransformComponents.All)]));
        Reject(() => FalloutNifTransformCoverage.Resolve(1, [new(0, 0, -1, FalloutNifTransformComponents.All)]));
        var saved = new FalloutActorResidualPose("meshes/fixture/skeleton.nif", new string('a', 64),
        [
            new(1, "Uncontrolled", [1.125f, 2, 3], [0, 0, 0, 1], [1, 1, 1]),
            new(2, "RotationOnly", [-.125f, 0, .5f], Scale: [1, 1, 1]),
        ]);
        (string, FalloutNifTransformComponents)[] source =
        [
            ("Root", FalloutNifTransformComponents.All),
            ("Uncontrolled", FalloutNifTransformComponents.None),
            ("RotationOnly", FalloutNifTransformComponents.Rotation),
        ];
        saved.ValidateBinding(saved.SkeletonResource, saved.SkeletonSha256, source);
        var copied = saved.Copy();
        Require(JsonSerializer.Serialize(copied) == JsonSerializer.Serialize(saved) &&
            !ReferenceEquals(copied.Bones[0].Position, saved.Bones[0].Position),
            "Residual pose cloning rounded raw components or kept mutable storage.");
        foreach (var invalid in new[]
        {
            saved with { SkeletonResource = "meshes/fixture/other-skeleton.nif" },
            saved with { SkeletonSha256 = new string('0', 64) },
            saved with { Bones = saved.Bones.Take(1).ToArray() },
            saved with { Bones = [saved.Bones[1], saved.Bones[0]] },
            saved with { Bones = [saved.Bones[0], saved.Bones[0]] },
            saved with { Bones = [saved.Bones[0] with { Name = "WrongSourceBone" }, saved.Bones[1]] },
            saved with { Bones = [saved.Bones[0] with { Index = 3 }, saved.Bones[1]] },
            saved with { Bones = [saved.Bones[0] with { Position = [1, 2] }, saved.Bones[1]] },
            saved with { Bones = [saved.Bones[0] with { Rotation = [0, 0, 0, 0] }, saved.Bones[1]] },
            saved with { Bones = [saved.Bones[0] with { Scale = [1, 0, 1] }, saved.Bones[1]] },
            saved with { Bones = [saved.Bones[0] with { Position = [float.NaN, 0, 0] }, saved.Bones[1]] },
            saved with { Bones = [saved.Bones[0], saved.Bones[1] with { Rotation = [0, 0, 0, 1] }] },
        }) Reject(() => invalid.ValidateBinding(saved.SkeletonResource, saved.SkeletonSha256, source));
        var changedCoverage = source.ToArray(); changedCoverage[1].Item2 = FalloutNifTransformComponents.Translation;
        Reject(() => saved.ValidateBinding(saved.SkeletonResource, saved.SkeletonSha256, changedCoverage));
        var completeCoverage = source.ToArray(); completeCoverage[2].Item2 = FalloutNifTransformComponents.All;
        Reject(() => saved.ValidateBinding(saved.SkeletonResource, saved.SkeletonSha256, completeCoverage));
        return saved;
    }

    private static FalloutReferenceSnapshot[] Roundtrip(IReadOnlyList<FalloutReferenceSnapshot> values) =>
        JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(values))!;
    private static FalloutReferenceSnapshot[] Replace(IReadOnlyList<FalloutReferenceSnapshot> values, FalloutFormKey actor,
        Func<FalloutReferenceSnapshot, FalloutReferenceSnapshot> change) => values.Select(value => value.Reference == actor ? change(value) : value).ToArray();
    private static void AtomicReject(FalloutPluginStack records, FalloutReferenceSnapshot[] candidate)
    {
        using var rejected = new FalloutReferenceWorld(records); Reject(() => rejected.Restore(candidate));
        Require(rejected.InstanceCount == 0, "Invalid composed continuation partially published the world.");
    }
    private static string Hash(FalloutPluginStack records, uint id) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(id)));
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidDataException("Invalid composed stopped/idle continuation was admitted.");
    }
    private static byte[] Npc(uint id)
    {
        var data = new byte[11]; BinaryPrimitives.WriteInt32LittleEndian(data, 100);
        var acbs = new byte[24]; UInt(acbs, 0, 0x10); BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(8), 1);
        return Record("NPC_", id, Field("ACBS", acbs), Field("DATA", data), Field("NAM4", BitConverter.GetBytes(6u)),
            Field("PKID", BitConverter.GetBytes(0x25u)));
    }
    private static byte[] Part()
    {
        var data = new byte[84]; Float(data, 0, 1); data[4] = 1; data[6] = 100;
        Float(data, 24, 1); Float(data, 40, 1); Float(data, 80, 1);
        return Join(Field("BPNN", Text("Root")), Field("BPNT", Text("Root")), Field("BPND", data));
    }
    private static byte[] Reference(string kind, uint id, uint actor) => Record(kind, id, Field("NAME", BitConverter.GetBytes(actor)), Field("DATA", new byte[24]));
    private static byte[] Setting(uint id, string name, float value) =>
        Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Float(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string kind, uint id, params byte[][] fields)
    {
        if (kind == "PACK") fields = [Field("EDID", Text("SourcePackage" + id)), .. fields];
        var payload = Join(fields); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(kind).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)payload.Length); UInt(bytes, 12, id); payload.CopyTo(bytes, 24); return bytes;
    }
}
