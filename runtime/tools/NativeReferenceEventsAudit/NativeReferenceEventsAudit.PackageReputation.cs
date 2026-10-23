using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private static void ReputationPackageAdapters(string mode = "all")
    {
        var directory = Directory.CreateTempSubdirectory("opennv-package-reputation-");
        // Detached native components exercise their subject-independent query
        // entry points. No actor is published, processed, or presented as gameplay.
        var npc = new RuntimeNativeNpc();
        var creature = new RuntimeNativeCreature();
        try
        {
            byte[] C(ushort function, uint selector, float comparison, byte flags = 0)
            {
                var b = new byte[28]; b[0] = flags; BitConverter.GetBytes(comparison).CopyTo(b, 4);
                BitConverter.GetBytes(function).CopyTo(b, 8); BitConverter.GetBytes(1u).CopyTo(b, 12);
                BitConverter.GetBytes(selector).CopyTo(b, 16); return Field("CTDA", b);
            }
            byte[] Pack(uint id, params byte[][] conditions) => Record("PACK", id,
                new[] { Field("PSDT", [255, 255, 0, 255, 0, 0, 0, 0]) }.Concat(conditions).ToArray());
            byte[] Actor(uint id, params uint[] packages) => Record("NPC_", id,
                new[] { Field("ACBS", new byte[24]) }.Concat(packages.Select(value => Field("PKID", BitConverter.GetBytes(value)))).ToArray());
            File.WriteAllBytes(Path.Combine(directory.FullName, "Contact.esm"),
                Record("TES4", 0, Field("HEDR", new byte[12]))
                    .Concat(Record("REPU", 1, Field("DATA", BitConverter.GetBytes(100f))))
                    .Concat(Record("GMST", 20, Field("EDID", Encoding.ASCII.GetBytes("fReputationThresholdOne\0")), Field("DATA", BitConverter.GetBytes(.25f))))
                    .Concat(Record("GMST", 21, Field("EDID", Encoding.ASCII.GetBytes("fReputationThresholdTwo\0")), Field("DATA", BitConverter.GetBytes(.5f))))
                    .Concat(Record("GMST", 22, Field("EDID", Encoding.ASCII.GetBytes("fReputationThresholdThree\0")), Field("DATA", BitConverter.GetBytes(.75f))))
                    .Concat(Pack(3, C(573, 1, 85), C(574, 1, .85f), C(575, 1, 3)))
                    .Concat(Pack(4, C(573, 1, 86), C(573, 0, 30)))
                    .Concat(Pack(5)).Concat(Pack(6, C(573, 1, 85, 1), C(573, 0, 30)))
                    .Concat(Pack(7, C(573, 1, 85), C(573, 0, 30)))
                    .Concat(Actor(10, 4, 5)).Concat(Actor(11, 6, 5)).Concat(Actor(12, 7, 5)).Concat(Actor(13, 3, 5)).ToArray());
            using var records = FalloutPluginStack.Load(directory.FullName, ["Contact.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var dispatches = 0;
            var unloaded = new FalloutUnloadedActorPackages(records, world, quests, null, null,
                (_, _) => { dispatches++; throw new InvalidOperationException("Query audit cannot dispatch package events."); },
                () => throw new InvalidOperationException("Reputation query cannot request player level."));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(RuntimeNativeNpc).GetField("_aiWorld", flags)!.SetValue(npc, world);
            typeof(RuntimeNativeCreature).GetField("_aiWorld", flags)!.SetValue(creature, world);
            var unloadedQuery = typeof(FalloutUnloadedActorPackages).GetMethod("Evaluate", flags)!
                .CreateDelegate<Func<FalloutFormKey, FalloutCondition, float>>(unloaded);
            var adapters = new[] {
                (Name:"npc", Query:(Func<FalloutCondition,float>)npc.EvaluateAiCondition),
                (Name:"creature", Query:(Func<FalloutCondition,float>)creature.PackageCondition),
                (Name:"unloaded", Query:(Func<FalloutCondition,float>)(condition => unloadedQuery(records.RuntimeFormKey(0x14),condition))) };
            var conditions = FalloutCondition.Read(records.GetEffective(Key(3)));
            foreach (var (name, query) in adapters.Where(value => mode == "all" || value.Name == mode))
            {
                world.RestoreReputations(new(true, []));
                var fresh = query(conditions[0]);
                Require(fresh == 0, $"Actual {name} package adapter did not read fresh reputation.");
                world.SetReputation(Key(1), 1, 85); world.SetReputation(Key(1), 0, 30);
                var before = JsonSerializer.Serialize(world.CaptureReputations());
                var ownerBefore = JsonSerializer.Serialize(unloaded.State);
                foreach (var condition in conditions)
                    Require(query(condition) == condition.Comparison, $"Actual {name} package adapter failed query{condition.Function}.");
                Require(FalloutAiPackages.Select(records, Key(13), query, evaluateRunOn: true)?.FormKey == Key(3),
                    "Existing package selector lost authored order/combined query admission.");
                Require(JsonSerializer.Serialize(world.CaptureReputations()) == before,
                    "Package selection mutated reputation state.");
                world.RestoreReputations(JsonSerializer.Deserialize<FalloutReputationsSnapshot>(before));
                Require(query(conditions[2]) == 3, "Package adapter lost cold-restored threshold inputs.");
                world.RestoreReputations(null); world.SetReputation(Key(1), 1, 85);
                foreach (var condition in conditions)
                    RejectPackageReputation(() => query(condition with { Argument2 = 0 }));
                Require(query(conditions[0]) == 85 && query(conditions[1]) == .85f, "Known legacy package axis was lost.");
                RejectPackageReputation(() => query(conditions[2])); // Threshold always requires both axes.
                Require(FalloutAiPackages.Select(records, Key(10), query, evaluateRunOn: true)?.FormKey == Key(5),
                    "False AND prefix evaluated an unknown axis or changed priority.");
                Require(FalloutAiPackages.Select(records, Key(11), query, evaluateRunOn: true)?.FormKey == Key(6),
                    "True OR prefix evaluated an unknown axis or skipped the first package.");
                RejectPackageReputation(() => FalloutAiPackages.Select(records, Key(12), query, evaluateRunOn: true));
                Require(JsonSerializer.Serialize(unloaded.State) == ownerBefore && dispatches == 0,
                    "Condition queries changed unloaded assignment/lifecycle or dispatched events.");
                GD.Print($"OPENNV_NATIVE_PACKAGE_REPUTATION_PASS kind={name} actualAdapter=true threeQueries=true " +
                    "existingSelector=true falseAnd=true trueOr=true reachedUnknownRefused=true thresholdBothAxes=true cold=true " +
                    "noEventDispatch=true detachedComponent=true actorMaterializationPackageExecutionAndRetail=unverified");
            }
        }
        finally
        {
            npc.Free(); creature.Free(); File.Delete(Path.Combine(directory.FullName, "Contact.esm")); directory.Delete();
        }
    }
    private static void RejectPackageReputation(Action action)
    {
        try { action(); } catch (NotSupportedException) { return; }
        throw new InvalidDataException("Reached unknown package reputation was invented or silently skipped.");
    }
}
