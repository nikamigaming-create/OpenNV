using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ElsePayloadContracts
{
    internal static void Run()
    {
        Check("endif\nset value to 9\nendif\nendif", 9);
        Check("if 0\nif UnknownQuery\nset value to 99\nendif\nendif\nendif\nset value to 10", 10);
        Check("if 1\nset value to 11\nelse\nset value to 99\nendif\nendif", 11);
        Check("if 1\nset value to 1\nelse (UnknownQuery (1 / 0))\nset value to 99\nendif", 1);
        Check("if 0\nset value to 99\nelse (UnknownQuery (1 / 0))\nset value to 2\nendif", 2);
        Check("if 0\nif 0\nset value to 98\nelse (UnknownQuery (1 / 0))\nset value to 99\nendif\nendif\nset value to 3", 3);
        Check("if 0\nset value to 99\nelseif Allowed\nset value to 4\nelse (UnknownQuery (1 / 0))\nset value to 98\nendif", 4, 1);
        Check("if 1\nset value to 5\nelseif UnknownQuery\nset value to 99\nelse (UnknownQuery (1 / 0))\nset value to 98\nendif", 5);
        Check("if 1\nset value to 6\nelse ignored\nif UnknownQuery\nset value to 99\nendif\nelseif UnknownQuery\nset value to 98\nendif", 6);
        Check("if 0\nset value to 99\nelse if UnknownQuery\nif 1\nset value to 7\nendif\nelseif (1 / 0)\nset value to 98\nendif", 7);
        Check("if 0\nif 0\nelse ignored\nelseif UnknownQuery\nset value to 99\nendif\nendif\nset value to 8", 8);
        foreach (var source in new[]
        {
            "else (UnknownQuery)\nendif",
            "if 1\nelse ignored\nelse ignoredAgain\nendif",
            "if 1\nelse ignored\nelseif 0\nelse duplicateAfterTail\nendif",
            "if 1\nelse ignored",
            "endif UnknownQuery",
            "while 0\nendif\nloop",
            "loop"
        })
        {
            try { _ = FalloutGameModeProgram.Read("begin GameMode\n" + source + "\nend"); }
            catch (InvalidDataException) { continue; }
            throw new InvalidDataException("Ignoring Else text admitted malformed surrounding structure.");
        }
        const string newlyAdmitted = "begin GameMode\nif 0\nelse ignored\nelseif UnknownQuery\nendif\nend";
        Require(FalloutGameModeProgram.WasRejectedByParser(newlyAdmitted, 9) &&
            !FalloutGameModeProgram.WasRejectedByParser(newlyAdmitted, FalloutGameModeProgram.ParserVersion) &&
            !FalloutGameModeProgram.WasRejectedByParser("begin GameMode\nif 0\nelse ignored\nNotify \"elseif\" ; elseif 1\nendif\nend", 9),
            "Parser migration admitted an unrelated missing owner or lost the exact old structural refusal.");
        const string baseline = "begin GameMode\nendif\nset value to 1\nend";
        Require(FalloutGameModeProgram.WasRejectedByParser(baseline, 10) &&
            !FalloutGameModeProgram.WasRejectedByParser(baseline, FalloutGameModeProgram.ParserVersion) &&
            !FalloutGameModeProgram.WasRejectedByParser("begin GameMode\nNotify \"endif\" ; endif\nend", 10),
            "Baseline EndIf migration lost the old refusal or matched quoted/comment text.");
        var continued = FalloutGameModeProgram.Read("begin GameMode\nset value to 1\nendif\nFixed\nend")
            .MissingCommandContinuation("Reached object-script command Fixed (0 arguments) has no owner.", _ => true)
            ?? throw new InvalidDataException("Baseline EndIf prevented a unique retained command continuation.");
        var resumed = 0;
        continued.Execute(_ => throw new InvalidDataException("Continuation queried consumed prefix."),
            (_, _) => throw new InvalidDataException("Continuation replayed consumed prefix."),
            (name, arguments) => { Require(name == "Fixed" && arguments.Count == 0, "Wrong continuation command."); resumed++; });
        Require(resumed == 1, "Baseline EndIf continuation lost the stopped command.");
        double menuValue = 0;
        FalloutGameModeProgram.Read("begin MenuMode 1001\nendif\nset value to 1\nendif\nend\n" +
            "begin MenuMode 1002\nendif\nset value to 99\nendif\nend", "MenuMode").Execute(
            _ => throw new InvalidDataException("Unexpected menu variable."), (_, value) => menuValue = value,
            (_, _) => throw new InvalidDataException("Unexpected menu command."), name => name == "MenuMode"
                ? new([FalloutScriptArgumentKind.Number], arguments => arguments[0].Number == 1001 ? 1 : 0) : null);
        Require(menuValue == 1, "Source baseline EndIf escaped a generated menu guard.");
        menuValue = 0;
        FalloutGameModeProgram.Read("begin MenuMode selected\nendif\nset value to 99\nendif\nend", "MenuMode", 1001).Execute(
            name => name == "selected" ? 1002 : throw new InvalidDataException("Unexpected menu filter."),
            (_, value) => menuValue = value, (_, _) => throw new InvalidDataException("Unexpected menu command."));
        Require(menuValue == 0, "Source baseline EndIf escaped a dynamic menu filter guard.");
        ExerciseRetainedActivationPrefix();
        Console.WriteLine("OPENNV_BASELINE_ENDIF_CONTRACT_PASS zeroDepth=true nestedParent=true " +
            "loopMismatchRefused=true legacy10Admission=true continuationPrefixConsumed=true coldFaultNoReplay=true");
        Console.WriteLine("OPENNV_ELSE_PAYLOAD_CONTRACT_PASS taken=true untaken=true inactiveNested=true ignoredTailNeverQueried=true elseIfStillConditional=true elseIfAfterElseDead=true nestedParentSelection=true structuralRefusals=true committedActivationRetained=true oldInvalidElseLatched=true coldNoReplay=true");
    }

    private static void Check(string source, double expected, int expectedQueries = 0)
    {
        double value = 0;
        var queries = 0;
        FalloutScriptFunction? Function(string name)
        {
            queries++;
            return name == "Allowed" ? new([], _ => 1) :
                throw new InvalidDataException("Ignored branch text evaluated an unowned function: " + name);
        }
        FalloutGameModeProgram.Read("begin GameMode\n" + source + "\nend").Execute(
            _ => throw new InvalidDataException("Ignored branch text queried a variable."),
            (name, number) => { Require(name == "value", "Unexpected branch assignment."); value = number; },
            (_, _) => throw new InvalidDataException("Ignored branch text dispatched a command."), Function);
        Require(value == expected && queries == expectedQueries, "Else payload changed branch selection or ElseIf condition evaluation.");
    }

    private static void ExerciseRetainedActivationPrefix()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-else-activation-");
        try
        {
            var header = new byte[20]; U32(1).CopyTo(header, 12);
            var local = new byte[24]; U32(1).CopyTo(local, 0);
            // Original synthetic source: the earlier branch commits before
            // reaching the ignored Else tail, as in the owned gate failure.
            var program = "float counter\nbegin OnActivate\nendif\nif 1\nset counter to counter + 1\nActivate\nelse (UnknownQuery (1 / 0))\nset counter to 99\nelseif UnknownQuery\nset counter to 98\nendif\nendif\nMissingAfterActivation\nend";
            File.WriteAllBytes(Path.Combine(directory.FullName, "Else.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("ACTI", 1, Field("SCRI", U32(0x50))),
                Record("SCPT", 0x50, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("counter")), Field("SCTX", Text(program))),
                Record("CELL", 0x80, Field("DATA", [1])), Group(0x80,
                    Record("REFR", 0x90, Field("NAME", U32(1)), Field("DATA", new byte[24])))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Else.esm"]);
            var key = new FalloutFormKey("Else.esm", 0x90);
            var cell = FalloutCellSceneReader.Read(records, new("Else.esm", 0x80));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var quests = new FalloutQuestState(records);
            var activations = 0;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                Require(effect.Kind == FalloutReferenceEffectKind.DefaultActivate && world.Get(key).Read(1) == 1,
                    "Activation preceded its source prefix or dispatched another effect.");
                activations++;
            }));
            var result = scripts.Activate(key, records.RuntimeFormKey(0x14));
            Require(activations == 1 && result.Error == "OnActivate: Reached object-script command MissingAfterActivation (0 arguments) has no owner." &&
                world.Get(key).Read(1) == 1, "The actual post-activation source failure lost its committed prefix.");
            var retained = world.Capture();
            foreach (var oldElseFault in new[] { false, true })
            {
                // The legacy error case is explicitly an admitted synthetic
                // snapshot. It models the old interpreter's failure after the
                // same prefix; it is never labeled an ordinary campaign save.
                var expected = oldElseFault ? retained.Select(value => value.Reference == key
                    ? value with { ScriptError = "OnActivate: Invalid script else." } : value).ToArray() : retained.ToArray();
                using var cold = new FalloutReferenceWorld(records);
                cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(expected))!);
                cold.LoadCell(cell);
                var before = JsonSerializer.Serialize(cold.Capture());
                var coldEffects = 0;
                var retry = new FalloutReferenceScripts(records, cold, new(records), new((_, _) => false, _ => coldEffects++));
                var retried = retry.Activate(key, records.RuntimeFormKey(0x14));
                Require(retried.Error == expected.Single(value => value.Reference == key).ScriptError && coldEffects == 0 &&
                    cold.Get(key).Read(1) == 1 && before == JsonSerializer.Serialize(cold.Capture()),
                    "Cold activation replayed or auto-cleared an already committed failure prefix.");
            }
        }
        finally { directory.Delete(true); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        U32((uint)data.Length).CopyTo(bytes, 4); U32(id).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Group(uint cell, params byte[][] rows)
    {
        var data = Join(rows); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        U32((uint)bytes.Length).CopyTo(bytes, 4); U32(cell).CopyTo(bytes, 8); U32(6).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
}
