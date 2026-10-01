using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class InputControlContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-input-controls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var profile = Path.Combine(directory, "controls.json");
            var reads = 0;
            string Source(string name)
            {
                reads++;
                return name switch
                {
                    "Forward" => "0011FF13", "Back" => "001FFF14", "Activate" => "0012FF0A",
                    "Use" => "00FF0011", "Block" => "00380110", _ => "00FFFFFF",
                };
            }
            var controls = new FalloutInputControls(Source, profile);
            Require(controls.Get(0) == 17 && controls.Get(1) == 31 && controls.Get(4) == -1 &&
                controls.Get(4, 1) == 256 && controls.Get(16) == -1 && controls.Get(28) == -1 && reads == 28,
                "Source control bytes, unassigned sentinel or source table count differ.");
            controls.Set(0, 31);
            Require(controls.Get(0) == 31 && controls.Get(1) == 17 && reads == 28 && !File.Exists(profile),
                "Duplicate keyboard binding did not swap, or a live write escaped into persistent settings early.");
            controls.Set(4, 257, 1);
            Require(controls.Get(4, 1) == 257 && controls.Get(6, 1) == 256 && controls.Get(6) == 56,
                "Mouse swap changed the keyboard lane or its DirectInput offset.");
            var version = controls.Revision;
            void RejectNativeKey() { if (controls.Get(0) == 250) throw new NotSupportedException("Unbound physical key."); }
            controls.Changed += RejectNativeKey;
            Reject(() => controls.Set(0, 250));
            controls.Changed -= RejectNativeKey;
            Require(controls.Revision == version && controls.Get(0) == 31 && controls.Get(1) == 17,
                "Rejected native adaptation partially changed the control owner.");
            foreach (var action in new Action[] { () => controls.Get(0, 2), () => controls.Get(0, 3),
                () => controls.Set(0, 512), () => controls.Set(0, 17, 4), () => FalloutInputControls.Index(-1),
                () => FalloutInputControls.Index(.5), () => FalloutInputControls.Index(double.NaN) }) Reject(action);
            Require(controls.Get(0) == 31 && controls.Revision == version, "Invalid input mutated control settings.");

            var header = new byte[20]; header[16] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2);
            var bytes = Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("QUST", 0x10, Field("EDID", Text("ControlQuest")), Field("DATA", [1, 0, 0, 0, 0, 0, 0, 0]),
                    Field("SCRI", BitConverter.GetBytes(0x11u))),
                Record("SCPT", 0x11, Field("SCHR", header), Local(1, "seen"), Local(2, "once"),
                    Field("SCTX", Text("short seen\nshort once\nbegin GameMode\nif once == 0\nSetControl 16 45\n" +
                        "SetAltControl 4 258\nset seen to GetControl 16 + GetAltControl 4\nset once to 1\nendif\nend"))));
            var path = Path.Combine(directory, "Controls.esm"); File.WriteAllBytes(path, bytes);
            using var records = FalloutPluginStack.Load(directory, ["Controls.esm"]);
            using var world = new FalloutReferenceWorld(records, controls: controls);
            var quests = new FalloutQuestState(records);
            FalloutFormKey Key(uint id) => new("Controls.esm", id);
            var executor = new FalloutReferenceScripts(records, world, quests,
                new((_, _) => false, _ => throw new InvalidOperationException("Input commands invented presentation effects.")));
            var script = records.GetEffective(Key(0x11)); var quest = records.GetEffective(Key(0x10));
            void Run(string body) => executor.ExecuteProgram(quest, script,
                FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
            Run("SetControl (16) (44)\nSetControl 4 256 1\nset seen to GetControl 16 + GetControl 4 1");
            Require(controls.Get(16) == 44 && controls.Get(4, 1) == 256 && quests.Variable(Key(0x10), 1) == 300,
                "Reference control commands or optional device arguments used a private copy.");
            Reject(() => Run("SetControl 16.5 18"));
            Reject(() => Run("SetControl 16 18 2"));
            var storage = new FalloutScriptStorage(new FalloutScriptIniStore(_ => null, directory), controls: controls);
            var fallback = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
                defaultProcessingDelay: .01f, storage: storage);
            fallback.Advance(1);
            Require(fallback.Capture().Instances.Single().Error is null && quests.Variable(Key(0x10), 1) == 47 &&
                controls.Get(16) == 45 && controls.Get(4, 1) == 258,
                "Fallback quest controls lost the shared owner, mouse byte query or authored remap.");
            controls.Flush();
            var cold = new FalloutInputControls(_ => throw new InvalidOperationException("Cold profile reread source defaults."), profile);
            Require(cold.Get(16) == 45 && cold.Get(4, 1) == 258 && File.ReadAllBytes(path).SequenceEqual(bytes),
                "Control settings did not survive a cold owner or wrote the source graph.");
            File.WriteAllText(profile, "{\"Schema\":\"opennv-input-controls/v1\",\"NewVegas\":true,\"Bindings\":[]}");
            Reject(() => new FalloutInputControls(Source, profile).Get(0));
            Reject(() => new FalloutInputControls(_ => "0011FF1G").Get(0));
            Console.WriteLine("OPENNV_INPUT_CONTROL_CONTRACT_PASS source=true swap=true keyboard=true mouse=true optionalTypes=true reference=true fallbackQuest=true invalidAtomic=true cold=true sourceReadonly=true devicesUnbound=true");
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static byte[] Local(uint index, string name)
    {
        var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index);
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or JsonException) { return; }
        throw new InvalidOperationException("Unsupported control state was accepted.");
    }
}
