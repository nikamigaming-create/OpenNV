using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class SandboxAdmissionFailureContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-sandbox-admission-").FullName;
        var path = Path.Combine(directory, "Admission.esm");
        try
        {
            var data = new byte[12]; data[4] = 12;
            var location = new byte[12]; location[0] = 3;
            var reference = Record("ACHR", 0x900, Field("NAME", BitConverter.GetBytes(0x10u)), Field("DATA", new byte[24]));
            var group = new byte[24 + reference.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 6); reference.CopyTo(group, 24);
            byte[] Package(uint id, bool idle) => Record("PACK", id,
                Field("EDID", Encoding.ASCII.GetBytes("Sandbox\0")), Field("PKDT", data), Field("PLDT", location),
                Field("IDLC", [idle ? (byte)1 : (byte)0]), Field("IDLA", idle ? BitConverter.GetBytes(0x30u) : []),
                Field("POBA", []));
            File.WriteAllBytes(path, Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 0x10, Field("ACBS", new byte[24]), Field("PKID", BitConverter.GetBytes(0x20u)),
                    Field("PKID", BitConverter.GetBytes(0x21u))), Package(0x20, false), Package(0x21, true),
                Record("CELL", 0x800, Field("DATA", [1, 0])), group));
            using var records = FalloutPluginStack.Load(directory, ["Admission.esm"]);
            FalloutFormKey Key(uint id) => new("Admission.esm", id);
            string Hash(uint id) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(id)));
            var actor = new FalloutReferenceInstance(records.GetEffective(Key(0x900)), null);
            var effects = 0;
            void Dispatch(FalloutScriptPackage package, string kind)
            {
                if (kind == "POBA" && package.EventPrograms.ContainsKey(kind)) effects++;
            }
            var live = new FalloutPackageEvents(Dispatch);
            live.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x20))));
            Require(effects == 1 && !live.Done && live.Error is null, "POBA effect did not execute exactly once.");
            var saved = new FalloutActorSandboxAdmissionFailure(Key(0x20), Hash(0x20),
                FalloutActorSandboxAdmissionFailure.AdmissionErrorPrefix + "IdleMarker reference=Admission.esm:000901 base=Admission.esm:000030 inventory=False",
                [3, 4, 5], [1, 0, 0, 0, 1, 0, 0, 0, 1],
                0x123456789abcdef0, .25, new(1, 10, 3, 9), 4, 2,
                new(false, true, false, false, false, false, 2), live.Revision, live.LastEvent!,
                live.LastPackage!.Value, Hash(0x20));
            saved.Validate(records, actor);
            var cold = JsonSerializer.Deserialize<FalloutActorSandboxAdmissionFailure>(JsonSerializer.Serialize(saved))!;
            var restored = new FalloutPackageEvents(Dispatch);
            cold.RestoreEvents(records, actor, restored);
            Require(effects == 1 && restored.Active?.Form == saved.Package && !restored.Done &&
                restored.Error is null && restored.Revision == live.Revision && restored.LastEvent == live.LastEvent &&
                restored.LastPackage == live.LastPackage && JsonSerializer.Serialize(cold) == JsonSerializer.Serialize(saved),
                "Cold admission restore replayed POBA or changed the stopped state.");
            var copy = saved.Copy(); copy.Position[0]++; copy.Basis[0]++;
            Require(saved.Position[0] == 3 && saved.Basis[0] == 1, "Admission copy aliases its pose arrays.");
            foreach (var invalid in new[]
            {
                saved with { PackageSha256 = new('0', 64), LastPackageSha256 = new('0', 64) },
                saved with { LastPackageSha256 = new('0', 64) }, saved with { LastPackage = Key(0x21) },
                saved with { EventRevision = 0 }, saved with { EventRevision = long.MaxValue },
                saved with { LastEvent = "POCA" }, saved with { LastEvent = "POEA" },
                saved with { Error = "" }, saved with { Basis = new float[9] },
                saved with { Error = FalloutActorSandboxAdmissionFailure.AdmissionErrorPrefix },
                saved with { Error = "Sandbox location needs an other-region approach owner." },
                saved with { Activity = saved.Activity with { Revision = -1 } },
                saved with { Package = Key(0x21), PackageSha256 = Hash(0x21), LastPackage = Key(0x21), LastPackageSha256 = Hash(0x21) }
            }) RejectFresh(invalid);

            actor.PackageMotion = new(Key(0x20), Hash(0x20), "meshes/fixture/idle.kf", new('a', 64),
                0, false, [0, 0, 0], [0, 0, 0]);
            RejectFresh(saved); actor.PackageMotion = null;
            actor.PackageStarts.Add(new(Key(0x20), Hash(0x20), 1));
            actor.PackageAssignment = new(Key(0x20), Hash(0x20), false);
            var assigned = new FalloutPackageEvents(Dispatch);
            cold.RestoreEvents(records, actor, assigned);
            Require(assigned.Active?.Form == saved.Package && effects == 1 && actor.PackageStarts.Count == 1,
                "Matching active assignment or prior package-start history was lost during restore.");
            actor.PackageAssignment = new(Key(0x20), Hash(0x20), true);
            RejectFresh(saved);
            actor.PackageAssignment = new(Key(0x21), Hash(0x21), false);
            RejectFresh(saved); actor.PackageAssignment = null;
            actor.PackageIdle = new(Key(0x20), Hash(0x20), "POBA", Key(0x30), new('a', 64),
                "meshes/fixture/idle.kf", new('b', 64), new(0, 0, 0, 0, false, false));
            RejectFresh(saved); actor.PackageIdle = null;
            Reject(() => saved.RestoreEvents(records, actor, restored));
            Require(restored.Revision == 1 && effects == 1, "Occupied event owner was partially overwritten.");
            var faulted = new FalloutPackageEvents((_, _) => throw new InvalidDataException("POBA fault"));
            Reject(() => faulted.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x20)))));
            Reject(() => saved.RestoreEvents(records, actor, faulted));
            Require(faulted.Error == "POBA fault" && faulted.Revision == 1, "Faulted event history was overwritten.");
            using var world = new FalloutReferenceWorld(records);
            var worldActor = world.Get(Key(0x900));
            worldActor.Animation.Change("meshes/fixture/mtidle.kf", new('a', 64));
            worldActor.Animation.Advance(6.25);
            worldActor.SoundRandom.Restore(0x1122334455667788);
            worldActor.ConversationHistory = new(12.5f, [worldActor.Reference]);
            worldActor.ConversationSuppressionSeconds = 3.75f;
            worldActor.PackageAssignment = new(saved.Package, saved.PackageSha256, false);
            worldActor.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + saved.Error;
            worldActor.CanCaptureSandboxAdmissionFailure = () => true;
            var spatialSaved = saved with { Basis = [0, 0, 1, 0, 1, 0, -1, 0, 0] };
            worldActor.CaptureSandboxAdmissionFailure = () => spatialSaved.Copy();
            var snapshotJson = JsonSerializer.Serialize(world.Capture());
            var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(snapshotJson)!;
            using var coldWorld = new FalloutReferenceWorld(records);
            coldWorld.Restore(snapshots);
            Require(JsonSerializer.Serialize(coldWorld.Capture()) == snapshotJson,
                "World cold restore changed base animation, shared RNG, conversation history or admission state.");
            var resumed = coldWorld.Get(worldActor.Reference);
            var playerPose = new FalloutReferencePlacement(Key(0x800), [13, -5, 4], [0, 0, 0]);
            var stoppedPose = coldWorld.SpawnPlacement(resumed.Reference, playerPose, 1);
            Require(stoppedPose.Position.SequenceEqual(new float[] { 3, -5, 4 }) &&
                Math.Abs(stoppedPose.RotationRadians[2] - MathF.PI / 2) < .0001f &&
                Math.Abs(coldWorld.Distance(resumed.Reference, records.RuntimeFormKey(0x14), playerPose, 1) - 10) < .0001f &&
                Math.Abs(coldWorld.HeadingAngle(resumed.Reference, records.RuntimeFormKey(0x14), playerPose, 1)) < .0001f,
                "Offcell distance or heading used the authored origin instead of the retained moved and rotated pose.");
            var unloadedExecutions = 0;
            coldWorld.UnloadedPackages = new(records, coldWorld, new(records), null, null,
                (_, _) => unloadedExecutions++, () => 1);
            Require(coldWorld.CurrentPackage(resumed.Reference) == saved.Package && unloadedExecutions == 0 &&
                coldWorld.PendingProcedureCaptureCount == 0 && coldWorld.PendingPackageEventCount == 0,
                "Unloaded admission query reelected or replayed the stopped package.");
            var bound = new FalloutPackageEvents(Dispatch);
            coldWorld.UnloadedPackages.BindNative(resumed.Reference, bound);
            Require(bound.Active is null && bound.Revision == 0,
                "Unloaded binding stole the native stopped-history restoration.");
            resumed.SandboxAdmissionFailure!.RestoreEvents(records, resumed, bound);
            coldWorld.UnloadedPackages.Retain(resumed.Reference, bound);
            Require(coldWorld.CurrentPackage(resumed.Reference) == saved.Package && effects == 1 && unloadedExecutions == 0,
                "Retired stopped actor reelected its active package.");
            var rebound = new FalloutPackageEvents(Dispatch);
            coldWorld.UnloadedPackages.BindNative(resumed.Reference, rebound);
            resumed.SandboxAdmissionFailure.RestoreEvents(records, resumed, rebound);
            Require(rebound.Revision == saved.EventRevision && rebound.LastEvent == "POBA" && !rebound.Done &&
                effects == 1 && unloadedExecutions == 0 && JsonSerializer.Serialize(coldWorld.Capture()) == snapshotJson,
                "Retain-to-native handoff changed the stopped lifecycle or consumed results again.");
            using var rejectedWorld = new FalloutReferenceWorld(records);
            var invalidSnapshots = snapshots.Select(value => value with
            { SandboxAdmissionFailure = saved with { PackageSha256 = new('0', 64), LastPackageSha256 = new('0', 64) } }).ToArray();
            Reject(() => rejectedWorld.Restore(invalidSnapshots));
            Require(rejectedWorld.Capture().Count == 0,
                "Rejected Sandbox source restore partially committed a world.");
            var snapshot = snapshots.Single();
            foreach (var invalid in new[]
            {
                snapshot with { SoundRandomState = null }, snapshot with { Animation = null },
                snapshot with { Unconscious = true }, snapshot with { Restrained = true },
                snapshot with { KnockedDown = true },
                snapshot with { SandboxAdmissionFailure = saved with { Activity = saved.Activity with { InCombat = true } } },
                snapshot with { PackageMotion = new(saved.Package, saved.PackageSha256,
                    "meshes/fixture/mtidle.kf", new('a', 64), 0, false, [0, 0, 0], [0, 0, 0]) },
                snapshot with { PackageBindingFailure = new(saved.Package, saved.PackageSha256,
                    "Pre-begin failure", [0, 0, 0], [1, 0, 0, 0, 1, 0, 0, 0, 1], false, 1, 0, null,
                    new(0, null, null, null)) }
            })
            {
                using var rejected = new FalloutReferenceWorld(records);
                Reject(() => rejected.Restore([invalid]));
                Require(rejected.Capture().Count == 0,
                    "Conflicting Sandbox snapshot partially committed a world.");
            }
            var previous = JsonSerializer.Deserialize<FalloutNativeCampaignState>(JsonSerializer.Serialize(new
            {
                Schema = FalloutNativeCampaignSave.BeforeSandboxAdmissionSchema,
                References = new[] { snapshot with { SandboxAdmissionFailure = null } }
            }))!;
            var upgraded = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(previous);
            Require(upgraded.Schema == FalloutNativeCampaignSave.ExpectedSchema &&
                ReferenceEquals(upgraded.References, previous.References),
                "Prior schema admission changed existing reference state.");
            Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(previous with { References = [snapshot] }));
            var current = previous with { Schema = FalloutNativeCampaignSave.ExpectedSchema, References = [snapshot] };
            Require(ReferenceEquals(FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(current), current),
                "Current schema admission replaced stopped Sandbox state.");
            Console.WriteLine("OPENNV_SANDBOX_ADMISSION_FAILURE_CONTRACT_PASS countedBeginDispatch=true compiledResultProof=false " +
                "coldWorldRestoreNoReplay=true unloadedHandoff=true sourceAndConflictRejection=true " +
                "atomicEventRestore=true exclusiveSnapshot=true priorSchemaGuard=true gameplay=unverified");

            void RejectFresh(FalloutActorSandboxAdmissionFailure invalid)
            {
                var target = new FalloutPackageEvents(Dispatch);
                Reject(() => invalid.RestoreEvents(records, actor, target));
                Require(target.Active is null && !target.Done && target.Revision == 0 && target.LastEvent is null &&
                    target.LastPackage is null && target.Error is null && effects == 1,
                    "Rejected admission restore mutated event state or dispatched an effect.");
            }
        }
        finally { File.Delete(path); Directory.Delete(directory, recursive: false); }
    }

    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6);
        return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24);
        return bytes;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid Sandbox admission continuation was accepted.");
    }
}
