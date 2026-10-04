using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ReferencePackageEventContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-reference-package-events-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string ordered = "short trace\nref caller\nref action\n" +
                "begin OnPackageChange PackageA\nset trace to trace * 10 + 1\nend\n" +
                "begin GameMode\nset trace to trace * 10 + 2\nend\n" +
                "begin OnPackageStart PackageB\nset trace to trace * 10 + 3\nset caller to GetSelf\nset action to GetActionRef\nSetStage EventQuest 16\nend\n" +
                "begin OnPackageEnd PackageA\nset trace to trace * 10 + 4\nend\n" +
                "begin OnPackageDone PackageB\nset trace to trace * 10 + 5\nend\n" +
                "begin OnPackageDone PackageA\nset trace to trace * 10 + 6\nend\n" +
                "begin OnPackageStart PackageA\nset trace to trace * 10 + 7\nend";
            File.WriteAllBytes(Path.Combine(directory, "Events.esm"), Join(Header(),
                ActorBase("CREA", 1, 0x50), ActorBase("CREA", 2, 0x51), ActorBase("CREA", 3, 0x52),
                ActorBase("CREA", 4, 0x53), ActorBase("CREA", 5, 0x54), ActorBase("NPC_", 8, 0x50),
                Record("ACTI", 7, Field("SCRI", BitConverter.GetBytes(0x50u))),
                Script(0x50, ordered, 0x70, 0x60),
                Script(0x51, "short trace\nbegin OnPackageDone PackageA\nset trace to trace + 1\nUnsupportedPackageCommand\nend\n" +
                    "begin GameMode\nset trace to trace + 100\nend", 0x70),
                Script(0x52, "short trace\nbegin OnPackageDone\nset trace to 1\nend", 0x70),
                Script(0x53, "short trace\nbegin OnPackageDone WrongKind\nset trace to 1\nend", 0x71),
                Script(0x54, "short trace\nbegin OnPackageDone UncompiledPackage\nset trace to 1\nend", 0x70),
                Record("QUST", 0x60, Field("EDID", Text("EventQuest"))), Package(0x70, "PackageA"),
                Record("ACTI", 0x71, Field("EDID", Text("WrongKind"))), Package(0x72, "UncompiledPackage"),
                Record("CELL", 0x80, Field("DATA", [1])), Group(0x80,
                    Reference("ACRE", 0x90, 1), Reference("ACRE", 0x91, 1), Reference("ACRE", 0x92, 2),
                    Reference("ACRE", 0x93, 3), Reference("ACRE", 0x94, 4), Reference("ACRE", 0x95, 5),
                    Reference("REFR", 0x96, 7), Reference("ACHR", 0x97, 8))));
            // The winning SCPT binds a master PACK and an ESP-local PACK. An
            // unrelated matching EDID in the corpus grants no compiled access.
            File.WriteAllBytes(Path.Combine(directory, "EventPatch.esp"), Join(Header("Events.esm"),
                Script(0x50, ordered, 0x70, 0x01000101, 0x60), Package(0x01000101, "PackageB")));
            using var records = FalloutPluginStack.Load(directory, ["Events.esm", "EventPatch.esp"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var queue = world.PackageEvents;
            var effects = new List<FalloutReferenceScriptEffect>();
            var requeueOnEffect = false;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                effects.Add(effect);
                if (effect.Kind != FalloutReferenceEffectKind.SetStage) throw new InvalidOperationException("Unexpected reference package effect.");
                quests.EnterStage(effect.Target!.Value, effect.Stage);
                if (requeueOnEffect) queue.Mark(effect.Source, PackageB(), FalloutReferencePackageEventKind.Start);
            }));
            var actor = Key(0x90);
            queue.Mark(actor, Key(0x70), FalloutReferencePackageEventKind.Done);
            queue.Mark(actor, PackageB(), FalloutReferencePackageEventKind.Start);
            queue.Mark(actor, Key(0x70), FalloutReferencePackageEventKind.Change);
            queue.Mark(actor, PackageB(), FalloutReferencePackageEventKind.Done);
            queue.Mark(actor, Key(0x70), FalloutReferencePackageEventKind.Done);
            queue.Mark(Key(0x91), Key(0x70), FalloutReferencePackageEventKind.Done);
            Require(world.InstanceCount == 0 && effects.Count == 0 && queue.PendingCount == 5 && queue.PendingActors.Count == 2 &&
                queue.HasPending(actor) && queue.HasPending(Key(0x91)) && !queue.HasPending(Key(0x97)),
                "Prebinding package marks executed source, failed to coalesce or borrowed another actor's event list.");
            var pendingSave = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            Require(effects.Count == 0 && world.Get(actor).Read(1) == 0 && queue.PendingCount == 5,
                "Capturing prebinding marks executed or consumed their source events.");
            var cell = FalloutCellSceneReader.Read(records, Key(0x80));
            using (var pendingCold = new FalloutReferenceWorld(records))
            {
                pendingCold.Restore(pendingSave);
                Require(pendingCold.PendingPackageEventCount == 5 && pendingCold.Get(actor).Read(1) == 0 &&
                    JsonSerializer.Serialize(pendingCold.Capture()) == JsonSerializer.Serialize(pendingSave),
                    "Cold restoration lost queued marks, source revisions or performed their effects.");
                pendingCold.LoadCell(cell);
                var coldEffects = new List<FalloutReferenceScriptEffect>();
                var restoredScripts = new FalloutReferenceScripts(records, pendingCold, new(records),
                    new((_, _) => false, effect => coldEffects.Add(effect)));
                var restoredBatch = pendingCold.PackageEvents.SnapshotPending(actor);
                Require(restoredScripts.DispatchFrame(actor, [new("GameMode"), .. restoredBatch.Events], .25)
                    .All(result => result.Error is null) && pendingCold.Get(actor).Read(1) == 123456 && coldEffects.Count == 1,
                    "A cold event frame changed source declaration order, caller or effects.");
                pendingCold.PackageEvents.Mark(actor, PackageB(), FalloutReferencePackageEventKind.Start);
                pendingCold.PackageEvents.Consume(restoredBatch);
                Require(pendingCold.PendingPackageEventCount == 2 && pendingCold.PackageEvents.HasPending(actor),
                    "Cold receipt consumption discarded a newer identical transition or another actor's event.");
                Reject(() => queue.Consume(restoredBatch));
                Reject(() => pendingCold.PackageEvents.Consume(restoredBatch));
            }
            var marked = pendingSave.Single(snapshot => snapshot.Reference == actor);
            var savedMarks = marked.PackageEvents!;
            foreach (var invalidMarks in new IReadOnlyList<FalloutReferencePackageEventSnapshot>[]
            {
                [.. savedMarks, savedMarks[0]],
                [savedMarks[0] with { Revision = 0 }],
                [savedMarks[0] with { Kind = (FalloutReferencePackageEventKind)99 }],
                [savedMarks[0] with { Package = Key(0x71) }],
                [savedMarks[0] with { SourceSha256 = new string('0', 64) }],
                [savedMarks[0], savedMarks[1] with { Revision = savedMarks[0].Revision }],
                [null!],
            })
            {
                using var rejectedWorld = new FalloutReferenceWorld(records);
                Reject(() => rejectedWorld.Restore(pendingSave.Select(snapshot => snapshot.Reference == actor
                    ? snapshot with { PackageEvents = invalidMarks } : snapshot).ToArray()));
                Require(rejectedWorld.InstanceCount == 0 && rejectedWorld.PendingPackageEventCount == 0,
                    "Invalid package-event continuation partially restored live state.");
            }
            // Identical PACK bytes in a different winning master context must
            // fail before any actor or event reaches the cold world.
            File.WriteAllBytes(Path.Combine(directory, "EventDrift.esp"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12]), Field("MAST", Text("Events.esm")), Field("DATA", new byte[8]),
                    Field("MAST", Text("EventPatch.esp")), Field("DATA", new byte[8])),
                Package(0x01000101, "PackageB")));
            using (var driftRecords = FalloutPluginStack.Load(directory, ["Events.esm", "EventPatch.esp", "EventDrift.esp"]))
            using (var driftWorld = new FalloutReferenceWorld(driftRecords))
            {
                Require(records.GetEffective(PackageB()).ReadData().SequenceEqual(driftRecords.GetEffective(PackageB()).ReadData()),
                    "The package-context drift fixture unexpectedly changed its source bytes.");
                Reject(() => driftWorld.Restore(pendingSave));
                Require(driftWorld.InstanceCount == 0 && driftWorld.PendingPackageEventCount == 0,
                    "Changed winning package master context partially restored pending events.");
            }
            world.LoadCell(cell);
            var batch = queue.SnapshotPending(actor);
            Require(batch.Actor == actor && batch.Count == 4 && batch.Events.Count == 3 &&
                batch.Events.Single(item => item.Name == "OnPackageDone").Packages!.SetEquals([Key(0x70), PackageB()]),
                "A source event receipt lost the PACK/event-bit identity or failed to coalesce done packages.");
            Require(batch.Events is not FalloutReferenceScriptEvent[] && batch.Events.All(item => item.Packages is not HashSet<FalloutFormKey>),
                "Package receipt exposed mutable admitted collections.");
            var results = scripts.DispatchFrame(actor, [new("GameMode"), .. batch.Events], 0.25);
            Require(results.All(result => result.Error is null) && results.Single(result => result.Event == "OnPackageDone").Blocks == 3 &&
                results.Sum(result => result.Blocks) == 6 && world.Get(actor).Read(1) == 123456 &&
                world.Get(actor).Read(2) == records.RuntimeFormId(actor) && world.Get(actor).Read(3) == 0 &&
                quests.Stage(Key(0x60)) == 16 && effects.Single().Source == actor,
                "Queued package blocks lost source declaration order, End alias, master binding, caller or null action reference.");
            queue.Consume(batch);
            Require(queue.PendingCount == 1 && queue.PendingActors.Single() == Key(0x91) && !queue.HasPending(actor), "Consumption crossed actor event lists.");
            Reject(() => queue.Consume(batch));
            var other = queue.SnapshotPending(Key(0x91));
            Require(scripts.DispatchFrame(other.Actor, other.Events, 0).All(result => result.Error is null) && world.Get(other.Actor).Read(1) == 46,
                "The same PACK mark did not remain independent for a second actor instance.");
            queue.Consume(other);
            var empty = queue.SnapshotPending(actor);
            Require(empty.Count == 0 && empty.Events.Count == 0 && scripts.DispatchFrame(actor, empty.Events, 0).Count == 0 &&
                world.Get(actor).Read(1) == 123456, "Consumed package marks replayed source blocks.");
            queue.Consume(empty);

            var beforeMismatch = world.Get(actor).Read(1);
            var mismatch = scripts.Dispatch(actor, "OnPackageDone", package: Key(0x72));
            Require(mismatch.Blocks == 0 && mismatch.Error is null && world.Get(actor).Read(1) == beforeMismatch,
                "A different PACK identity admitted unrelated source filters.");
            var alias = scripts.Dispatch(actor, "onpackageEND", package: Key(0x70));
            Require(alias.Error is null && alias.Blocks == 2 && world.Get(actor).Read(1) == beforeMismatch * 100 + 46,
                "The End alias failed canonical event admission or bypassed a Done source block.");
            var human = scripts.Dispatch(Key(0x97), "OnPackageStart", package: PackageB());
            Require(human.Error is null && human.Blocks == 1 && world.Get(Key(0x97)).Read(2) == records.RuntimeFormId(Key(0x97)),
                "A placed ACHR lost its actual actor calling context.");

            // All incoming identities validate before even a preceding GameMode
            // block can run. A PACK must never be admitted as an action reference.
            var state = JsonSerializer.Serialize(world.Capture());
            Reject(() => scripts.DispatchFrame(actor, [new("GameMode"), new("OnPackageDone")], 1));
            Reject(() => scripts.Dispatch(actor, "OnPackageDone", actor: Key(0x70), package: Key(0x70)));
            Reject(() => scripts.Dispatch(actor, "OnPackageDone", package: Key(0x71)));
            Reject(() => scripts.DispatchFrame(actor, [new("OnPackageDone", Package: Key(0x70), Packages: new HashSet<FalloutFormKey> { PackageB() })], 0));
            Reject(() => scripts.DispatchFrame(actor, [new("OnPackageDone", Packages: new HashSet<FalloutFormKey>())], 0));
            Reject(() => scripts.DispatchFrame(actor, [new("OnPackageDone", Packages: new HashSet<FalloutFormKey> { Key(0x70), Key(0x71) })], 0));
            Reject(() => scripts.DispatchFrame(actor, [new("OnPackageDone", Topic: Key(0x60), Package: Key(0x70))], 0));
            Reject(() => scripts.Dispatch(actor, "GameMode", package: Key(0x70)));
            Reject(() => scripts.Dispatch(Key(0x96), "OnPackageDone", package: Key(0x70)));
            Reject(() => scripts.DispatchFrame(actor, [new("OnPackageDone", Package: Key(0x70)), new("OnPackageEnd", Package: Key(0x70))], 0));
            Reject(() => queue.Mark(Key(0x96), Key(0x70), FalloutReferencePackageEventKind.Done));
            Reject(() => queue.Mark(actor, Key(0x71), FalloutReferencePackageEventKind.Done));
            Reject(() => queue.Mark(actor, Key(0x70), (FalloutReferencePackageEventKind)99));
            Require(state == JsonSerializer.Serialize(world.Capture()) && queue.PendingCount == 0,
                "Malformed package admission or a nonactor caller partially mutated live script or queue state.");

            world.UnloadCell(cell.Cell.FormKey);
            queue.Mark(actor, PackageB(), FalloutReferencePackageEventKind.Start);
            var unloaded = queue.SnapshotPending(actor);
            Reject(() => scripts.DispatchFrame(actor, unloaded.Events, 0));
            Require(queue.PendingCount == 1, "Unloaded source admission silently erased pending actor package marks.");
            scripts.UnloadCell(cell.Cell.FormKey);
            world.LoadCell(cell);
            requeueOnEffect = true;
            Require(scripts.DispatchFrame(actor, unloaded.Events, 0).Single().Error is null, "Retained package mark failed after residency return.");
            queue.Consume(unloaded);
            Require(queue.PendingCount == 1, "Receipt consumption discarded a newer source-effect transition mark.");
            requeueOnEffect = false;
            var next = queue.SnapshotPending(actor);
            Require(next.Count == 1 && scripts.DispatchFrame(actor, next.Events, 0).Single().Blocks == 1,
                "A newly marked transition did not execute once on the next source frame.");
            var foreign = new FalloutReferencePackageEvents(records);
            Reject(() => foreign.Consume(next));
            Require(queue.PendingCount == 1, "A foreign receipt consumption changed the real owner.");
            queue.Consume(next);
            queue.Mark(actor, Key(0x70), FalloutReferencePackageEventKind.Done);
            var retired = queue.SnapshotPending(actor);
            queue.Clear();
            Reject(() => queue.Consume(retired));
            Require(queue.PendingCount == 0 && queue.PendingActors.Count == 0 && !queue.HasPending(actor), "Queue retirement retained stale event receipts.");

            foreach (var invalidActor in new[] { Key(0x93), Key(0x94), Key(0x95) })
            {
                var rejected = scripts.Dispatch(invalidActor, "OnPackageDone", package: Key(0x70));
                Require(rejected.Error is not null && rejected.Blocks == 0 && world.Get(invalidActor).Read(1) == 0,
                    "An absent, non-PACK or uncompiled source header fabricated a package match.");
            }
            queue.Mark(Key(0x92), Key(0x70), FalloutReferencePackageEventKind.Done);
            var failed = queue.SnapshotPending(Key(0x92));
            var failure = scripts.DispatchFrame(failed.Actor, [.. failed.Events, new("GameMode")], 0);
            Require(failure.All(result => result.Error?.StartsWith("OnPackageDone:", StringComparison.Ordinal) == true) &&
                world.Get(failed.Actor).Read(1) == 1, "Reached source failure discarded its prefix or continued into GameMode.");
            queue.Consume(failed);
            Require(queue.PendingCount == 0 && scripts.Dispatch(failed.Actor, "GameMode").Error == failure[0].Error &&
                world.Get(failed.Actor).Read(1) == 1, "A consumed failed event replayed its prefix on the ordinary script clock.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            var coldScripts = new FalloutReferenceScripts(records, cold, quests, new((_, _) => false,
                _ => throw new InvalidOperationException("A cold retained fault unexpectedly emitted source effects.")));
            Require(cold.Get(failed.Actor).Read(1) == 1 && coldScripts.Dispatch(failed.Actor, "GameMode").Error == failure[0].Error &&
                cold.Get(failed.Actor).Read(1) == 1 && cold.PendingPackageEventCount == 0,
                "Cold reference restoration lost committed locals, source fault or replayed transient event marks.");
            using var invalidCold = new FalloutReferenceWorld(records);
            Reject(() => invalidCold.Restore(saved.Select(snapshot => snapshot.Reference == failed.Actor
                ? snapshot with { ScriptSha256 = new string('0', 64) } : snapshot).ToArray()));
            Require(invalidCold.InstanceCount == 0, "Changed source identity partially restored package-event state.");
            Console.WriteLine("OPENNV_REFERENCE_PACKAGE_EVENTS_PASS typedPack=true actorCaller=true queued=true coalesced=true declarationOrder=true endAlias=true prebinding=true residency=true receipt=true invalidAtomic=true faultPrefix=true coldLocals=true coldPending=true coldRequeue=true sourceContext=true");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static FalloutFormKey Key(uint id) => new("Events.esm", id);
    private static FalloutFormKey PackageB() => new("EventPatch.esp", 0x101);
    private static byte[] ActorBase(string kind, uint id, uint script)
    {
        var acbs = new byte[24]; acbs[8] = 1;
        return Record(kind, id, Field("ACBS", acbs), Field("DATA", new byte[17]), Field("SCRI", BitConverter.GetBytes(script)));
    }
    private static byte[] Script(uint id, string source, params uint[] forms) => Record("SCPT", id,
        Local(1, "trace"), Local(2, "caller", true), Local(3, "action", true), Field("SCTX", Text(source)),
        Join(forms.Select(form => Field("SCRO", BitConverter.GetBytes(form))).ToArray()));
    private static byte[] Package(uint id, string name)
    {
        var data = new byte[12]; data[4] = 6;
        return Record("PACK", id, Field("EDID", Text(name)), Field("PKDT", data));
    }
    private static byte[] Reference(string kind, uint id, uint baseId) => Record(kind, id,
        Field("NAME", BitConverter.GetBytes(baseId)), Field("DATA", new byte[24]));
    private static byte[] Group(uint cell, params byte[][] references)
    {
        var data = Join(references); var group = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, cell); UInt(group, 12, 6); data.CopyTo(group, 24); return group;
    }
    private static byte[] Local(uint id, string name, bool reference = false)
    {
        var data = new byte[24]; UInt(data, 0, id); data[16] = reference ? (byte)1 : (byte)0;
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)data.Length); UInt(bytes, 12, id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void UInt(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidOperationException("Invalid package event or receipt was admitted.");
    }
}
