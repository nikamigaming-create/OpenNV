using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunPackageCallbacks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-packages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Bytecode.esm");
            File.WriteAllBytes(path, PackageCallbackFixture());
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var quests = new FalloutQuestState(records);
            var effects = 0;
            var requeue = false;
            FalloutReferenceScripts Scripts(FalloutReferenceWorld current) => new(records, current, quests,
                new((_, _) => false, _ =>
                {
                    ++effects;
                    if (requeue) current.PackageEvents.Mark(Key(0x900), Key(0x71), FalloutReferencePackageEventKind.Start);
                }, Command: (_, _, _, _) => throw new InvalidDataException("Package callback interpreted contradictory SCTX.")));
            var scripts = Scripts(world);
            var queue = world.PackageEvents;
            void Mark(FalloutFormKey actor)
            {
                queue.Mark(actor, Key(0x70), FalloutReferencePackageEventKind.Done);
                queue.Mark(actor, Key(0x71), FalloutReferencePackageEventKind.Start);
                queue.Mark(actor, Key(0x70), FalloutReferencePackageEventKind.Change);
                queue.Mark(actor, Key(0x71), FalloutReferencePackageEventKind.Done);
                queue.Mark(actor, Key(0x70), FalloutReferencePackageEventKind.Done);
            }
            Mark(Key(0x900));
            queue.Mark(Key(0x901), Key(0x70), FalloutReferencePackageEventKind.Done);
            var pending = Copy(world.Capture().ToArray());
            using (var coldPending = new FalloutReferenceWorld(records))
            {
                coldPending.Restore(pending); coldPending.LoadCell(cell);
                Require(coldPending.Get(Key(0x900)).Read(1) == 0 && effects == 0 && coldPending.PendingPackageEventCount == 5,
                    "Cold package restoration executed callbacks or discarded source event bits.");
                var receipt = coldPending.PackageEvents.SnapshotPending(Key(0x900));
                var result = Scripts(coldPending).DispatchFrame(receipt.Actor, [new("GameMode"), .. receipt.Events], .25);
                Require(result.All(row => row.Error is null) && coldPending.Get(receipt.Actor).Read(1) == 123456 &&
                    coldPending.Get(receipt.Actor).Read(2) == records.RuntimeFormId(receipt.Actor) &&
                    coldPending.Get(receipt.Actor).Read(3) == 0, "Cold package callbacks lost binary filters, source order or actor/null-action context.");
                coldPending.PackageEvents.Consume(receipt);
                using var noReplay = new FalloutReferenceWorld(records);
                noReplay.Restore(Copy(coldPending.Capture().ToArray())); noReplay.LoadCell(cell);
                var consumed = noReplay.PackageEvents.SnapshotPending(receipt.Actor);
                Require(consumed.Count == 0 && Scripts(noReplay).DispatchFrame(receipt.Actor, consumed.Events, 0).Count == 0 &&
                    noReplay.Get(receipt.Actor).Read(1) == 123456, "Consumed compiled package callbacks replayed after cold reconstruction.");
            }
            foreach (var corrupt in new[]
            {
                pending.Select(row => row.Reference == Key(0x900) ? row with { ScriptSha256 = new string('0', 64) } : row).ToArray(),
                pending.Select(row => row.Reference == Key(0x900) ? row with
                { PackageEvents = row.PackageEvents!.Select(mark => mark with { SourceSha256 = new string('0', 64) }).ToArray() } : row).ToArray()
            })
            {
                using var rejected = new FalloutReferenceWorld(records);
                Reject(() => rejected.Restore(corrupt));
                Require(rejected.InstanceCount == 0 && rejected.PendingPackageEventCount == 0,
                    "Changed compiled script/package identity partially restored callbacks.");
            }
            var batch = queue.SnapshotPending(Key(0x900));
            Require(batch.Count == 4 && batch.Events.Count == 3, "Package marks lost their typed event-bit coalescing.");
            requeue = true;
            var results = scripts.DispatchFrame(batch.Actor, [new("GameMode"), .. batch.Events], .25);
            requeue = false;
            Require(results.All(row => row.Error is null) && results.Sum(row => row.Blocks) == 6 &&
                results.Single(row => row.Event == "OnPackageDone").Blocks == 3 && world.Get(batch.Actor).Read(1) == 123456 &&
                world.Get(batch.Actor).Read(2) == records.RuntimeFormId(batch.Actor) && world.Get(batch.Actor).Read(3) == 0,
                "Compiled Start/Done/Change used delivery order, the wrong package or an action reference.");
            queue.Consume(batch);
            Require(queue.PendingCount == 2, "Receipt consumption erased a newer identical callback or a second actor's mark.");
            Reject(() => queue.Consume(batch));
            var next = queue.SnapshotPending(batch.Actor);
            Reject(() => new FalloutReferencePackageEvents(records).Consume(next));
            Require(scripts.DispatchFrame(next.Actor, next.Events, 0).Single() is { Error: null, Blocks: 1 },
                "The newer source transition did not execute on its next frame.");
            queue.Consume(next);
            var creature = queue.SnapshotPending(Key(0x901));
            Require(scripts.DispatchFrame(creature.Actor, creature.Events, 0).Single() is { Error: null, Blocks: 2 } &&
                world.Get(creature.Actor).Read(1) == 46, "Compiled package callbacks lost their independent ACRE owner.");
            queue.Consume(creature);
            var beforeMismatch = world.Get(batch.Actor).Read(1);
            Require(scripts.Dispatch(batch.Actor, "OnPackageDone", package: Key(0x72)) is { Error: null, Blocks: 0 } &&
                world.Get(batch.Actor).Read(1) == beforeMismatch, "A different winning PACK bypassed a compiled filter.");
            world.WriteVariable(quests, batch.Actor, 1, 47);
            Require(scripts.Dispatch(batch.Actor, "onpackageEND", package: Key(0x70)) is { Error: null, Blocks: 2 } &&
                world.Get(batch.Actor).Read(1) == 4746, "The original End alias lost Done identity.");

            var stable = JsonSerializer.Serialize(world.Capture());
            foreach (var invalid in new FalloutReferenceScriptEvent[]
            {
                new("OnPackageDone"), new("OnPackageDone", Key(0x901), Package: Key(0x70)),
                new("OnPackageDone", Package: Key(0x100)), new("OnPackageDone", Package: Key(0x70), Topic: Key(0x70)),
                new("OnPackageDone", Package: Key(0x70), Packages: new HashSet<FalloutFormKey> { Key(0x71) }),
                new("OnPackageDone", Packages: new HashSet<FalloutFormKey>()),
                new("OnPackageDone", Packages: new HashSet<FalloutFormKey> { Key(0x70), Key(0x100) })
            }) Reject(() => scripts.DispatchFrame(batch.Actor, [new("GameMode"), invalid], 0));
            Reject(() => scripts.Dispatch(Key(0x909), "OnPackageDone", package: Key(0x70)));
            Reject(() => scripts.DispatchFrame(batch.Actor, [new("OnPackageDone", Package: Key(0x70)), new("OnPackageEnd", Package: Key(0x70))], 0));
            Require(stable == JsonSerializer.Serialize(world.Capture()), "Invalid package delivery changed an earlier compiled frame prefix.");

            var source = records.GetEffective(Key(0x60));
            var program = FalloutCompiledScriptProgram.Read(source, FalloutScriptScope.Standalone(source), standalone: true);
            var owned = program.Events.First(row => FalloutCompiledScriptEvents.IsPackage(row.Event));
            Reject(() => FalloutCompiledScriptEvents.ActorCallbackFilter(program, owned with { }));
            var failed = scripts.DispatchFrame(Key(0x902), [new("OnPackageDone", Package: Key(0x70)), new("GameMode")], 0);
            Require(failed.All(row => row.Error?.Contains("SCDA offset", StringComparison.Ordinal) == true) &&
                world.Get(Key(0x902)).Read(1) == 1, "Reached package failure discarded its original instruction prefix or continued source blocks.");
            for (uint index = 0; index < PackageMalformedHeaders().Length + 2; ++index)
            {
                var actor = Key(0x920 + index);
                var result = scripts.DispatchFrame(actor, [new("GameMode"), new("OnPackageDone", Package: Key(0x70))], 0);
                Require(result.All(row => row.Error is not null) && world.Get(actor).Read(1) == 71 && world.Get(actor).Read(2) == 0,
                    "Missing, malformed, non-PACK or unowned SCRV package header admitted its body or lost the earlier prefix.");
            }
            var saved = Copy(world.Capture().ToArray());
            using var coldFault = new FalloutReferenceWorld(records); coldFault.Restore(saved); coldFault.LoadCell(cell);
            var frozen = JsonSerializer.Serialize(coldFault.Capture());
            Require(Scripts(coldFault).Dispatch(Key(0x902), "OnPackageDone", package: Key(0x70)).Error == failed[0].Error &&
                frozen == JsonSerializer.Serialize(coldFault.Capture()), "Cold package failure replayed its prefix or lost its source identity.");
            File.WriteAllBytes(Path.Combine(directory, "Override.esp"), Join(Tes4("Bytecode.esm"),
                Script(0x60, source.ReadSubrecords().Single(field => field.Signature == "SCDA").Data.ToArray(),
                    [Field("SCRO", U32(0x71)), Field("SCRO", U32(0x71)), Field("SCRV", U32(2)), Field("SCRV", U32(3)), Field("SCRO", U32(0x20))])));
            using var winning = FalloutPluginStack.Load(directory, ["Bytecode.esm", "Override.esp"]);
            using var winningWorld = new FalloutReferenceWorld(winning);
            using var changedCold = new FalloutReferenceWorld(winning);
            Reject(() => changedCold.Restore(pending));
            Require(changedCold.InstanceCount == 0 && changedCold.PendingPackageEventCount == 0,
                "A changed winning package filter admitted a historical script identity.");
            winningWorld.LoadCell(FalloutCellSceneReader.Read(winning, Key(0x800)));
            var winningQuests = new FalloutQuestState(winning);
            var winningScripts = new FalloutReferenceScripts(winning, winningWorld, winningQuests,
                new((_, _) => false, _ => { }));
            var overrideEvents = new FalloutReferenceScriptEvent[] { new("GameMode"), new("OnPackageStart", Package: Key(0x71)),
                new("OnPackageDone", Packages: new HashSet<FalloutFormKey> { Key(0x70), Key(0x71) }), new("OnPackageChange", Package: Key(0x70)) };
            Require(winningScripts.DispatchFrame(Key(0x900), overrideEvents, 0).All(row => row.Error is null) &&
                winningWorld.Get(Key(0x900)).Read(1) == 234567,
                "Compiled package filters lost the winning override's ordered master-bound SCRO identity.");
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Package callbacks changed original source bytes.");
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("OPENNV_COMPILED_PACKAGE_CALLBACK_PASS startDoneChange=true orderedScroPack=true winningOverride=true actorCaller=true nullAction=true eventMembership=true sourceOrder=true coalesced=true pendingCold=true noReplay=true malformedAtomic=true faultPrefix=true sourceUnchanged=true variableFilter=UNOWNED renderer=NONE gameplay=UNEXECUTED");
    }

    private static byte[] PackageTrace(int digit)
    {
        var expression = Join([32, (byte)'f'], U16(1), Encoding.ASCII.GetBytes(" 10 * " + digit + " +"));
        return Instruction(0x15, Join([(byte)'f'], U16(1), U16((ushort)expression.Length), expression));
    }
    private static byte[] PackageQueryStore(ushort slot, ushort command)
    {
        var expression = Join([(byte)'X'], U16(command), U16(0));
        return Instruction(0x15, Join([(byte)'f'], U16(slot), U16((ushort)expression.Length), expression));
    }
    private static byte[] PackageBlock(ushort kind, ushort reference, byte[] body) =>
        TriggerBlock(kind, Join(U16(1), Form(reference)), body);
    private static byte[][] PackageMalformedHeaders() =>
        [[], U16(0), [1], Join(U16(2), Form(1), Form(1)), Join(U16(1), [(byte)'f'], U16(1)),
            Join(U16(1), Form(0)), Join(U16(1), Form(9))];

    private static byte[] PackageCallbackFixture()
    {
        var links = new[] { Field("SCRO", U32(0x70)), Field("SCRO", U32(0x71)), Field("SCRV", U32(2)),
            Field("SCRV", U32(3)), Field("SCRO", U32(0x20)) };
        var main = Join(Instruction(0x1d), PackageBlock(FalloutCompiledScriptEvents.PackageChange, 1, PackageTrace(1)),
            Block(0, PackageTrace(2)), PackageBlock(FalloutCompiledScriptEvents.PackageStart, 2,
                Join(PackageTrace(3), PackageQueryStore(2, 0x10ce), PackageQueryStore(3, 0x10cd),
                    Call(0x1039, Join(U16(2), Form(5), Integer(10))))),
            PackageBlock(FalloutCompiledScriptEvents.PackageDone, 1, PackageTrace(4)),
            PackageBlock(FalloutCompiledScriptEvents.PackageDone, 2, PackageTrace(5)),
            PackageBlock(FalloutCompiledScriptEvents.PackageDone, 1, PackageTrace(6)),
            PackageBlock(FalloutCompiledScriptEvents.PackageStart, 1, PackageTrace(7)));
        var fault = Join(Instruction(0x1d), PackageBlock(FalloutCompiledScriptEvents.PackageDone, 1,
            Join(PackageTrace(1), Instruction(0x2f03), PackageTrace(9))), Block(0, PackageTrace(2)));
        var bad = PackageMalformedHeaders().Append(Join(U16(1), Form(2))).Append(Join(U16(1), Form(3))).ToArray();
        var scripts = bad.Select((header, index) => Script((uint)(0x80 + index), Join(Instruction(0x1d),
            Block(0, Set('f', 1, " 71")), TriggerBlock(FalloutCompiledScriptEvents.PackageDone, header, Set('f', 2, " 999"))),
            [Field("SCRO", U32(0x70)), Field("SCRO", U32(0x100)), Field("SCRV", U32(3))])).ToArray();
        var acbs = new byte[24]; acbs[8] = 1;
        byte[] Actor(string kind, uint id, uint script) => Record(kind, id, Field("ACBS", acbs),
            Field("DATA", new byte[17]), Field("SCRI", U32(script)));
        byte[] Placed(string kind, uint id, uint baseId) => Record(kind, id, Field("NAME", U32(baseId)), Field("DATA", new byte[24]));
        var bases = Join(Actor("NPC_", 0x100, 0x60), Actor("CREA", 0x101, 0x60), Actor("NPC_", 0x102, 0x61),
            Record("STAT", 0x109, Field("SCRI", U32(0x60))),
            Join(bad.Select((_, index) => Actor("NPC_", (uint)(0x120 + index), (uint)(0x80 + index))).ToArray()));
        var placed = Join(Placed("ACHR", 0x900, 0x100), Placed("ACRE", 0x901, 0x101), Placed("ACHR", 0x902, 0x102),
            Placed("REFR", 0x909, 0x109), Join(bad.Select((_, index) => Placed("ACHR", (uint)(0x920 + index), (uint)(0x120 + index))).ToArray()));
        var group = new byte[24 + placed.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); placed.CopyTo(group, 24);
        byte[] Package(uint id) { var data = new byte[12]; data[4] = 6; return Record("PACK", id, Field("PKDT", data)); }
        return Join(Tes4(), bases, Script(0x60, main, links, "begin OnPackageDone\nset value to 999\nend"),
            Script(0x61, fault, [Field("SCRO", U32(0x70))]), Join(scripts), Package(0x70), Package(0x71), Package(0x72),
            Script(0x75, Instruction(0x1d), [], quest: true), Record("QUST", 0x20, Field("DATA", new byte[8]), Field("SCRI", U32(0x75))),
            Record("CELL", 0x800, Field("DATA", [1])), group);
    }
}
