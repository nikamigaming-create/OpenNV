using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class DefaultActivationContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-default-activation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Activation.esm");
            File.WriteAllBytes(path, Fixture());
            var sourceHash = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Activation.esm"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var effects = new List<FalloutReferenceScriptEffect>();
            var rejectDefault = false;
            FalloutReferenceScripts Scripts(FalloutReferenceWorld owner) => new(records, owner, new(records), new((_, _) => false, effect =>
            {
                Require(effect.Kind == FalloutReferenceEffectKind.DefaultActivate && effect.Source == effect.Target &&
                    effect.Argument == records.RuntimeFormKey(0x14), "Default action changed its calling/target/action identity.");
                if (rejectDefault) throw new NotSupportedException("Fixture default interaction is unavailable.");
                effects.Add(effect);
            }));
            var scripts = Scripts(world);
            var fault = scripts.Dispatch(Key(0x900), "GameMode");
            Require(fault.Error?.Contains("MissingOperation", StringComparison.Ordinal) == true &&
                world.Get(Key(0x900)).Read(1) == 1 && world.Get(Key(0x900)).Read(2) == 0,
                "Fixture did not reach its unsupported operation after the real source prefix.");
            var stopped = JsonSerializer.Serialize(world.Get(Key(0x900)).Capture());
            var begins = 0; var ends = 0;
            var results = scripts.DispatchFrame(Key(0x900), [new("GameMode"), new("OnActivate", Key(0x14))], 1,
                () => begins++, () => ends++);
            Require(results.Single(result => result.Event == "OnActivate") is { Error: null, Blocks: 0, RecoveredError: null } &&
                results.Single(result => result.Event == "GameMode").Error == fault.Error && effects.Count == 1 && begins == 1 && ends == 1 &&
                JsonSerializer.Serialize(world.Get(Key(0x900)).Capture()) == stopped,
                "Independent default activation cleared, replayed or replaced the stopped invocation.");
            Require(scripts.CanAdmitActivation(Key(0x900)) && scripts.Activate(Key(0x900), Key(0x14)).Error is null &&
                effects.Count == 2 && JsonSerializer.Serialize(world.Get(Key(0x900)).Capture()) == stopped,
                "A fresh default activation repeated the consumed source prefix or duplicated one action.");
            rejectDefault = true;
            Require(scripts.Activate(Key(0x900), Key(0x14)).Error?.Contains("default interaction", StringComparison.Ordinal) == true &&
                effects.Count == 2 && JsonSerializer.Serialize(world.Get(Key(0x900)).Capture()) == stopped,
                "Default failure hid or replaced the historical source error.");
            rejectDefault = false;
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!); cold.LoadCell(cell);
            var coldScripts = Scripts(cold);
            Require(coldScripts.CanAdmitActivation(Key(0x900)) && coldScripts.Activate(Key(0x900), Key(0x14)).Error is null &&
                effects.Count == 3 && JsonSerializer.Serialize(cold.Get(Key(0x900)).Capture()) == stopped,
                "Cold default activation lost the exact source error, hash or consumed locals.");

            // Default interaction precedes ordinary VM evaluation, even if the
            // first GameMode invocation in that same frame will stop later.
            results = scripts.DispatchFrame(Key(0x904), [new("GameMode"), new("OnActivate", Key(0x14))], 0);
            Require(effects.Count == 4 && results.Single(result => result.Event == "OnActivate").Error is null &&
                results.Single(result => result.Event == "GameMode").Error is not null && world.Get(Key(0x904)).Read(1) == 1,
                "A later source VM failure suppressed an independent native action.");
            var blocked = scripts.Dispatch(Key(0x901), "GameMode");
            var blockedState = JsonSerializer.Serialize(world.Get(Key(0x901)).Capture());
            Require(blocked.Error is not null && !scripts.CanAdmitActivation(Key(0x901)) &&
                scripts.Activate(Key(0x901), Key(0x14)).Error == blocked.Error && effects.Count == 4 &&
                JsonSerializer.Serialize(world.Get(Key(0x901)).Capture()) == blockedState,
                "A source OnActivate block was bypassed after an unrelated fault.");
            foreach (var id in new uint[] { 0x902, 0x903 })
            {
                var parse = scripts.Dispatch(Key(id), "GameMode");
                var prior = JsonSerializer.Serialize(world.Get(Key(id)).Capture());
                Require(parse.Error is not null && !scripts.CanAdmitActivation(Key(id)) &&
                    scripts.Activate(Key(id), Key(0x14)).Error == parse.Error && effects.Count == 4 &&
                    JsonSerializer.Serialize(world.Get(Key(id)).Capture()) == prior,
                    "Missing or malformed source was treated as proof of no OnActivate.");
            }
            Require(scripts.Activate(Key(0x905), Key(0x14)) is { Error: null, Blocks: 1 } &&
                world.Get(Key(0x905)).Read(1) == 1 && effects.Count == 4,
                "An ordinary authored OnActivate lost its suppression of default activation.");
            var deathFault = scripts.Dispatch(Key(0x906), "OnDeath");
            var deathState = JsonSerializer.Serialize(world.Get(Key(0x906)).Capture());
            Require(deathFault.Error is not null && scripts.Activate(Key(0x906), Key(0x14)).Error is null &&
                JsonSerializer.Serialize(world.Get(Key(0x906)).Capture()) == deathState && effects.Count == 5,
                "Independent activation changed a consumed OnDeath prefix.");
            world.Get(Key(0x900)).Enabled = false;
            var disabled = JsonSerializer.Serialize(world.Get(Key(0x900)).Capture());
            Require(scripts.Activate(Key(0x900), Key(0x14)).Error is not null && effects.Count == 5 &&
                JsonSerializer.Serialize(world.Get(Key(0x900)).Capture()) == disabled,
                "Independent activation escaped existing enabled/default availability.");
            Reject(() => scripts.DispatchFrame(Key(0x905), [new("OnActivate", Key(0x14)), new("onactivate", Key(0x14))], 0));
            world.UnloadCell(cell.Cell.FormKey);
            Reject(() => scripts.Activate(Key(0x900), Key(0x14)));
            Require(effects.Count == 5 && sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),
                "Duplicate/unloaded activation dispatched an effect or source bytes changed.");
            Console.WriteLine("OPENNV_DEFAULT_ACTIVATION_CONTRACT_PASS noOnActivate=true retainedFaultAndPrefix=true " +
                "nativeBeforeVm=true activationErrorIndependent=true cold=true authoredAndUnknownGuarded=true disabledAndResidencyGuarded=true sourceUnchanged=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Fixture()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var references = Enumerable.Range(0, 7).Select(index => Record("REFR", (uint)(0x900 + index),
            Field("NAME", BitConverter.GetBytes((uint)(1 + index))), Field("DATA", new byte[24]))).SelectMany(value => value).ToArray();
        var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); references.CopyTo(group, 24);
        const string failed = "set prefix to prefix + 1\nMissingOperation\nset suffix to suffix + 1";
        return Join(Record("TES4", 0, Field("HEDR", header)),
            Enumerable.Range(0, 7).Select(index => Record("ACTI", (uint)(1 + index), Field("EDID", Text("ActivationBase" + index)),
                Field("SCRI", BitConverter.GetBytes((uint)(0x50 + index))))).SelectMany(value => value).ToArray(),
            Script(0x50, "begin GameMode\n" + failed + "\nend"),
            Script(0x51, "begin GameMode\n" + failed + "\nend\nbegin oNaCtIvAtE player\nset suffix to suffix + 1\nend"),
            Script(0x52, "begin GameMode\nset prefix to 1"), Script(0x53, null),
            Script(0x54, "begin GameMode\n" + failed + "\nend"),
            Script(0x55, "begin OnActivate player\nset prefix to prefix + 1\nend"),
            Script(0x56, "begin OnDeath\n" + failed + "\nend"), Record("CELL", 0x800, Field("DATA", [1])), group);
    }

    private static byte[] Script(uint id, string? source)
    {
        var header = new byte[20]; UInt(header, 12, 2);
        return Record("SCPT", id, Field("SCHR", header), Local(1, "prefix"), Local(2, "suffix"),
            Field("SCRO", BitConverter.GetBytes(0x14u)), source is null ? [] : Field("SCTX", Text(source)));
    }
    private static byte[] Local(uint index, string name)
    {
        var data = new byte[24]; UInt(data, 0, index);
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static FalloutFormKey Key(uint id) => new("Activation.esm", id);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid activation admission was accepted.");
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void UInt(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var payload = Join(fields); var result = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        UInt(result, 4, (uint)payload.Length); UInt(result, 12, id); payload.CopyTo(result, 24); return result;
    }
}
