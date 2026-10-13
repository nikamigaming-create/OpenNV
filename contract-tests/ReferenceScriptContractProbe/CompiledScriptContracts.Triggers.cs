using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunTriggerCallbacks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-triggers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Bytecode.esm");
            File.WriteAllBytes(path, TriggerCallbackFixture());
            var sourceHash = SHA256.HashData(File.ReadAllBytes(path));
            foreach (var overridden in new[] { false, true })
            {
                if (overridden)
                    File.WriteAllBytes(Path.Combine(directory, "Override.esp"), Join(Tes4("Bytecode.esm"),
                        Script(0x60, TriggerCallbackCode(), [Field("SCRO", U32(0xa01)), Field("SCRO", U32(0xa01)), Field("SCRV", U32(3))],
                            source: "begin OnTriggerEnter\nset value to 999\nend")));
                using var records = FalloutPluginStack.Load(directory, overridden ? ["Bytecode.esm", "Override.esp"] : ["Bytecode.esm"]);
                var cell = FalloutCellSceneReader.Read(records, Key(0x800));
                using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
                var quests = new FalloutQuestState(records);
                FalloutReferenceScripts Scripts(FalloutReferenceWorld current) => new(records, current, quests,
                    new((_, _) => false, _ => throw new InvalidDataException("Unexpected trigger effect."),
                        Command: (_, _, _, _) => throw new InvalidDataException("Trigger interpreted contradictory source text.")));
                var scripts = Scripts(world);
                var trigger = world.Get(Key(0x900));
                var first = scripts.DispatchFrame(trigger.Reference, [new("ontriggerenter", Key(0xa00))], 0).Single();
                Require(first is { Error: null } && first.Blocks == (overridden ? 0 : 1) && trigger.Read(1) == (overridden ? 0 : 10),
                    "Compiled trigger ignored its actual winning fixed reference filter.");
                world.WriteVariable(quests, trigger.Reference, 3, records.RuntimeFormId(Key(0xa01)));
                var variable = scripts.DispatchFrame(trigger.Reference, [new("OnTriggerEnter", Key(0xa01))], 0).Single();
                Require(variable is { Error: null } && variable.Blocks == (overridden ? 2 : 1) && trigger.Read(2) == 20,
                    "Compiled trigger did not read current declared reference-variable storage.");

                var missingMember = scripts.DispatchFrame(trigger.Reference,
                    [new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { Key(0xa00) })], 0).Single();
                Require(missingMember is { Error: null, Blocks: 1 } && trigger.Read(2) == 40,
                    "Continuous trigger used an action ref or admitted an absent contact filter.");
                var member = scripts.DispatchFrame(trigger.Reference,
                    [new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { Key(0xa00), Key(0xa01) })], 0).Single();
                Require(member is { Error: null, Blocks: 2 } && trigger.Read(1) == 30,
                    "Continuous trigger lost membership or source block order.");
                var leave = scripts.DispatchFrame(trigger.Reference, [new("OnTriggerLeave", overridden ? Key(0xa01) : Key(0xa00))], 0).Single();
                Require(leave is { Error: null, Blocks: 1 } && trigger.Read(1) == 50, "Compiled leave missed its actual subject.");

                var action = world.Get(Key(0x901));
                var player = records.RuntimeFormKey(FalloutScriptBindings.PlayerReferenceId);
                Require(!records.EffectiveRecords().Any(row => row.FormKey == player) &&
                    scripts.DispatchFrame(Key(0x903), [new("OnTriggerEnter", player)], 0).Single() is { Error: null, Blocks: 1 } &&
                    world.Get(Key(0x903)).Read(1) == 88, "Engine PlayerRef incorrectly required an ESM placement record.");
                Require(scripts.DispatchFrame(action.Reference, [new("OnTriggerEnter", Key(0xa01))], 0).Single() is { Error: null, Blocks: 1 } &&
                    action.Read(3) == records.RuntimeFormId(Key(0xa01)), "Enter body lost its GetActionRef context.");
                Require(scripts.DispatchFrame(action.Reference, [new("OnTriggerLeave", Key(0xa00))], 0).Single() is { Error: null, Blocks: 1 } &&
                    action.Read(3) == records.RuntimeFormId(Key(0xa00)), "Leave body borrowed a prior enter subject.");
                Require(scripts.DispatchFrame(action.Reference,
                    [new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { Key(0xa01) })], 0).Single() is { Error: null, Blocks: 1 } &&
                    action.Read(3) == 0, "Continuous trigger fabricated an action reference.");

                var before = JsonSerializer.Serialize(world.Capture());
                foreach (var invalid in new FalloutReferenceScriptEvent[]
                {
                    new("OnTriggerEnter"), new("OnTriggerLeave", TriggerReferences: new HashSet<FalloutFormKey> { Key(0xa00) }),
                    new("OnTriggerEnter", Key(0x100)), new("OnTrigger", Key(0xa00)),
                    new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey>()),
                    new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { Key(0x100) })
                }) Reject(() => scripts.DispatchFrame(trigger.Reference, [new("GameMode"), invalid], 0));
                Require(before == JsonSerializer.Serialize(world.Capture()), "Malformed trigger admission changed earlier frame state.");

                var malformed = scripts.DispatchFrame(Key(0x902), [new("GameMode"), new("OnTriggerEnter", Key(0xa00))], 0);
                Require(malformed.All(row => row.Error is not null) && world.Get(Key(0x902)).Read(1) == 71 && world.Get(Key(0x902)).Read(2) == 0,
                    "Malformed binary trigger header concealed its committed prefix or ran its body.");
                var saved = Copy(world.Capture().ToArray());
                using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
                var coldScripts = Scripts(cold);
                Require(coldScripts.DispatchFrame(trigger.Reference, [new("OnTriggerEnter", Key(0xa01))], 0).Single() is { Error: null } &&
                    cold.Get(trigger.Reference).Read(2) == 20, "Cold trigger lost its live variable-filter owner.");
                var coldFault = JsonSerializer.Serialize(cold.Capture());
                Require(coldScripts.DispatchFrame(Key(0x902), [new("OnTriggerEnter", Key(0xa00))], 0).Single().Error is not null &&
                    coldFault == JsonSerializer.Serialize(cold.Capture()), "Cold trigger replayed a failed prefix.");
            }
            Require(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Executing callbacks changed source bytes.");
            Console.WriteLine("OPENNV_COMPILED_TRIGGER_CALLBACK_PASS enterLeave=true contactMembership=true fixedAndLiveVariableFilters=true winningOverride=true actionContext=true malformedAtomic=true retainedPrefix=true cold=true sourceUnchanged=true renderer=NONE gameplay=UNEXECUTED");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] TriggerBlock(ushort kind, byte[] parameters, byte[] body) => Join(
        Instruction(0x10, Join(U16(kind), U32((uint)body.Length + 4), parameters)), body, Instruction(0x11));

    private static byte[] TriggerCallbackCode() => Join(Instruction(0x1d),
        TriggerBlock(FalloutCompiledScriptEvents.TriggerEnter, Join(U16(1), Form(1)), Set('f', 1, " 10")),
        TriggerBlock(FalloutCompiledScriptEvents.TriggerEnter, Join(U16(1), Form(3)), Set('f', 2, " 20")),
        TriggerBlock(FalloutCompiledScriptEvents.Trigger, Join(U16(1), Form(2)), Set('f', 1, " 30")),
        TriggerBlock(FalloutCompiledScriptEvents.Trigger, U16(0), Set('f', 2, " 40")),
        TriggerBlock(FalloutCompiledScriptEvents.TriggerLeave, Join(U16(1), Form(1)), Set('f', 1, " 50")));

    private static byte[] TriggerCallbackFixture()
    {
        var action = Join([(byte)'X'], U16(0x10cd), U16(0));
        var storeAction = Instruction(0x15, Join([(byte)'f'], U16(3), U16((ushort)action.Length), action));
        var actionCode = Join(Instruction(0x1d), Block(FalloutCompiledScriptEvents.TriggerEnter, storeAction),
            Block(FalloutCompiledScriptEvents.TriggerLeave, storeAction), Block(FalloutCompiledScriptEvents.Trigger, storeAction));
        var references = Enumerable.Range(0, 4).Select(index => Record("REFR", (uint)(0x900 + index),
            Field("NAME", U32((uint)(0x100 + index))), Field("DATA", new byte[24]))).SelectMany(value => value).ToArray();
        var contacts = Join(Record("REFR", 0xa00, Field("NAME", U32(0x110)), Field("DATA", new byte[24])),
            Record("REFR", 0xa01, Field("NAME", U32(0x110)), Field("DATA", new byte[24])));
        var contents = Join(references, contacts);
        var group = new byte[24 + contents.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); contents.CopyTo(group, 24);
        return Join(Tes4(), Enumerable.Range(0, 4).Select(index => Record("STAT", (uint)(0x100 + index),
                Field("SCRI", U32((uint)(0x60 + index))))).SelectMany(value => value).ToArray(), Record("STAT", 0x110),
            Script(0x60, TriggerCallbackCode(), [Field("SCRO", U32(0xa00)), Field("SCRO", U32(0xa01)), Field("SCRV", U32(3))],
                source: "begin OnTriggerEnter\nset value to 999\nend"),
            Script(0x61, actionCode, [Field("SCRV", U32(3))]),
            Script(0x62, Join(Instruction(0x1d), Block(0, Set('f', 1, " 71")),
                TriggerBlock(FalloutCompiledScriptEvents.TriggerEnter, [1], Set('f', 2, " 999"))), []),
            Script(0x63, Join(Instruction(0x1d), TriggerBlock(FalloutCompiledScriptEvents.TriggerEnter,
                Join(U16(1), Form(1)), Set('f', 1, " 88"))), [Field("SCRO", U32(FalloutScriptBindings.PlayerReferenceId))]),
            Record("CELL", 0x800, Field("DATA", [1])), group);
    }
}
