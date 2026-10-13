using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class ScreenBloodContracts
{
    internal static void RunCompiledHeadless()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-headless-blood-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Blood.esm");
            File.WriteAllBytes(path, CompiledBloodFixture());
            var sourceHash = SHA256.HashData(File.ReadAllBytes(path));
            var ini = Path.Combine(directory, "blood.ini");
            File.WriteAllText(ini, "[ScreenSplatter]\nbBloodSplatterEnabled=1\n");
            var enabled = FalloutInstallationSettings.ReadLayers([ini]).Boolean("ScreenSplatter", "bBloodSplatterEnabled");
            using var records = FalloutPluginStack.Load(directory, ["Blood.esm"]);
            var declaration = FalloutCompiledCommandDeclarations.Get(0x1205);
            Require(declaration is { Name: "TriggerScreenBlood", Alias: "tsb", Required: 1, RequiresReference: false } &&
                declaration.Parameters.SequenceEqual(new byte[] { 1 }) &&
                FalloutCompiledSemanticBinding.Read(declaration).Kind == FalloutCompiledSemanticKind.Effect,
                "Original screen-blood declaration was classified as a query or lost its signed count/alias.");
            var mask = BloodDds(0x51); var color = BloodDds(0x72); var reads = 0;
            FalloutScreenBloodMediaReader Media(bool malformed = false) => new(asset =>
            {
                ++reads;
                var bytes = asset.Contains("alpha", StringComparison.Ordinal) ? mask : malformed ? color[..^1] : color;
                return (bytes, "synthetic-owned-byte-resource:" + asset);
            });
            FalloutReferenceScripts Scripts(FalloutReferenceWorld world, FalloutQuestState quests) => new(records, world, quests,
                new((_, _) => false, _ => throw new InvalidDataException("Blood invented an unrelated effect."),
                    Command: (_, _, _, _) => throw new InvalidDataException("Compiled blood interpreted contradictory source text.")));
            var cell = FalloutCellSceneReader.Read(records, Key(0x800));
            var player = records.RuntimeFormKey(FalloutScriptBindings.PlayerReferenceId);
            using (var world = new FalloutReferenceWorld(records))
            {
                world.LoadCell(cell); var quests = new FalloutQuestState(records);
                var questScripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new(),
                    defaultProcessingDelay: 0, references: world);
                Require(ReferenceEquals(questScripts.ScreenBlood, world.ScreenBlood), "Headless blood split reference and quest authority.");
                var media = Media();
                using var adapter = new FalloutHeadlessScreenBlood(world.ScreenBlood, enabled, media);
                var result = Scripts(world, quests).Dispatch(Key(0x900), "OnTriggerEnter", player);
                Require(result is { Error: null, Blocks: 1 } && world.Get(Key(0x900)).Read(1) == 3 &&
                    world.Get(Key(0x900)).Read(2) == 7 && world.ScreenBlood.ActiveDrops == 1 && adapter.ActiveGroups == 1 &&
                    world.ScreenBlood.LastMedia![0].Sha256 == Convert.ToHexString(SHA256.HashData(mask)) &&
                    world.ScreenBlood.LastMedia[1].Sha256 == Convert.ToHexString(SHA256.HashData(color)),
                    "Original SCDA effect did not commit real media identities and its following local store.");
                var output = JsonSerializer.SerializeToElement(world.ScreenBlood.State).GetProperty("output");
                Require(!output.GetProperty("nativeResources").GetBoolean() && output.GetProperty("drawCount").GetInt32() == 0 &&
                    output.GetProperty("finalPixels").GetString() == "UNEXECUTED",
                    "Shared screen-blood telemetry hid the headless output boundary.");
                var request = world.ScreenBlood.LastRequest!;
                Require(request.Drops.All(drop => drop.HalfSize is >= .12f and <= .18f && drop.Opacity == .1f),
                    "Headless output replaced shared source geometry or alternate opacity.");
                adapter.Advance(10, false); Require(adapter.Fade(request.Id) == 1, "Headless blood advanced through a modal menu.");
                adapter.Advance(1.5, true); Require(Math.Abs(adapter.Fade(request.Id) - .5f) < .00001f, "Headless blood lost shared fade timing.");
                adapter.Advance(.5, true); Require(adapter.ActiveGroups == 0 && world.ScreenBlood.ActiveDrops == 0, "Headless expiration leaked its group.");
                world.ScreenBlood.Trigger(Key(0x900), 1);
                Require(media.ValidatedCount == 2 && reads == 2, "Repeated headless effects reread the selected media graph.");
                var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
                using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
                using var coldAdapter = new FalloutHeadlessScreenBlood(cold.ScreenBlood, enabled, Media());
                var coldScripts = Scripts(cold, quests);
                Require(coldScripts.Dispatch(Key(0x900), "GameMode") is { Error: null, Blocks: 0 } &&
                    cold.ScreenBlood.LastRequest is null && coldAdapter.ActiveGroups == 0 && cold.Get(Key(0x900)).Read(2) == 7,
                    "Cold construction replayed a completed transient screen effect.");
            }
            using (var world = new FalloutReferenceWorld(records))
            {
                world.LoadCell(cell); var quests = new FalloutQuestState(records);
                using var adapter = new FalloutHeadlessScreenBlood(world.ScreenBlood, enabled, Media(malformed: true));
                var scripts = Scripts(world, quests);
                var failed = scripts.Dispatch(Key(0x901), "OnTriggerEnter", player);
                Require(failed.Error is not null && world.Get(Key(0x901)).Read(1) == 3 && world.Get(Key(0x901)).Read(2) == 0 &&
                    adapter.ActiveGroups == 0 && world.ScreenBlood.LastRequest is null && world.ScreenBlood.ActiveDrops == 0,
                    "Malformed source DDS consumed a request, leaked a group or executed the compiled suffix.");
                var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
                using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
                using var coldAdapter = new FalloutHeadlessScreenBlood(cold.ScreenBlood, enabled, Media());
                var before = JsonSerializer.Serialize(cold.Capture());
                Require(Scripts(cold, quests).Dispatch(Key(0x901), "OnTriggerEnter", player).Error == failed.Error &&
                    before == JsonSerializer.Serialize(cold.Capture()) && cold.ScreenBlood.LastRequest is null,
                    "Cold resource failure replayed its retained compiled prefix.");
            }
            using (var world = new FalloutReferenceWorld(records))
            {
                world.LoadCell(cell); var quests = new FalloutQuestState(records);
                using var adapter = new FalloutHeadlessScreenBlood(world.ScreenBlood, enabled, Media());
                Require(Scripts(world, quests).Dispatch(Key(0x902), "OnTriggerEnter", player).Error is not null &&
                    world.Get(Key(0x902)).Read(1) == 3 && world.Get(Key(0x902)).Read(2) == 0 && world.ScreenBlood.LastRequest is null,
                    "Malformed compiled count ran an effect or discarded its committed local prefix.");
                Require(Scripts(world, quests).Dispatch(Key(0x903), "OnTriggerEnter", player).Error is null &&
                    world.ScreenBlood.LastRequest is { Requested: uint.MaxValue, Drops.Count: 3 },
                    "Compiled signed count lost its original payload or shared active capacity.");
            }
            using (var world = new FalloutReferenceWorld(records))
            {
                world.LoadCell(cell); var quests = new FalloutQuestState(records); var before = reads;
                using var adapter = new FalloutHeadlessScreenBlood(world.ScreenBlood, false, Media());
                Require(Scripts(world, quests).Dispatch(Key(0x900), "OnTriggerEnter", player).Error is null &&
                    world.Get(Key(0x900)).Read(2) == 7 && reads == before && world.ScreenBlood.LastDisposition == "source-disabled",
                    "Source-disabled blood prepared a resource or stopped the compiled suffix.");
            }
            HeadlessBloodResourceAtomicity(records, mask, color);
            Require(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Compiled blood changed original source bytes.");
            Console.WriteLine("OPENNV_COMPILED_HEADLESS_SCREEN_BLOOD_PASS originalScda=true sourceDeclaration=true " +
                "sharedState=true ownedBytesHash=true resourceAtomic=true prefixFailure=true coldNoReplay=true " +
                "geometryFadeMenuLifetime=true sourceDisabled=true nativeResources=false draws=0 parity=unverified gameplay=UNEXECUTED");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void HeadlessBloodResourceAtomicity(FalloutPluginStack records, byte[] mask, byte[] color)
    {
        var failed = true;
        var owner = new FalloutScreenBlood(records, 123); var control = new FalloutScreenBlood(records, 123);
        using var adapter = new FalloutHeadlessScreenBlood(owner, true, new(asset =>
            (asset.Contains("alpha", StringComparison.Ordinal) ? mask : failed ? color[..^1] : color, "synthetic-resource:" + asset)));
        using var controlAdapter = new FalloutHeadlessScreenBlood(control, true, new(asset =>
            (asset.Contains("alpha", StringComparison.Ordinal) ? mask : color, "synthetic-resource:" + asset)));
        Reject(() => owner.Trigger(Key(0x900), 1)); failed = false;
        owner.Trigger(Key(0x900), 1); control.Trigger(Key(0x900), 1);
        Require(owner.LastRequest!.RandomBefore == 123 && owner.LastRequest.RandomAfter == control.LastRequest!.RandomAfter &&
            JsonSerializer.Serialize(owner.LastRequest.Drops) == JsonSerializer.Serialize(control.LastRequest.Drops),
            "Failed headless media validation consumed shared random state or geometry.");
        Reject(() => FalloutScreenBloodMediaReader.Validate(color[..^1]));
        Reject(() => FalloutScreenBloodMediaReader.Validate(Join(color, [0])));
        var tooSmall = color.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(tooSmall.AsSpan(16), 1);
        Reject(() => FalloutScreenBloodMediaReader.Validate(tooSmall));
        var pitched = new byte[140]; color.AsSpan(0, 128).CopyTo(pitched);
        BinaryPrimitives.WriteUInt32LittleEndian(pitched.AsSpan(8), 0x100f);
        BinaryPrimitives.WriteUInt32LittleEndian(pitched.AsSpan(20), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(pitched.AsSpan(80), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(pitched.AsSpan(88), 24);
        Reject(() => FalloutScreenBloodMediaReader.Validate(pitched));
        var partial = new byte[160]; color.AsSpan(0, 128).CopyTo(partial);
        BinaryPrimitives.WriteUInt32LittleEndian(partial.AsSpan(8), 0xa1007);
        BinaryPrimitives.WriteUInt32LittleEndian(partial.AsSpan(12), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(partial.AsSpan(16), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(partial.AsSpan(28), 2);
        FalloutScreenBloodMediaReader.Validate(partial);
        adapter.Dispose(); Require(adapter.ActiveGroups == 0 && owner.ActiveDrops == 0, "Headless retirement leaked group capacity.");
    }

    private static byte[] BloodDds(byte value)
    {
        var bytes = new byte[144]; "DDS "u8.CopyTo(bytes);
        void Word(int offset, uint data) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), data);
        Word(4, 124); Word(8, 0x81007); Word(12, 2); Word(16, 2); Word(20, 16); Word(76, 32); Word(80, 4);
        "DXT5"u8.CopyTo(bytes.AsSpan(84)); Word(108, 0x1000); bytes.AsSpan(128).Fill(value);
        return bytes;
    }

    private static byte[] CompiledBloodFixture()
    {
        byte[] Instruction(ushort opcode, byte[]? payload = null) => Join(BitConverter.GetBytes(opcode),
            BitConverter.GetBytes((ushort)(payload?.Length ?? 0)), payload ?? []);
        byte[] Set(ushort slot, string literal)
        {
            var expression = System.Text.Encoding.ASCII.GetBytes(literal);
            return Instruction(0x15, Join([(byte)'f'], BitConverter.GetBytes(slot), BitConverter.GetBytes((ushort)expression.Length), expression));
        }
        byte[] Script(uint id, byte[] arguments)
        {
            var body = Join(Set(1, " 3"), Instruction(0x1205, arguments), Set(2, " 7"));
            var code = Join(Instruction(0x1d), Instruction(0x10, Join(BitConverter.GetBytes(FalloutCompiledScriptEvents.TriggerEnter),
                BitConverter.GetBytes((uint)body.Length + 4))), body, Instruction(0x11));
            var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)code.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2); header[18] = 1;
            byte[] Local(uint slot, string name) { var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, slot); return Join(Field("SLSD", local), Field("SCVR", Text(name))); }
            return Record("SCPT", id, Field("SCHR", header), Field("SCDA", code), Local(1, "prefix"), Local(2, "suffix"),
                Field("SCTX", Text("begin OnTriggerEnter\nset suffix to 999\nend")));
        }
        byte[] Argument(int count) => Join(BitConverter.GetBytes((ushort)1), [(byte)'n'], BitConverter.GetBytes(count));
        var references = Enumerable.Range(0, 4).Select(index => Record("REFR", (uint)(0x900 + index),
            Field("NAME", BitConverter.GetBytes((uint)(0x100 + index))), Field("DATA", new byte[24]))).SelectMany(value => value).ToArray();
        var group = new byte[24 + references.Length]; "GRUP"u8.CopyTo(group);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 6);
        references.CopyTo(group, 24);
        return Join(Record("TES4", 0), Setting(0x201, "iBloodSplatterMaxCount", 3), Setting(0x202, "fBloodSplatterDuration", 2f),
            Setting(0x203, "fBloodSplatterFadeStart", .5f), Setting(0x204, "fBloodSplatterMinSize", .12f), Setting(0x205, "fBloodSplatterMaxSize", .18f),
            Setting(0x206, "fBloodSplatterMinOpacity", .5f), Setting(0x207, "fBloodSplatterMaxOpacity", 1f),
            Setting(0x208, "fBloodSplatterMinOpacity2", .1f), Setting(0x209, "fBloodSplatterMaxOpacity2", .1f),
            Setting(0x210, "fBloodSplatterOpacityChance", 1f), Setting(0x211, "sBloodSplatterAlpha01OPTFilename", "textures/alpha.dds"),
            Setting(0x212, "sBloodSplatterColor01OPTFilename", "textures/color.dds"),
            Enumerable.Range(0, 4).Select(index => Record("STAT", (uint)(0x100 + index),
                Field("SCRI", BitConverter.GetBytes((uint)(0x601 + index))))).SelectMany(value => value).ToArray(),
            Script(0x601, Argument(1)), Script(0x602, Argument(1)),
            Script(0x603, Join(BitConverter.GetBytes((ushort)2), [(byte)'n'], BitConverter.GetBytes(1), [(byte)'n'], BitConverter.GetBytes(2))),
            Script(0x604, Argument(-1)), Record("CELL", 0x800, Field("DATA", [1])), group);
    }
}
