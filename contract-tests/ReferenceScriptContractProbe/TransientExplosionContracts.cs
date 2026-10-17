using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class TransientExplosionContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-transient-explosion-");
        try
        {
            var header = new byte[20]; UInt(header, 12, 2);
            var local = new byte[24]; UInt(local, 0, 1); local[16] = 1;
            var count = new byte[24]; UInt(count, 0, 2);
            var reference = Record("REFR", 0x900, Field("EDID", Text("Marker")), Field("NAME", BitConverter.GetBytes(0x801u)), Field("DATA", new byte[24]));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Explosion.esm"), Join(Record("TES4", 0),
                Explosion(0x800, 0), Explosion(0x803, 10), Record("ACTI", 0x801, Field("SCRI", BitConverter.GetBytes(0x802u))),
                Record("SCPT", 0x802, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("created")),
                    Field("SLSD", count), Field("SCVR", Text("count")),
                    Field("SCRO", BitConverter.GetBytes(0x800u)), Field("SCRO", BitConverter.GetBytes(0x900u)),
                    Field("SCTX", Text("ref created\nshort count\nbegin GameMode\nif count == 0\nset created to PlaceAtMe Impulse\nMarker.PlaceAtMe Impulse\nset count to 1\nendif\nend"))),
                Record("CELL", 0x810, Field("DATA", [1])), Group(0x810, reference)));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Explosion.esm"]);
            CheckSchemaAdmission(Path.Combine(directory.FullName, "invalid-save.json"));
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x810)));
            var completions = new Dictionary<uint, Action>();
            var starts = 0;
            world.TransientExplosions.Prepare = request => new(world.Placement(request.Caller), (id, completed, _) =>
            { starts++; completions.Add(id, completed); }, () => { });
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => { }));
            var result = scripts.Dispatch(Key(0x900), "GameMode");
            Check(result.Error is null && starts == 2 && world.Get(Key(0x900)).Read(1) == 0xff000001 &&
                world.Get(Key(0x900)).Read(2) == 1, "Script statement or typed PlaceAtMe result failed: " + result.Error);
            Reject(() => world.Capture());
            completions[0xff000001]();
            Reject(() => world.TransientExplosions.Capture());
            completions[0xff000002]();
            var snapshot = Cold(world.TransientExplosions.Capture());
            using var restored = new FalloutReferenceWorld(records);
            restored.TransientExplosions.Restore(snapshot);
            restored.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            restored.ValidateValueHandles();
            Check(starts == 2 && restored.Get(Key(0x900)).Read(1) == 0xff000001 && restored.TransientExplosions.SaveBlocker is null,
                "Cold restore lost the retired reference or replayed its blast.");
            Reject(() => FalloutScriptValue.Form(0xff000001).FormKey(records));
            Reject(() => restored.TransientExplosions.ValidateFormHandle(0xff000003));
            restored.TransientExplosions.Prepare = request => new(world.Placement(request.Caller), (id, done, _) => { starts++; done(); }, () => { });
            var function = restored.TransientExplosions.Function(() => Key(0x900));
            var value = function.InvokeValue([new(FalloutScriptValue.Form(0x800))]);
            Check(value.Kind == FalloutScriptValueKind.Form && value.Number == 0xff000003 && starts == 3,
                "Cold transient allocator reused a retired identity.");
            var before = JsonSerializer.Serialize(restored.TransientExplosions.Capture());
            foreach (var arguments in new FalloutScriptArgument[][]
            {
                [new(FalloutScriptValue.Form(0x800)), new(2)],
                [new(FalloutScriptValue.Form(0x800)), new(1), new(1)],
                [new(FalloutScriptValue.Form(0x800)), new(1), new(0), new(1)],
                [new(FalloutScriptValue.Form(0x803))], [new(FalloutScriptValue.Form(0x801))]
            }) Reject(() => function.InvokeValue(arguments));
            Check(starts == 3 && before == JsonSerializer.Serialize(restored.TransientExplosions.Capture()),
                "Refused placement changed the allocator or emitted a blast.");
            Reject(() => new FalloutTransientExplosions(records).Restore(snapshot with { LastId = 3 }));
            Reject(() => new FalloutTransientExplosions(records).Restore(snapshot with { Retired = [snapshot.Retired[0], snapshot.Retired[0]] }));
            var failed = new FalloutTransientExplosions(records)
            {
                Prepare = request => new(world.Placement(request.Caller), (_, _, fail) => fail("lost visual owner"), () => { })
            };
            Reject(() => failed.Function(() => Key(0x900)).InvokeValue([new(FalloutScriptValue.Form(0x800))]));
            Reject(() => failed.Capture());
            Reject(() => failed.Function(() => Key(0x900)).InvokeValue([new(FalloutScriptValue.Form(0x800))]));
            Console.WriteLine("OPENNV_TRANSIENT_EXPLOSION_PASS typedResult=true scriptStatement=true activeSaveRefused=true retiredCold=true monotonic=true invalidAtomic=true failedRetryRefused=true");
        }
        finally { directory.Delete(true); }
    }

    internal static void RunOwned(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var script = records.GetEffective(new("FalloutNV.esm", 0x11e65e));
        var fields = script.ReadSubrecords().ToArray();
        var hash = SHA256.HashData(script.ReadData());
        var source = FalloutDialogueTopic.ScriptText(fields.Single(field => field.Signature == "SCTX").Data.Span);
        var command = FalloutDialogueTopic.CodeLines(source).Single(line => line.Contains(".PlaceAtMe ", StringComparison.OrdinalIgnoreCase));
        var tokens = command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Check(tokens.Length == 2, "Owned source no longer declares ordinary single-reference PlaceAtMe.");
        var bindings = new FalloutScriptBindings(records, script, script, fields);
        var caller = bindings.Reference(tokens[0].Split('.')[0]);
        var explosion = bindings.Form(tokens[1]).FormKey;
        using var world = new FalloutReferenceWorld(records);
        var placement = world.Placement(caller);
        var starts = 0;
        world.TransientExplosions.Prepare = request =>
        {
            Check(request.Caller == caller && request.Explosion.Form == explosion && request.Explosion.Force == 25 &&
                request.Explosion.Damage == 0 && request.Explosion.Radius == 256 && request.Explosion.UsesWorldOrientation,
                "Owned command did not resolve the source-authored impulse.");
            return new(placement, (_, done, _) => { starts++; done(); }, () => { });
        };
        var handle = world.TransientExplosions.Function(() => caller)
            .InvokeValue([new(FalloutScriptValue.Form(records.RuntimeFormId(explosion)))]);
        var cold = new FalloutTransientExplosions(records);
        cold.Restore(Cold(world.TransientExplosions.Capture()));
        cold.ValidateFormHandle(handle.Number);
        Check(starts == 1 && hash.SequenceEqual(SHA256.HashData(script.ReadData())), "Owned source changed or a blast replayed on restore.");
        Console.WriteLine($"OPENNV_OWNED_TRANSIENT_EXPLOSION_PASS script={script.FormKey} caller={caller} explosion={explosion} typedResult=true retiredCold=true sourceReadonly=true physics=unverified rendering=unverified campaign=unverified");
    }

    private static FalloutTransientExplosionsSnapshot Cold(FalloutTransientExplosionsSnapshot value) =>
        JsonSerializer.Deserialize<FalloutTransientExplosionsSnapshot>(JsonSerializer.Serialize(value))!;
    private static void CheckSchemaAdmission(string path)
    {
        var prior = new FalloutNativeCampaignState(FalloutNativeCampaignSave.BeforeTransientExplosionsSchema,
            "source", Key(0x810), "quest", 0, "Player", null!, null!, [], [], [], [], [], [], []);
        var admitted = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(prior);
        Check(admitted.Schema == FalloutNativeCampaignSave.ExpectedSchema && admitted.TransientExplosions is { LastId: 0, Retired.Count: 0 },
            "Schema50 did not explicitly initialize absent transient history.");
        Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(prior with { TransientExplosions = new(0, []) }));
        Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(prior with
        { Music = new(Key(0x800), new string('A', 64), null, null, 0, 0, false) }));
        Reject(() => FalloutNativeCampaignSave.Write(path, prior with { Schema = FalloutNativeCampaignSave.ExpectedSchema }));
        Check(!File.Exists(path), "A current save without allocator authority reached the filesystem.");
    }
    private static FalloutFormKey Key(uint id) => new("Explosion.esm", id);
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidDataException("Invalid transient explosion state was admitted.");
    }
    private static byte[] Explosion(uint id, float damage)
    {
        var data = new byte[52]; BinaryPrimitives.WriteSingleLittleEndian(data, 25);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), damage);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8), 256); UInt(data, 20, 3);
        return Record("EXPL", id, Field("EDID", Text(id == 0x800 ? "Impulse" : "DamageExplosion")), Field("DATA", data));
    }
    private static byte[] Group(uint cell, byte[] data)
    {
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)bytes.Length); UInt(bytes, 8, cell); UInt(bytes, 12, 6); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void UInt(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)data.Length); UInt(bytes, 12, id); data.CopyTo(bytes, 24); return bytes;
    }
}
