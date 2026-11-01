using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

internal static class FollowPackageContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-follow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            File.WriteAllBytes(Path.Combine(directory, "Follow.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Package(1, 330), Package(2, 500), Package(3, 330, location: true), Package(4, 0), Package(5, 330, targetType: 1),
                Package(6, 128, procedure: 7, flags: 0x04501000), Package(7, 128, procedure: 7, flags: 0x04501001),
                Package(8, 128, location: true, procedure: 7), Package(9, 0, procedure: 7),
                Package(10, 128, targetType: 1, procedure: 7), Package(11, 128, procedure: 7, specificFlags: 1),
                Package(15, 128, targetType: 3, flags: 0x04501200),
                Package(12, 128, procedure: 7, flags: 0x3000), Package(13, 128, flags: 1), Package(14, 512, flags: 0x04400000)));
            using var records = FalloutPluginStack.Load(directory, ["Follow.esm"]);
            FalloutPluginRecord Source(uint id) => records.GetEffective(new("Follow.esm", id));
            var linkedFollow = FalloutFollowContinuation.ReadResident(Source(15));
            if (linkedFollow.TargetKind != 3 || linkedFollow.Distance != 128 || !linkedFollow.ContinueIfPlayerNear ||
                linkedFollow.ResolveTarget(() => new FalloutFormKey("Follow.esm", 0x14)).Target != new FalloutFormKey("Follow.esm", 0x14) ||
                !linkedFollow.RetainsSelection(true, false) || linkedFollow.RetainsSelection(false, false) || linkedFollow.RetainsSelection(true, true))
                throw new InvalidOperationException("Linked Follow lost its source target or qualified retention flag.");
            Reject(() => linkedFollow.ResolveTarget(() => null));
            var shortFollow = FalloutFollowPackage.Read(Source(1));
            if (shortFollow.Target != new FalloutFormKey("Follow.esm", 0x14) || shortFollow.Distance != 330 ||
                FalloutFollowPackage.Read(Source(2)).Distance != 500 || FalloutScriptPackage.Read(Source(1)).LocationType is not null)
                throw new InvalidOperationException("Follow invented a start location or lost its target/distance.");
            Reject(() => FalloutFollowPackage.Read(Source(3)));
            Reject(() => FalloutFollowPackage.Read(Source(4)));
            Reject(() => FalloutFollowPackage.Read(Source(5)));
            var accompany = FalloutFollowPackage.ReadAccompany(Source(6));
            if (!accompany.Accompany || !accompany.DefensiveCombat || accompany.Running ||
                accompany.Target != shortFollow.Target || accompany.Distance != 128 || accompany.Flags != 0x04501000 ||
                accompany.AllowsUnprovokedCombat(null) || accompany.AllowsUnprovokedCombat(Source(1).FormKey) ||
                !accompany.AllowsUnprovokedCombat(accompany.Target) || !shortFollow.AllowsUnprovokedCombat(null) ||
                !FalloutFollowPackage.ReadAccompany(Source(12)).Running)
                throw new InvalidOperationException("Accompany lost its target, distance or defensive target protection policy.");
            Reject(() => FalloutFollowPackage.ReadAccompany(Source(1)));
            Reject(() => FalloutFollowPackage.Read(Source(6)));
            foreach (var id in new uint[] { 7, 8, 9, 10, 11 }) Reject(() => FalloutFollowPackage.ReadAccompany(Source(id)));
            if (FalloutFollowContinuation.ReadResident(Source(1)).Form != shortFollow.Form ||
                !FalloutFollowContinuation.ReadResident(Source(6)).Accompany)
                throw new InvalidOperationException("Resident actor follower lost source type or target binding.");
            Reject(() => FalloutFollowContinuation.ReadResident(Source(13)));
            var defensiveFollow = FalloutFollowContinuation.ReadResident(Source(14));
            if (defensiveFollow.Accompany || !defensiveFollow.RequiresDefensiveCombatOwner ||
                defensiveFollow.Distance != 512 || accompany.RequiresDefensiveCombatOwner || shortFollow.RequiresDefensiveCombatOwner)
                throw new InvalidOperationException("Follow defensive source admission conflated movement with combat policy.");
            Reject(() => defensiveFollow.AllowsUnprovokedCombat(null));
            Reject(() => defensiveFollow.AllowsUnprovokedCombat(defensiveFollow.Target));
            var leader = Godot.Vector3.Zero; var goal = new Godot.Vector3(100, 0, 0);
            var lateral = RuntimeNativeCreature.AccompanyApproach(new(100, 0, 50), leader, goal, 2);
            var behind = RuntimeNativeCreature.AccompanyApproach(new(-10, 0, 0), leader, goal, 2);
            if (lateral.Destination != leader || lateral.Distance != 2 ||
                behind.Destination != goal || behind.Distance != 98)
                throw new InvalidOperationException("Accompany destination knowledge lost the actual leader distance.");
            var motion = new FalloutActorPackageMotion(shortFollow.Form, new string('a', 64), "meshes/actor/mtforward.kf",
                new string('b', 64), 3.25, false, [2, 3, 4], [0, 0, 0, 1]);
            var completedTarget = motion with { Travel = new(new("Follow.esm", 0x50), [7, 8, 9], true) };
            var completedAssignment = new FalloutActorPackageAssignment(motion.Package, motion.PackageSha256, true);
            var retainedPosition = FalloutFollowContinuation.CompletedTravelTarget(completedAssignment, completedTarget)!;
            if (!retainedPosition.SequenceEqual(motion.Position))
                throw new InvalidOperationException("Retired completed target lost its actual motion position.");
            retainedPosition[0] = 99;
            if (completedTarget.Position[0] != 2 ||
                FalloutFollowContinuation.CompletedTravelTarget(completedAssignment with { Done = false }, completedTarget) is not null ||
                FalloutFollowContinuation.CompletedTravelTarget(completedAssignment, motion) is not null ||
                FalloutFollowContinuation.CompletedTravelTarget(completedAssignment, completedTarget with
                { Travel = completedTarget.Travel! with { Complete = false } }) is not null)
                throw new InvalidOperationException("Retained target aliased motion or invented a stopped process.");
            Reject(() => FalloutFollowContinuation.CompletedTravelTarget(completedAssignment with { Sha256 = new string('c', 64) }, completedTarget));
            var coldTarget = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(completedTarget))!;
            if (!FalloutFollowContinuation.CompletedTravelTarget(completedAssignment, coldTarget)!.SequenceEqual(motion.Position))
                throw new InvalidOperationException("Cold completed target lost actual retained position.");
            var restored = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(motion))!;
            restored.Validate();
            if (restored.Seconds != 3.25 || !restored.Position.SequenceEqual(motion.Position))
                throw new InvalidOperationException("Follow persistence lost motion state.");
            Reject(() => (motion with { Seconds = double.NaN }).Validate());
            Reject(() => (motion with { Rotation = [0, 0, 0, 0] }).Validate());
            Reject(() => (motion with { StartPending = true }).Validate());
            Reject(() => (motion with { PackageSha256 = "changed" }).Validate());
            var accompanyMotion = motion with { Package = accompany.Form };
            var coldAccompany = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(accompanyMotion))!;
            coldAccompany.Validate();
            if (coldAccompany.Package != accompany.Form || coldAccompany.Seconds != accompanyMotion.Seconds ||
                !coldAccompany.Position.SequenceEqual(accompanyMotion.Position))
                throw new InvalidOperationException("Accompany cold motion lost its source identity, clock or pose.");
            foreach (var source in new[] { Source(1), Source(6), Source(14) })
            {
                var accepted = motion with
                {
                    Package = source.FormKey,
                    PackageSha256 = Convert.ToHexString(SHA256.HashData(source.ReadData()))
                };
                FalloutFollowContinuation.ValidateMotion(source, accepted);
                var eventsSeen = new List<string>();
                var live = new FalloutPackageEvents((_, kind) => eventsSeen.Add(kind));
                live.Change(FalloutScriptPackage.Read(source));
                var assignment = FalloutActorPackageAssignment.Capture(records, live)!;
                if (!eventsSeen.SequenceEqual(["POBA"]) || assignment.Done)
                    throw new InvalidOperationException("Follower acceptance did not start its source lifecycle once.");
                var coldEvents = new FalloutPackageEvents((_, kind) => eventsSeen.Add(kind));
                FalloutFollowContinuation.BindAccepted(records, source,
                    JsonSerializer.Deserialize<FalloutActorPackageAssignment>(JsonSerializer.Serialize(assignment))!,
                    JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(accepted))!, coldEvents);
                coldEvents.Change(FalloutScriptPackage.Read(source));
                if (!eventsSeen.SequenceEqual(["POBA"]) || coldEvents.Done || coldEvents.Active?.Form != source.FormKey)
                    throw new InvalidOperationException("Cold follower binding replayed start or invented completion while waiting.");
                coldEvents.Change(null);
                if (!eventsSeen.SequenceEqual(["POBA", "POCA"]))
                    throw new InvalidOperationException("Follower replacement did not retire its accepted source lifecycle.");
                Reject(() => FalloutFollowContinuation.ValidateMotion(source, accepted with { PackageSha256 = new string('0', 64) }));
                Reject(() => FalloutFollowContinuation.ValidateMotion(source, accepted with { DialogueCompleted = true }));
                Reject(() => FalloutFollowContinuation.BindAccepted(records, source, assignment with { Done = true }, accepted,
                    new FalloutPackageEvents((_, _) => throw new InvalidOperationException("Rejected follower replayed an event."))));
            }
            Console.WriteLine("OPENNV_RESIDENT_FOLLOW_CONTINUATION_CONTRACT_PASS type1And7=true sourceHash=true retainedPose=true freshStartOnce=true coldNoReplay=true waitingNotComplete=true replacementChange=true wrongProcedureRefused=true defensiveMovementAdmitted=true defensiveCombatExplicitlyRefused=true");
            Console.WriteLine("OPENNV_FOLLOW_PACKAGE_CONTRACT_PASS optionalStart=true sourceTarget=true distances=true coldClock=true accompany=true defensiveTarget=true malformedRejected=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Package(uint id, int distance, bool location = false, int targetType = 0,
        byte procedure = 1, uint flags = 0, ushort specificFlags = 0)
    {
        var data = new byte[12]; data[4] = procedure; data[11] = 0xa5;
        BinaryPrimitives.WriteUInt32LittleEndian(data, flags);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), specificFlags);
        var target = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(target, targetType);
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(4), 0x14);
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(8), distance); target[15] = 0xcc;
        return Record("PACK", id, Field("EDID", Encoding.ASCII.GetBytes("SourceFollow\0")), Field("PKDT", data),
            Field("PTDT", target), location ? Field("PLDT", new byte[12]) : []);
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid follow source/state was accepted.");
    }
}
