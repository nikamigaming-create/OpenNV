using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ScreenBloodContracts
{
    private sealed class Presentation
    {
        internal int Releases;
        internal float Fade;
        internal FalloutScreenBloodPresentation Prepare(FalloutScreenBloodRequest request) => new(
            [new(request.Mask, "synthetic-mask", new string('a', 64)), new(request.Color, "synthetic-color", new string('b', 64))],
            value => Fade = value, () => Releases++, ["synthetic-partial-lighting"]);
    }
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-screen-blood-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string source = "short done\nbegin GameMode\nif done == 0\nTriggerScreenBlood 1\nset done to 1\nendif\nend";
            var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1); header[16] = 1;
            var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, 1);
            File.WriteAllBytes(Path.Combine(directory, "Blood.esm"), Join(Record("TES4", 0),
                Setting(0x101, "iBloodSplatterMaxCount", 3), Setting(0x102, "fBloodSplatterDuration", 20f),
                Setting(0x103, "fBloodSplatterFadeStart", .5f), Setting(0x104, "fBloodSplatterMinSize", .12f),
                Setting(0x105, "fBloodSplatterMaxSize", .18f), Setting(0x106, "fBloodSplatterMinOpacity", .5f),
                Setting(0x107, "fBloodSplatterMaxOpacity", 1f), Setting(0x108, "fBloodSplatterMinOpacity2", .1f),
                Setting(0x109, "fBloodSplatterMaxOpacity2", .1f), Setting(0x110, "fBloodSplatterOpacityChance", 1f),
                Setting(0x111, "sBloodSplatterAlpha01OPTFilename", "textures/base-alpha.dds"),
                Setting(0x112, "sBloodSplatterColor01OPTFilename", "textures/color.dds"),
                Record("SCPT", 0x601, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("done")), Field("SCTX", Text(source))),
                Record("QUST", 0x600, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x601u)))));
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Record("TES4", 0, Field("MAST", Text("Blood.esm"))),
                Setting(0x102, "fBloodSplatterDuration", 2f), Setting(0x111, "sBloodSplatterAlpha01OPTFilename", "textures/winner-alpha.dds")));
            using var records = FalloutPluginStack.Load(directory, ["Blood.esm", "Patch.esp"]);
            using var world = new FalloutReferenceWorld(records); var blood = world.ScreenBlood;
            var presentations = new List<Presentation>(); var fail = false; var badIdentity = false;
            FalloutScreenBloodPresentation Prepare(FalloutScreenBloodRequest request)
            {
                if (fail) throw new InvalidDataException("Synthetic texture decode failure.");
                var presentation = new Presentation(); presentations.Add(presentation);
                var prepared = presentation.Prepare(request);
                return badIdentity ? prepared with { Media = [prepared.Media[0] with { Sha256 = "invalid" }, prepared.Media[1]] } : prepared;
            }
            using var binding = blood.Bind(true, Prepare);
            var caller = Key(0x600); blood.Trigger(caller, 1);
            Require(blood.LastRequest is { Mask: "textures/winner-alpha.dds", Duration: 2 } && blood.ActiveDrops == 1 &&
                blood.LastRequest.Drops.All(drop => drop.X is >= -1 and <= 1 && drop.Y is >= -1 and <= 1 &&
                    drop.HalfSize is >= .12f and <= .18f && drop.Opacity == .1f && drop.AtlasU is 0 or .5f && drop.AtlasV is 0 or .5f),
                "Blood ignored winning settings, the alternate opacity, or source atlas geometry.");
            blood.Trigger(caller, 999);
            Require(blood.ActiveDrops == 3 && blood.LastRequest!.Drops.Count == 2, "Screen blood did not cap against remaining active drops.");
            var previous = blood.LastRequest; blood.Trigger(caller, 0); blood.Trigger(caller, 999);
            Require(ReferenceEquals(previous, blood.LastRequest), "Zero/full capacity consumed a request.");
            blood.Advance(10, false);
            Require(blood.ActiveDrops == 3 && presentations.All(presentation => presentation.Fade == 1), "Menu pause advanced effect lifetime.");
            records.NumericSettings.Set("fBloodSplatterDuration", 4);
            blood.Advance(1.5, true);
            Require(presentations.All(presentation => Math.Abs(presentation.Fade - .5f) < .00001), "Active groups lost captured duration or normalized fade.");
            records.NumericSettings.Set("fBloodSplatterFadeStart", .25);
            blood.Advance(0, true);
            Require(presentations.All(presentation => Math.Abs(presentation.Fade - 1f / 3) < .00001), "Fade did not read the live shared setting.");
            blood.Advance(.5, true);
            Require(blood.ActiveDrops == 0 && presentations.All(presentation => presentation.Releases == 1), "Expiration leaked active capacity or released a group twice.");
            fail = true; Reject(() => blood.Trigger(caller, 1)); fail = false;
            badIdentity = true; Reject(() => blood.Trigger(caller, 1)); badIdentity = false;
            Require(ReferenceEquals(previous, blood.LastRequest) && blood.ActiveDrops == 0 && presentations[^1].Releases == 1,
                "Failed preparation committed a request or leaked its prepared presentation.");
            blood.Trigger(caller, FalloutScreenBlood.Count(-1));
            Require(blood.LastRequest!.RandomBefore == previous!.RandomAfter && blood.LastRequest.Duration == 4 && blood.ActiveDrops == 3,
                "Preparation consumed random state, duration mutation was lost, or signed count payload was restricted.");
            blood.Advance(4, true);
            records.NumericSettings.Set("iBloodSplatterMaxCount", 20000);
            Reject(() => blood.Trigger(caller, uint.MaxValue)); records.NumericSettings.Set("iBloodSplatterMaxCount", 3);
            Reject(() => FalloutScreenBlood.Count(.5)); Reject(() => FalloutScreenBlood.Count(double.NaN));

            var quests = new FalloutQuestState(records);
            var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("Blood command invented an unrelated effect.")));
            var quest = records.GetEffective(caller); var script = records.GetEffective(Key(0x601));
            void Execute(string body) => executor.ExecuteProgram(quest, script, FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
            Execute("tsb 1\nset done to 7");
            Require(quests.Variable(caller, 1) == 7 && blood.LastRequest!.Caller == caller, "Result/reference alias execution bypassed the shared effect.");
            fail = true; Reject(() => Execute("TriggerScreenBlood 1\nset done to 99")); fail = false;
            Require(quests.Variable(caller, 1) == 7, "A failed blood command executed its source suffix.");
            quests.SetVariable(caller, 1, 0);
            var fallback = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new(), defaultProcessingDelay: 0, references: world);
            fallback.Advance(0);
            Require(ReferenceEquals(fallback.ScreenBlood, blood) && quests.Variable(caller, 1) == 1 &&
                fallback.Capture().Instances.Single().Error is null, "Fallback quest execution lost its shared effect owner.");
            var saved = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(fallback.Capture()))!;
            using var coldWorld = new FalloutReferenceWorld(records); var coldQuests = new FalloutQuestState(records); coldQuests.Restore(quests.Capture());
            var cold = new FalloutQuestScripts(records, coldQuests, new HashSet<FalloutFormKey>(), new(), defaultProcessingDelay: 0, references: coldWorld);
            cold.Restore(saved); cold.Advance(0);
            Require(cold.ScreenBlood.ActiveDrops == 0 && cold.ScreenBlood.LastRequest is null && cold.Capture().Instances.Single().Error is null,
                "Cold restoration replayed transient blood from an executed prefix.");
            Require(blood.Unbound.Contains("synthetic-partial-lighting"), "Partial effect presentation hid its divergence.");
            binding.Dispose();
            Require(blood.ActiveDrops == 0 && presentations.All(presentation => presentation.Releases == 1), "Effect retirement leaked or duplicated presentation release.");
            Reject(() => blood.Trigger(caller, 1));
            using var disabled = blood.Bind(false, _ => throw new InvalidDataException("Disabled blood prepared a texture."));
            blood.Trigger(caller, 1); Require(blood.ActiveDrops == 0 && blood.LastDisposition == "source-disabled", "Source-disabled blood was presented.");
            Console.WriteLine("OPENNV_SCREEN_BLOOD_CONTRACT_PASS winning=true activeCapacity=true atlas=true liveFade=true capturedDuration=true menuClock=true randomAtomic=true prefixFailure=true fallback=true coldNoReplay=true retirement=true divergenceVisible=true parity=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }
    private static FalloutFormKey Key(uint id) => new("Blood.esm", id);
    private static byte[] Setting(uint id, string name, object value) => Record("GMST", id, Field("EDID", Text(name)),
        Field("DATA", value switch { float number => BitConverter.GetBytes(number), int number => BitConverter.GetBytes(number), string text => Text(text), _ => throw new InvalidDataException() }));
    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text + "\0");
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)data.Length); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Join(params byte[][] data) => data.SelectMany(part => part).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid screen-blood request was accepted.");
    }
}
