using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunCompanionCallbacks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-actor-callbacks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Bytecode.esm");
            File.WriteAllBytes(path, CompanionCallbackFixture());
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var quests = new FalloutQuestState(records);
            FalloutReferenceScripts Scripts(FalloutReferenceWorld state) => new(records, state, quests,
                new((_, _) => false, _ => throw new InvalidDataException("Unexpected authored callback effect."),
                    Command: (_, _, _, _) => throw new InvalidDataException("Binary callback evaluated SCTX.")));
            var scripts = Scripts(world);
            var result = scripts.DispatchFrame(Key(0xa00), [new("OnDeath"), new("OnCombatEnd"),
                new("SayToDone", Topics: new HashSet<FalloutFormKey> { Key(0x70) })], .125);
            Require(result.All(item => item is { Error: null, Blocks: 1 }) && world.Get(Key(0xa00)).Read(1) == 73 &&
                world.Get(Key(0xa00)).Read(2) == 0, "Compiled original callback order/topic filter differs from admitted source.");
            var mismatch = scripts.DispatchFrame(Key(0xa00), [new("SayToDone", Topic: Key(0x72))], 0);
            Require(mismatch.Single() is { Error: null, Blocks: 0 } && world.Get(Key(0xa00)).Read(1) == 73,
                "Unmatched original speech topic executed or manufactured a callback block.");
            var multi = scripts.DispatchFrame(Key(0xa00), [new("SayToDone", Topics: new HashSet<FalloutFormKey> { Key(0x70), Key(0x71) })], 0);
            Require(multi.Single() is { Error: null, Blocks: 2 } && world.Get(Key(0xa00)).Read(2) == 59,
                "Actual multiple completed topics lost original declaration order.");
            var unfiltered = scripts.DispatchFrame(Key(0xa01), [new("OnDeath")], 0);
            Require(unfiltered.Single() is { Error: null, Blocks: 1 } && world.Get(Key(0xa01)).Read(1) == 61,
                "Original explicit zero-count death event was refused.");
            var activation = scripts.DispatchFrame(Key(0xa02), [new("OnActivate", ActionReference: records.RuntimeFormKey(0x14))], 0);
            Require(activation.Single() is { Error: null, Blocks: 1 } && world.Get(Key(0xa02)).Read(1) == 67,
                "Original OnActivate header was incorrectly converted to a subject filter.");
            foreach (var id in new uint[] { 0xa03, 0xa04, 0xa05 })
            {
                var fault = scripts.DispatchFrame(Key(id), [new("OnCombatEnd"), new("SayToDone", Topic: Key(0x70))], 0);
                Require(fault.All(item => item.Error is not null) && world.Get(Key(id)).Read(1) == 71 &&
                    world.Get(Key(id)).Read(2) == 0, "Malformed/variable/non-topic binary filter lost its committed prefix or ran its suffix.");
            }
            var saved = Copy(world.Capture().ToArray());
            using var cold = new FalloutReferenceWorld(records); cold.LoadCell(cell); cold.Restore(saved);
            var coldScripts = Scripts(cold);
            var before = JsonSerializer.Serialize(cold.Capture());
            foreach (var id in new uint[] { 0xa03, 0xa04, 0xa05 })
                Require(coldScripts.Dispatch(Key(id), "OnCombatEnd").Error is not null, "Cold callback fault fabricated a new invocation.");
            Require(before == JsonSerializer.Serialize(cold.Capture()), "Cold failed callback replay changed its committed locals or history.");
            FollowContinuationFixture(records);
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Authored source changed while inspecting/executing callbacks.");
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("OPENNV_COMPANION_CALLBACK_CONTRACT_PASS compiledOnly=true actualActorRecords=true " +
            "sourceOrder=true exactTopicFilter=true unfilteredDeathCombatEnd=true activationHeaderIgnored=true " +
            "malformedPrefixCold=true typedFollowCold=true nativeMovementUnexecuted=true");
    }

    private static void FollowContinuationFixture(FalloutPluginStack records)
    {
        var follow = FalloutFollowPackage.Read(records.GetEffective(Key(0x80)));
        var activity = new FalloutActorActivitySnapshot(false, true, false, true, false, false, 4);
        var election = new FalloutFollowElection(3.875, new(2, 4, 3, 9), 7, 3, false, activity, 2, "POBA", follow.Form);
        var progress = new FalloutFollowProgress(records.RuntimeFormKey(0x14), new string('a', 64),
            [5, 0, 9], [[2, 0, 3], [5, 0, 9]], 1, -.03125, .0625, .75f, null, 0, election);
        follow.ValidateContinuation(records, Key(0xa00), progress);
        var copied = Copy(progress);
        Require(JsonSerializer.Serialize(copied) == JsonSerializer.Serialize(progress), "Cold Follow rounded a fractional route/election clock.");
        var isolated = progress.Copy(); isolated.RouteWaypoints[0][0] = 999;
        Require(progress.RouteWaypoints[0][0] == 2, "Follow continuation copied a mutable native waypoint by alias.");
        var events = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Cold Follow replayed a package result."));
        events.Restore(FalloutScriptPackage.Read(records.GetEffective(Key(0x80))), false);
        election.RestoreHistory(events);
        Require(events.Revision == 2 && events.Active?.Form == follow.Form && !events.Done, "Cold Follow lost its original active/history owner.");
        Reject(() => follow.ValidateContinuation(records, Key(0xa00), progress with { Target = Key(0xa01) }));
        Reject(() => follow.ValidateContinuation(records, Key(0xa00), progress with { Election = null }));
        Reject(() => (progress with { RouteCursor = 3 }).Validate());
        Reject(() => (progress with { RetrySeconds = double.NaN }).Validate());
        Reject(() => (progress with { RouteFailures = 1 }).Validate());
        Reject(() => new FalloutActorPackageMotion(follow.Form, new string('b', 64), "source.kf", new string('c', 64),
            1.125, false, [0, 0, 0], [0, 0, 0, 1], Guard: new(Key(0x800), [0, 0, 0], false), Follow: progress).Validate());
        Reject(() => FalloutFollowPackage.Read(records.GetEffective(Key(0x81))).RequireActorTarget(records, Key(0xa00)));
    }

    private static byte[] CompanionCallbackFixture()
    {
        byte[] Filtered(ushort kind, byte[] parameters, byte[] body) => Join(
            Instruction(0x10, Join(U16(kind), U32((uint)body.Length + 4), parameters)), body, Instruction(0x11));
        var code = Join(Instruction(0x1d), Block(12, Set('f', 1, " 47")),
            Filtered(7, Join(U16(1), Form(1)), Set('f', 1, " 53")),
            Filtered(7, Join(U16(1), Form(2)), Set('f', 2, " 59")), Block(10, Set('f', 1, " 73")));
        var bodies = Enumerable.Range(0, 6).Select(index => Record("ACRE", (uint)(0xa00 + index),
            Field("NAME", U32((uint)(0x100 + index))), Field("DATA", new byte[24]))).SelectMany(value => value).ToArray();
        var group = new byte[24 + bodies.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); bodies.CopyTo(group, 24);
        var followData = new byte[12]; followData[4] = 1;
        byte[] Target(uint form) { var data = new byte[16]; UInt(data, 4, form); UInt(data, 8, 250); return data; }
        return Join(Tes4(), Enumerable.Range(0, 6).Select(index => Record("CREA", (uint)(0x100 + index),
                Field("SCRI", U32((uint)(0x60 + index))))).SelectMany(value => value).ToArray(),
            Script(0x60, code, [Field("SCRO", U32(0x70)), Field("SCRO", U32(0x71))]),
            Script(0x61, Join(Instruction(0x1d), Filtered(10, U16(0), Set('f', 1, " 61"))), []),
            Script(0x62, Join(Instruction(0x1d), Filtered(2, Join(U16(1), Form(1)), Set('f', 1, " 67"))), [Field("SCRO", U32(0x71))]),
            Script(0x63, Join(Instruction(0x1d), Block(12, Set('f', 1, " 71")), Filtered(7, [1], Set('f', 2, " 999"))), []),
            Script(0x64, Join(Instruction(0x1d), Block(12, Set('f', 1, " 71")), Filtered(7, Join(U16(1), Form(1)), Set('f', 2, " 999"))), [Field("SCRV", U32(3))]),
            Script(0x65, Join(Instruction(0x1d), Block(12, Set('f', 1, " 71")), Filtered(7, Join(U16(1), Form(1)), Set('f', 2, " 999"))), [Field("SCRO", U32(0x100))]),
            Record("DIAL", 0x70), Record("DIAL", 0x71), Record("DIAL", 0x72),
            Record("PACK", 0x80, Field("PKDT", followData), Field("PTDT", Target(0x14))),
            Record("PACK", 0x81, Field("PKDT", followData), Field("PTDT", Target(0x100))),
            Record("CELL", 0x800, Field("DATA", [1])), group);
    }
}
