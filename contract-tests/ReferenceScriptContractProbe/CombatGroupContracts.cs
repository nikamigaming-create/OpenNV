using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class CombatGroupContracts
{
    private static readonly FalloutFormKey Player = Key(0x14);
    private static readonly string[] Executables =
        ["518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
         "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"];

    internal static void Run()
    {
        foreach (var executable in Executables)
        {
            var source = FalloutCombatGroupDeclaration.ForExecutable(executable);
            Lists(source); MergeAndSplit(source); Failures(source);
        }
        SourceWorld();
        Console.WriteLine("OPENNV_COMBAT_GROUP_CONTRACT_PASS personalDistinctFromIncoming=true detectionDecision=true retainedAdmission=true orderedTargets=true directedMerge=true rejectedMergeAtomic=true playerSplit=true exactRemoval=true coldNoReplay=true activeOmissionRefused=true reentryRetained=true opaqueTransitionRefused=true placedNpcCreature=true winningMaster=true sourceDriftRefused=true foreignMasterRefused=true poseCaptureNoReplay=true nativeGrouping=unexecuted retailBehavior=unmatched");
    }

    private static void Lists(FalloutCombatGroupDeclaration source)
    {
        var detection = 0; var detectionCalls = 0;
        var inputs = Inputs(source, detection: _ => { detectionCalls++; return new(detection, "authored-actual-detection-entry"); });
        using var groups = new FalloutCombatGroups(source, "authored-stack", Player, inputs);
        Require(groups.ReadPlayerTargets().CombatGroupTargetCount == 0 && groups.Membership(Player) is null && !groups.ReadPlayerCombatFlag(),
            "Initial actual null personal group was replaced by a proxy group.");
        groups.ObserveTarget(Key(1), null, Player, FalloutCombatGroupTransitionKind.ActorTarget);
        Require(groups.ReadPlayerTargets().CombatGroupTargetCount == 0 && groups.IncomingMemberCount(Player) == 1 && !groups.ReadPlayerCombatFlag(),
            "An incoming hostile group or combat Boolean substituted for personal targets.");
        detection = 3; groups.ObservePlayerTargetCandidate(Key(1));
        var personal = groups.Membership(Player)!.Value;
        groups.ObservePlayerTargetCandidate(Key(2)); groups.ObservePlayerTargetCandidate(Key(1));
        Require(groups.ReadPlayerTargets().CombatGroupTargetCount == 2 &&
            groups.Capture().Groups.Single(group => group.Identity == personal).Targets.SequenceEqual([Key(1), Key(2)]),
            "Personal targets lost uniqueness or source insertion order.");
        detection = -100; groups.ObservePlayerTargetCandidate(Key(1));
        Require(groups.ReadPlayerTargets().CombatGroupTargetCount == 2 &&
            groups.Capture().PlayerCandidates.Single(candidate => candidate.Target == Key(1)) is
                { DetectionLevel: -100, Admission: { DetectionLevel: 3, RemovedSequence: null } },
            "A later negative candidate erased a still-owned admitted target.");
        groups.Advance(.125f);
        Require(groups.ReadPlayerCombatFlag(), "Actual player update did not publish the independent incoming combat flag.");
        var snapshot = RoundTrip(groups.Capture());
        var before = detectionCalls;
        using var cold = new FalloutCombatGroups(source, "authored-stack", Player, inputs, snapshot);
        cold.RequireCurrentTargets([(Key(1), Player)]);
        Require(detectionCalls == before && JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(snapshot),
            "Cold group publication replayed admission or changed the retained list/clock prefix.");
        Reject(() => new FalloutCombatGroups(source, "authored-stack", Player, inputs, snapshot with { Groups = [], Memberships = [] }).Dispose());
        Reject(() => cold.RequireCurrentTargets([(Key(1), Key(3))]));
        groups.RemoveGroupTarget(personal, Key(1)); groups.RemoveGroupTarget(personal, Key(2));
        groups.Advance(0);
        Require(groups.ReadPlayerTargets().CombatGroupTargetCount == 0 && groups.Membership(Player) is null &&
            groups.IncomingMemberCount(Player) == 1 && groups.ReadPlayerCombatFlag() && groups.Capture().PlayerCombatSeconds == 0 &&
            groups.Capture().PlayerCandidates.All(candidate => candidate.Admission?.RemovedSequence is > 0),
            "Exact personal removal incorrectly cleared incoming hostility or left its own clock/member active.");
        Reject(() => FalloutCombatGroups.Validate(snapshot with { Actors = snapshot.Actors.Concat([snapshot.Actors[0]]).ToArray() }));
        Reject(() => FalloutCombatGroups.Validate(snapshot with { Removals = null! }));
        Reject(() => FalloutCombatGroups.Validate(snapshot with { PlayerCombatFlag = false }));
    }

    private static void MergeAndSplit(FalloutCombatGroupDeclaration source)
    {
        var rejectCross = true;
        using var groups = new FalloutCombatGroups(source, "authored-stack", Player,
            Inputs(source, predicate: (member, target) => new(!(rejectCross && member == Key(1) && target == Key(6)), "authored-cross-predicate")));
        groups.ObserveTarget(Key(1), null, Key(5), FalloutCombatGroupTransitionKind.ActorTarget);
        groups.ObserveTarget(Key(2), null, Key(6), FalloutCombatGroupTransitionKind.ActorTarget);
        var destination = groups.Membership(Key(1))!.Value; var oldSource = groups.Membership(Key(2))!.Value;
        var before = groups.Capture();
        Require(!groups.MergeGroups(destination, oldSource) && groups.Membership(Key(2)) == oldSource &&
            JsonSerializer.Serialize(groups.Capture().Groups) == JsonSerializer.Serialize(before.Groups) && groups.SaveBlocker is null,
            "A known incompatible directed merge mutated members or invented an unsupported failure.");
        rejectCross = false;
        Require(groups.MergeGroups(destination, oldSource) && groups.Membership(Key(2)) == destination &&
            groups.Capture().Groups.Single(group => group.Identity == oldSource).Members.Count == 0 &&
            groups.Capture().Groups.Single(group => group.Identity == oldSource).Targets.SequenceEqual([Key(6)]) &&
            groups.IncomingMemberCount(Key(6)) == 2,
            "Directed merge lost old target ownership, new membership or its incoming index.");
        groups.RequireCurrentTargets([(Key(1), Key(5)), (Key(2), Key(6))]);
        using var split = new FalloutCombatGroups(source, "authored-stack", Player,
            Inputs(source, teammate: actor => actor == Key(1)));
        split.ObservePlayerTargetCandidate(Key(5));
        var oldPlayer = split.Membership(Player)!.Value;
        Require(split.JoinActor(Key(1), oldPlayer) && split.JoinActor(Key(2), oldPlayer) && split.JoinActor(Key(3), oldPlayer),
            "Authored compatible members failed to join the current player group.");
        var opposing = split.SplitPlayerGroup(Key(2));
        var currentPlayer = split.Membership(Player)!.Value;
        Require(currentPlayer != oldPlayer && split.Membership(Key(1)) == currentPlayer &&
            split.Membership(Key(3)) == oldPlayer && split.Membership(Key(2)) == opposing &&
            split.Capture().Groups.Single(group => group.Identity == currentPlayer).Targets.SequenceEqual([Key(5), Key(2)]) &&
            split.ReadPlayerTargets().CombatGroupTargetCount == 2,
            "Player split erased unrelated members, chose teammate state or reversed target copy ownership.");
    }

    private static void Failures(FalloutCombatGroupDeclaration source)
    {
        using var absent = new FalloutCombatGroups(source, "authored-stack", Player,
            Inputs(source, detection: _ => new(null, "authored-unowned-detection")));
        absent.ObserveTarget(Key(1), null, Player, FalloutCombatGroupTransitionKind.ActorTarget);
        Require(absent.ReadPlayerTargets().CombatGroupTargetCount is null && absent.SaveBlocker is not null &&
            absent.Capture().Groups.Any(group => group.Targets.Contains(Player)),
            "Unowned detection hid an already committed incoming group or reported settled zero.");
        using var failedCold = new FalloutCombatGroups(source, "authored-stack", Player,
            Inputs(source), RoundTrip(absent.Capture()));
        Require(failedCold.ReadPlayerTargets().CombatGroupTargetCount is null, "Cold publication cleared the original detection failure.");
        using var opaque = new FalloutCombatGroups(source, "authored-stack", Player, Inputs(source));
        opaque.ObserveTarget(Key(1), null, Key(2), FalloutCombatGroupTransitionKind.OpaqueTargetChange);
        Require(opaque.ReadPlayerTargets().CombatGroupTargetCount is null && opaque.Capture().CurrentTargets.Count == 1,
            "Opaque target mutation reported an inert empty group or erased its actual target prefix.");
        FalloutCombatGroups? reentrant = null;
        using var owner = new FalloutCombatGroups(source, "authored-stack", Player,
            Inputs(source, detection: _ =>
            {
                try { reentrant!.ObservePlayerTargetCandidate(Key(2)); }
                catch (InvalidOperationException) { }
                return new(1, "authored-caught-reentry");
            }));
        reentrant = owner;
        owner.ObservePlayerTargetCandidate(Key(1));
        Require(owner.ReadPlayerTargets().CombatGroupTargetCount is null && owner.Capture().OwnerFailure is not null,
            "A callback that caught reentry hid its failed owner and produced a settled count.");
        using var expiry = new FalloutCombatGroups(source, "authored-stack", Player,
            Inputs(source, retirement: (_, _) => new(null, "authored-unowned-expiry")));
        expiry.ObservePlayerTargetCandidate(Key(1)); expiry.Advance(.0625f);
        Require(expiry.ReadPlayerTargets().CombatGroupTargetCount is null && expiry.Capture().Groups.Single().Targets.Count == 1,
            "An unowned timer/detection expiry silently removed an actual target.");
    }

    private static void SourceWorld()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-combat-groups-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Actors.esm"), Join(Header(), Actor("NPC_", 7, 3), Actor("NPC_", 0x700, 3),
                Actor("CREA", 0x701, 3), Record("STAT", 0x710), Record("CELL", 0x800, Field("DATA", [1])),
                Group(Ref("ACHR", 0x900, 0x700), Ref("ACHR", 0x901, 0x700), Ref("ACRE", 0x902, 0x701), Ref("REFR", 0x903, 0x710))));
            File.WriteAllBytes(Path.Combine(directory, "Decoy.esm"), Join(Header(), Actor("NPC_", 0x700, 0),
                Record("CELL", 0x800, Field("DATA", [1])), Group(Ref("ACHR", 0x900, 0x700))));
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Decoy.esm", "Actors.esm"), Actor("NPC_", 0x01000700, 3)));
            using var records = FalloutPluginStack.Load(directory, ["Actors.esm", "Decoy.esm", "Patch.esp"]);
            var source = FalloutCombatGroupDeclaration.ForExecutable(Executables[0]);
            var player = records.RuntimeFormKey(0x14);
            using var world = new FalloutReferenceWorld(records);
            world.ConfigureCombatGroups(source, "authored-winning-stack");
            world.BindCombatGroupDecisions(_ => new(2, "authored-detection-entry"),
                (_, _) => new(true, "authored-reaction"), (_, _) => new(false, "authored-live-target"));
            world.SelectSourceCombatTarget(Key(0x900), player);
            world.SelectSourceCombatTarget(Key(0x902), Key(0x901));
            world.SelectSourceCombatTarget(Key(0x901), new("Decoy.esm", 0x900));
            Require(!world.PlayerInCombat(), "A start operation published the cached original combat flag before its player update.");
            world.AdvanceCombatGroups(.125f);
            Require(world.PlayerInCombat(), "The shared player query still used resident targets instead of the actual cached incoming flag.");
            var snapshot = RoundTrip(world.CaptureCombatGroups());
            Require(snapshot.Actors.Any(actor => actor.Reference == Key(0x902) && actor.ReferenceSignature == "ACRE" && actor.BaseSignature == "CREA") &&
                snapshot.Actors.Single(actor => actor.Reference == Key(0x900)).Base == Key(0x700) &&
                snapshot.Actors.Single(actor => actor.Reference == new FalloutFormKey("Decoy.esm", 0x900)).Base == new FalloutFormKey("Decoy.esm", 0x700),
                "Winning master/placed creature identity was replaced by a global object ID.");
            var receiptCount = snapshot.LastSequence;
            world.Get(Key(0x900)).Engagement = world.Get(Key(0x900)).Engagement! with { Position = [1, 2, 3], Rotation = [0, 0, 0, 1] };
            var references = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            Require(references.All(reference => reference.Reference != player), "Combat grouping invented a placed player proxy.");
            Require(world.CaptureCombatGroups().LastSequence == receiptCount, "Pose capture replayed a source target addition.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(references); cold.ConfigureCombatGroups(source, "authored-winning-stack", snapshot);
            Require(cold.CombatGroups.ReadPlayerTargets().CombatGroupTargetCount == 1 && cold.CombatGroups.IncomingMemberCount(player) == 1 && cold.PlayerInCombat(),
                "Cold shared-world publication conflated the personal target and incoming member lists.");
            cold.Get(Key(0x900)).Engagement = null;
            Require(cold.CombatGroups.ReadPlayerTargets().CombatGroupTargetCount is null &&
                world.CombatGroups.ReadPlayerTargets().CombatGroupTargetCount == 1,
                "Cold actor callback retained the temporary validation world or hid an opaque end.");
            Reject(() => world.SelectSourceCombatTarget(Key(0x900), Key(0x903)));
            Require(world.Get(Key(0x900)).Engagement!.Target == player, "Invalid target changed the actual engagement before refusal.");
            world.Get(Key(0x900)).Deleted = true;
            Require(world.ReadPlayerCombatGroupTargets().CombatGroupTargetCount is null && world.CombatGroupSaveBlocker is not null,
                "Actual reference deletion silently completed source combat or remained a settled save owner.");
            File.WriteAllBytes(Path.Combine(directory, "Drift.esp"), Join(Header("Decoy.esm", "Actors.esm", "Patch.esp"), Actor("NPC_", 0x01000700, 2)));
            using var changed = FalloutPluginStack.Load(directory, ["Actors.esm", "Decoy.esm", "Patch.esp", "Drift.esp"]);
            using var drift = new FalloutReferenceWorld(changed);
            drift.Restore(references);
            Reject(() => drift.ConfigureCombatGroups(source, "authored-winning-stack", snapshot));
            Require(drift.CombatGroupState is null, "Changed winning actor data published a partial cold combat owner.");
            var substituted = snapshot with { Actors = snapshot.Actors.Select(actor => actor.Reference == Key(0x900) ?
                actor with { Base = new("Decoy.esm", 0x700) } : actor).ToArray() };
            using var foreign = new FalloutReferenceWorld(records);
            foreign.Restore(references); Reject(() => foreign.ConfigureCombatGroups(source, "authored-winning-stack", substituted));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static FalloutCombatGroupInputs Inputs(FalloutCombatGroupDeclaration source,
        Func<FalloutFormKey, FalloutFormKey, FalloutCombatGroupPredicate>? predicate = null,
        Func<FalloutFormKey, bool>? teammate = null, Func<FalloutFormKey, FalloutCombatGroupDetection>? detection = null,
        Func<ulong, FalloutFormKey, FalloutCombatGroupPredicate>? retirement = null) => new(
            reference => new(reference, Key(reference == Player ? 7u : 0x700u), reference == Player ? "ENGINE_PLAYER" : "ACHR", 0,
                reference == Player ? source.ExecutableSha256 : Digest(reference.ToString()), "NPC_", 0, Digest("authored-base"), reference == Player),
            predicate ?? ((member, target) => new(member != target, "authored-target-predicate")),
            teammate ?? (_ => false), detection ?? (_ => new(2, "authored-detection-entry")),
            retirement ?? ((_, _) => new(false, "authored-retained-target")));
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static FalloutFormKey Key(uint id) => new("Actors.esm", id);
    private static FalloutCombatGroupsSnapshot RoundTrip(FalloutCombatGroupsSnapshot state) =>
        JsonSerializer.Deserialize<FalloutCombatGroupsSnapshot>(JsonSerializer.Serialize(state))!;
    private static byte[] Actor(string signature, uint id, byte aggression)
    { var ai = new byte[20]; ai[0] = aggression; ai[1] = 2; return Record(signature, id, Field("ACBS", new byte[24]), Field("AIDT", ai)); }
    private static byte[] Ref(string signature, uint id, uint basis) => Record(signature, id, Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]));
    private static byte[] Header(params string[] masters) => Record("TES4", 0, Field("HEDR", new byte[12]),
        Join(masters.Select(master => Join(Field("MAST", Encoding.ASCII.GetBytes(master + '\0')), Field("DATA", new byte[8]))).ToArray()));
    private static byte[] Group(params byte[][] values)
    {
        var body = Join(values); var bytes = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0x800);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 6); body.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var body = Join(fields); var bytes = new byte[24 + body.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)body.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        body.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] body)
    {
        var bytes = new byte[6 + body.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)body.Length)); body.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Malformed, foreign or omitted combat ownership was accepted.");
    }
}
