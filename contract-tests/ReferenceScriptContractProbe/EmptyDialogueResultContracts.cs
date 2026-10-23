using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class EmptyDialogueResultContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-empty-info-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Empty.esm"), Join(Header(), Record("QUST", 0x100),
                Info(0x200, Result(5, "Activate"), Result(0, "MissingActor.PlayIdle MissingIdle", references: 1)),
                Info(0x201, Result(0, "MissingCommand"), Result(5, "Activate")),
                Info(0x202, Result(0, "MissingCommand", actualBytes: 1), []),
                Info(0x203, Join(Result(0, "MissingCommand"), Field("SCHR", new byte[20])), []),
                Info(0x204, Join(Field("SCHR", new byte[19]), Field("SCTX", Text("MissingCommand"))), []),
                Info(0x205, Result(0, "MissingCommand"), Join(Result(0, "MissingCommand", references: 1),
                    Field("SCRO", BitConverter.GetBytes(0x999u)))),
                Info(0x206, Join(Result(0, "MissingCommand"), Field("SCRO", [1, 2, 3])), [])));
            using var records = FalloutPluginStack.Load(directory, ["Empty.esm"]);
            var info = FalloutDialogueTopic.Decode(records.GetEffective(Key(0x200)));
            Require(info.BeginScript == "Activate" && info.EndScript.Length == 0,
                "A zero-byte end result executed editor text or suppressed its nonempty begin sibling.");
            var reverse = FalloutDialogueTopic.Decode(records.GetEffective(Key(0x201)));
            Require(reverse.BeginScript.Length == 0 && reverse.EndScript == "Activate",
                "Begin/end result extent ownership crossed NEXT.");
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var effects = new List<FalloutReferenceScriptEffect>();
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effects.Add));
            scripts.ExecuteResult(info, Key(0x14), true);
            Require(effects.Count == 1 && effects[0].Kind == FalloutReferenceEffectKind.DefaultActivate,
                "The existing nonempty result lost its production executor.");
            var before = JsonSerializer.Serialize(world.Capture());
            scripts.ExecuteResult(info, Key(0x14), false);
            scripts.ExecuteResult(FalloutDialogueTopic.Decode(records.GetEffective(Key(0x205))), Key(0x14), false);
            Require(effects.Count == 1 && before == JsonSerializer.Serialize(world.Capture()),
                "Empty result queried missing bindings or produced a world effect.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(before)!);
            var coldEffects = new List<FalloutReferenceScriptEffect>();
            var coldScripts = new FalloutReferenceScripts(records, cold, new(records), new((_, _) => false, coldEffects.Add));
            coldScripts.ExecuteResult(info, Key(0x14), false);
            Require(coldEffects.Count == 0 && before == JsonSerializer.Serialize(cold.Capture()),
                "Cold reconstruction admitted editor-only result text.");
            foreach (var id in new uint[] { 0x202, 0x203, 0x204, 0x206 })
                Reject(() => FalloutDialogueTopic.Decode(records.GetEffective(Key(id))));
            Console.WriteLine("OPENNV_EMPTY_INFO_RESULT_CONTRACT_PASS explicitZeroExtent=true sourceNotExecuted=true " +
                "nonemptySibling=true actualResultExecutor=true coldNoEffects=true mismatchedBodyRefused=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static void RunOwned(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var record = records.GetEffective(records.RuntimeFormKey(0x15ad0d));
        Require(record.Signature == "INFO", "Owned empty-result fixture is not INFO.");
        var fields = record.ReadSubrecords().ToArray();
        var split = Array.FindIndex(fields, field => field.Signature == "NEXT");
        Require(split >= 0, "Owned result has no independent end scope.");
        var end = fields[(split + 1)..];
        var header = FalloutScriptHeader.Read(end.Single(field => field.Signature == "SCHR").Data.Span);
        Require(header.CompiledBytes == 0 && header.ReferenceCount == 1 &&
            !end.Any(field => field.Signature is "SCDA" or "SCRO" or "SCRV") &&
            FalloutDialogueTopic.ScriptText(end.Single(field => field.Signature == "SCTX").Data.Span)
                .Contains(".PlayIdle ", StringComparison.Ordinal), "Owned result source premise changed.");
        var original = Hash(record);
        var info = FalloutDialogueTopic.Decode(record);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var effects = new List<FalloutReferenceScriptEffect>();
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effects.Add));
        var before = JsonSerializer.Serialize(new { references = world.Capture(), quests = quests.Capture() });
        scripts.ExecuteResult(info, records.RuntimeFormKey(0x14), false);
        Require(info.EndScript.Length == 0 && effects.Count == 0 && original == Hash(record) &&
            before == JsonSerializer.Serialize(new { references = world.Capture(), quests = quests.Capture() }),
            "Owned empty result executed editor text, changed source, or mutated state.");
        Console.WriteLine("OPENNV_OWNED_EMPTY_INFO_RESULT_PASS info=FalloutNV.esm:15ad0d explicitZeroExtent=true " +
            "staleReferenceCount=1 actualResultExecutor=true effects=0 sourceUnchanged=true " +
            "controlledCaller=player14 ordinaryConversationAndRetail=unverified");
    }

    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(
        Join(record.ReadSubrecords().Select(field => Field(field.Signature, field.Data.ToArray())).ToArray())));
    private static byte[] Result(uint size, string source, uint references = 0, int? actualBytes = null)
    {
        var header = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), references);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), size);
        return Join(Field("SCHR", header), size != 0 || actualBytes is not null ?
            Field("SCDA", new byte[actualBytes ?? checked((int)size)]) : [], Field("SCTX", Text(source)));
    }
    private static byte[] Info(uint id, byte[] begin, byte[] end) => Record("INFO", id,
        Field("DATA", [0, 0, 0]), Field("QSTI", BitConverter.GetBytes(0x100u)), begin, Field("NEXT", []), end);
    private static byte[] Header()
    {
        var data = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
        return Record("TES4", 0, Field("HEDR", data));
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static FalloutFormKey Key(uint id) => new("Empty.esm", id);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidDataException("Malformed explicit empty result was admitted.");
    }
}
