using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class PlayerAbilityScriptContracts
{
    private static byte[] CompiledAbilityBody(bool failure, bool update)
    {
        var predicate = Join([(byte)' ', (byte)'s'], AbilityU16(1), Encoding.ASCII.GetBytes(" 0 =="));
        var body = Join(AbilityInstruction(0x16, Join(AbilityU16(3), AbilityU16((ushort)predicate.Length), predicate)),
            AbilitySet('s', 1, Encoding.ASCII.GetBytes(" 1")), AbilityActorValue(32, 3, 1),
            failure ? AbilityInstruction(0x2f03) : AbilityActorValue(40, 2, 1), AbilityInstruction(0x19));
        return Join(AbilityInstruction(0x1d), AbilityBlock(17, body),
            update ? AbilityBlock(19, AbilityActorValue(32, 9, 1)) : []);
    }

    private static void CheckCompiledAbilityAuthority(string directory)
    {
        foreach (var diagnostic in new[] { "absent", "malformed", "contradictory", "duplicate" })
        {
            var path = Path.Combine(directory, Plugin);
            var original = RewriteAbilityScript(Fixture(false), fields =>
            {
                fields.RemoveAll(field => field.Signature == "SCTX");
                if (diagnostic != "absent") fields.Add(("SCTX", Text(diagnostic == "contradictory"
                    ? "short count\nbegin ScriptEffectStart\nplayer.ModAV Barter 999\nend"
                    : "unmatched Endif is deliberately not executable source")));
                if (diagnostic == "duplicate") fields.Add(("SCTX", Text("a second invalid source body")));
            });
            File.WriteAllBytes(path, original); var inputHash = SHA256.HashData(original);
            using var records = FalloutPluginStack.Load(directory, [Plugin]);
            using var world = new FalloutReferenceWorld(records);
            var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
            var effects = Bind(records, world, actor, skills); effects.Synchronize();
            var saved = effects.Capture(); var effect = saved.Effects.Single(); var compiled = effect.Compiled!;
            Require(effect.Started && effect.Error is null && skills.CaptureValues().Pools[32].Permanent == 3 &&
                world.InstanceCount == 0 && compiled.Events is [{ Attempted: true, Cursor.Completed: true,
                    Receipt: { Disposition: "completed", Invocation: > 0 } }] &&
                compiled.Events[0].Receipt!.Session != Guid.Empty && compiled.Events[0].Cursor.CommittedInstructions == 5,
                "Absent/contradictory/malformed SCTX changed actual SCDA execution or fabricated a shared retirement.");
            Reject(() => FalloutPlayerAbilityScripts.Validate(saved with { Schema = "opennv-player-ability-scripts/v1" }));
            Reject(() => FalloutPlayerAbilityScripts.Validate(saved with
                { Effects = [effect with { Compiled = null }] }));
            Reject(() => FalloutPlayerAbilityScripts.ValidateSource(records, saved with
            {
                Effects = [effect with { Compiled = compiled with { ProgramSha256 = new('0', 64) } }]
            }));
            Reject(() => FalloutPlayerAbilityScripts.ValidateSource(records, saved with
            {
                Effects = [effect with { Compiled = compiled with
                {
                    Events = [compiled.Events[0] with { Cursor = compiled.Events[0].Cursor with { NextOffset = 1 } }]
                } }]
            }));
            Require(inputHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Compiled ability changed authored source bytes.");
        }
        CheckCompiledAbilityRawViews(directory);
        CheckCompiledAbilityExternalOwner(directory);
        CheckCompiledAbilityAdmission(directory);
        CheckCompiledAbilityOverride(directory);
        Console.WriteLine("OPENNV_COMPILED_ACTIVE_EFFECT_CONTRACT_PASS sourceKind=0100 actualSharedLeases=true implicitEffectCells=true orderedRaw=true sourceTextUnused=true currentCold=true originalEffectNative=UNOWNED parity=UNVERIFIED");
    }

    private static void CheckCompiledAbilityRawViews(string directory)
    {
        const ulong tail = 0x7ff8000000000042;
        var code = Join(AbilityInstruction(0x1d), AbilityBlock(17, Join(
            AbilitySet('s', 1, Encoding.ASCII.GetBytes(" 1")),
            AbilitySet('f', 2, Join([(byte)'s'], AbilityU16(1))),
            AbilitySet('f', 1, Join([(byte)'Z'], AbilityU16(1))), AbilityActorValue(32, 3, 3))));
        var authored = RewriteAbilityScript(Fixture(false), fields =>
        {
            ReplaceAbilityCode(fields, code, 3, 3);
            fields.RemoveAll(field => field.Signature is "SLSD" or "SCVR" or "SCTX");
            AddAbilityLocal(fields, 1, 1, "integerAlias", 0);
            AddAbilityLocal(fields, 1, 0, "referenceAlias", tail);
            AddAbilityLocal(fields, 2, 0, "numericCopy", BitConverter.DoubleToUInt64Bits(27));
            fields.Add(("SCRV", BitConverter.GetBytes(1u)));
            fields.Add(("SCTX", Text("This text has no authority to classify or execute these mixed cells.")));
        });
        File.WriteAllBytes(Path.Combine(directory, Plugin), authored);
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
        var effects = Bind(records, world, actor, skills); effects.Synchronize();
        var saved = JsonSerializer.Deserialize<FalloutPlayerAbilityScriptsSnapshot>(JsonSerializer.Serialize(effects.Capture()))!;
        var entry = saved.Effects.Single();
        Require(entry.Locals.Count == 3 && entry.Locals[0].Payload == 0x14 && entry.Locals[1].Payload == tail &&
            BitConverter.UInt64BitsToDouble(entry.Locals[2].Payload) == 1 && skills.CaptureValues().Pools[32].Permanent == 3 &&
            world.InstanceCount == 0, "Compiled effect views lost a duplicate tail, routed implicit locals to an actor, or invented a reference payload.");
        var cold = new FalloutPlayerAbilityScripts(records, skills.SelectedConstantEffects, skills.AbilityCondition, saved);
        cold.BindExecutor(_ => throw new InvalidOperationException("Cold completed Start cannot execute."));
        cold.Synchronize();
        Require(JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(cold.Capture()), "Cold raw mixed cells or completed effect cursor changed.");
    }

    private static void CheckCompiledAbilityAdmission(string directory)
    {
        foreach (var type in new ushort[] { 0, 1, 2, 0x0101 })
        {
            File.WriteAllBytes(Path.Combine(directory, Plugin), RewriteAbilityScript(Fixture(false), fields =>
            {
                var header = fields.Single(field => field.Signature == "SCHR").Bytes;
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), type);
            }));
            using var records = FalloutPluginStack.Load(directory, [Plugin]);
            using var world = new FalloutReferenceWorld(records);
            var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
            var effects = Bind(records, world, actor, skills); Reject(effects.Synchronize);
            Require(effects.Capture().Effects.Count == 0 && skills.CaptureValues().Pools.Count == 0,
                "An unrelated source script type was widened into active-effect execution.");
        }
        foreach (var eventType in new ushort[] { 18, 19, 0, 21 })
        {
            File.WriteAllBytes(Path.Combine(directory, Plugin), RewriteAbilityScript(Fixture(false), fields =>
                ReplaceAbilityCode(fields, Join(AbilityInstruction(0x1d), AbilityBlock(eventType, AbilityActorValue(32, 99, 1))), 2, 1)));
            using var records = FalloutPluginStack.Load(directory, [Plugin]);
            using var world = new FalloutReferenceWorld(records);
            var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
            var effects = Bind(records, world, actor, skills); Reject(effects.Synchronize);
            Require(skills.CaptureValues().Pools.Count == 0, "An unowned effect event/clock consumed its source body.");
        }
    }

    private static void CheckCompiledAbilityExternalOwner(string directory)
    {
        var code = Join(AbilityInstruction(0x1d), AbilityBlock(17, Join(
            AbilitySet('f', 1, Encoding.ASCII.GetBytes(" 9"), 3),
            AbilitySet('s', 1, Join([(byte)'r'], AbilityU16(3), [(byte)'f'], AbilityU16(1))))));
        var authored = RewriteAbilityScript(Fixture(false), fields =>
        {
            ReplaceAbilityCode(fields, code, 3, 1); fields.Add(("SCRO", BitConverter.GetBytes(0x20u)));
        });
        var questHeader = new byte[20]; questHeader[18] = 1;
        UInt(questHeader, 8, 4); UInt(questHeader, 12, 1); BinaryPrimitives.WriteUInt16LittleEndian(questHeader.AsSpan(16), 1);
        var local = new byte[24]; UInt(local, 0, 1); BinaryPrimitives.WriteDoubleLittleEndian(local.AsSpan(8), 5);
        File.WriteAllBytes(Path.Combine(directory, Plugin), Join(authored,
            Record("QUST", 0x20, Field("DATA", new byte[8]), Field("SCRI", BitConverter.GetBytes(0x31u))),
            Record("SCPT", 0x31, Field("SCHR", questHeader), Field("SCDA", AbilityInstruction(0x1d)),
                Field("SLSD", local), Field("SCVR", Text("external")))));
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
        var effects = Bind(records, world, actor, skills, quests: quests); effects.Synchronize();
        Require(quests.Variable(Key(0x20), 1) == 9 &&
            BitConverter.UInt64BitsToDouble(effects.Capture().Effects.Single().Locals.Single().Payload) == 9 &&
            world.InstanceCount == 0,
            "Explicit compiled variable owners were coerced into effect locals or implicit cells into the external script.");
    }

    private static void CheckCompiledAbilityOverride(string directory)
    {
        var input = Fixture(false); File.WriteAllBytes(Path.Combine(directory, Plugin), input);
        var script = ExtractAbilityScript(input);
        var overrideCode = Join(AbilityInstruction(0x1d), AbilityBlock(17,
            Join(AbilitySet('s', 1, Encoding.ASCII.GetBytes(" 1")), AbilityActorValue(32, 7, 1))));
        ReplaceAbilityCode(script, overrideCode, 2, 1);
        var header = Record("TES4", 0, Field("HEDR", new byte[12]), Field("MAST", Text(Plugin)), Field("DATA", new byte[8]));
        var overridePath = Path.Combine(directory, "Override.esp");
        File.WriteAllBytes(overridePath, Join(header, Record("SCPT", 0x30,
            script.Select(field => Field(field.Signature, field.Bytes)).ToArray()),
            Record("SCPT", 0x01000030, Field("SCHR", new byte[19]))));
        try
        {
            using var records = FalloutPluginStack.Load(directory, [Plugin, "Override.esp"]);
            using var world = new FalloutReferenceWorld(records);
            var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
            var effects = Bind(records, world, actor, skills); effects.Synchronize();
            Require(effects.Capture().Effects.Single().ScriptWinner == "Override.esp" &&
                skills.CaptureValues().Pools[32].Permanent == 7 && world.InstanceCount == 0,
                "Winning declaration/master scope selected another same-number Script or ran the overridden body.");
        }
        finally { File.Delete(overridePath); }
    }

    private static void ReplaceAbilityCode(List<(string Signature, byte[] Bytes)> fields, byte[] code, uint refs, uint locals)
    {
        var header = fields.Single(field => field.Signature == "SCHR").Bytes;
        UInt(header, 4, refs); UInt(header, 8, (uint)code.Length); UInt(header, 12, locals);
        fields.RemoveAll(field => field.Signature == "SCDA"); fields.Add(("SCDA", code));
    }
    private static void AddAbilityLocal(List<(string Signature, byte[] Bytes)> fields, uint slot, byte flags, string name, ulong initial)
    {
        var data = new byte[24]; UInt(data, 0, slot); data[16] = flags;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8), initial);
        fields.Add(("SLSD", data)); fields.Add(("SCVR", Text(name)));
    }
    private static byte[] RewriteAbilityScript(byte[] source, Action<List<(string Signature, byte[] Bytes)>> mutate)
    {
        var script = ExtractAbilityScript(source); mutate(script);
        var replacement = Record("SCPT", 0x30, script.Select(field => Field(field.Signature, field.Bytes)).ToArray());
        var at = 0;
        while (at < source.Length)
        {
            var size = checked(24 + (int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(at + 4)));
            if (Encoding.ASCII.GetString(source, at, 4) == "SCPT" && BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(at + 12)) == 0x30)
                return Join(source[..at], replacement, source[(at + size)..]);
            at = checked(at + size);
        }
        throw new InvalidDataException("Authored fixture has no script record.");
    }
    private static List<(string Signature, byte[] Bytes)> ExtractAbilityScript(byte[] source)
    {
        var at = 0;
        while (at < source.Length)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(at + 4)));
            if (Encoding.ASCII.GetString(source, at, 4) == "SCPT" && BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(at + 12)) == 0x30)
            {
                var fields = new List<(string, byte[])>(); var end = at + 24 + size; var field = at + 24;
                while (field < end)
                {
                    var extent = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(field + 4));
                    fields.Add((Encoding.ASCII.GetString(source, field, 4), source.AsSpan(field + 6, extent).ToArray())); field += 6 + extent;
                }
                if (field != end) throw new InvalidDataException("Authored fixture field extent is invalid.");
                return fields;
            }
            at += 24 + size;
        }
        throw new InvalidDataException("Authored fixture has no script record.");
    }
    private static byte[] AbilityInstruction(ushort opcode, byte[]? payload = null) =>
        Join(AbilityU16(opcode), AbilityU16(checked((ushort)(payload?.Length ?? 0))), payload ?? []);
    private static byte[] AbilityBlock(ushort kind, byte[] body) => Join(AbilityInstruction(0x10,
        Join(AbilityU16(kind), BitConverter.GetBytes(checked((uint)body.Length + 4)))), body, AbilityInstruction(0x11));
    private static byte[] AbilitySet(char kind, ushort slot, byte[] value, ushort? owner = null) => AbilityInstruction(0x15,
        Join(owner is { } reference ? Join([(byte)'r'], AbilityU16(reference)) : [],
            [(byte)kind], AbilityU16(slot), AbilityU16((ushort)value.Length), value));
    private static byte[] AbilityActorValue(ushort id, int amount, ushort receiver) =>
        Join(AbilityU16(0x1c), AbilityU16(receiver), AbilityInstruction(0x1010,
            Join(AbilityU16(2), AbilityU16(id), [(byte)'n'], BitConverter.GetBytes(amount))));
    private static byte[] AbilityU16(ushort value) => BitConverter.GetBytes(value);
}
