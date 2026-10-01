using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class NoActivationSoundContracts
{
    internal static void Run()
    {
        Associations();
        var directory = Path.Combine(Path.GetTempPath(), "opennv-activation-sounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string program = "short done\nref choice\nbegin GameMode\nif done == 0\nlet choice := OverrideSound\n" +
                "SetNoActivationSound choice\nset done to 1\nendif\nend";
            var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2); header[16] = 1;
            File.WriteAllBytes(Path.Combine(directory, "Feedback.esm"), Join(Record("TES4", 0),
                Sound(0x100, "DefaultSound", "fx/default.wav"), Sound(0x101, "OverrideSound", "fx/base.wav"),
                Record("SCPT", 0x601, Field("SCHR", header), Local(1, "done"), Local(2, "choice"),
                    Field("SCRO", BitConverter.GetBytes(0x101u)), Field("SCTX", Text(program))),
                Record("QUST", 0x600, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x601u)))));
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Record("TES4", 0, Field("MAST", Text("Feedback.esm"))),
                Sound(0x101, "OverrideSound", "fx/winner.wav")));
            using var records = FalloutPluginStack.Load(directory, ["Feedback.esm", "Patch.esp"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new(),
                defaultProcessingDelay: 0, references: world);
            var feedback = world.NoActivationSound;
            var resolves = 0;
            using var defaultBinding = feedback.BindDefault(() => { resolves++; return "DefaultSound"; });
            var releases = 0; var starts = 0; var fail = false;
            using var audio = world.Sounds.Bind(source => [source.LogicalPath], request => fail ? throw new InvalidDataException("Synthetic decode failure.") :
                new(new(request.Selection.Path!, "synthetic-source", new string('a', 64), 1), () => starts++, _ => { }, () => releases++), true);
            scripts.Advance(0);
            Require(ReferenceEquals(scripts.NoActivationSound, feedback) && feedback.Capture()?.Sound == Key(0x101) &&
                quests.Variable(Key(0x600), 1) == 1 && world.Sounds.LastRequest is null && resolves == 0,
                "Fallback typed SOUN selection bypassed the shared owner or played audio while setting it.");
            var saved = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(scripts.Capture()))!;
            feedback.RejectActivation(Key(0x14)); var voice = world.Sounds.LastRequest!.Id;
            Require(starts == 1 && world.Sounds.LastRequest.Source.LogicalPath == "sound\\fx\\winner.wav", "Feedback lost the winning source declaration.");
            feedback.RejectActivation(Key(0x14));
            Require(world.Sounds.LastRequest.Id == voice && starts == 1, "Repeated input restarted an active feedback voice.");
            world.Sounds.Complete(voice); feedback.RejectActivation(Key(0x14));
            Require(starts == 2 && releases == 1, "Completed feedback could not play again.");

            var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ =>
                throw new InvalidDataException("Feedback command invented a presentation effect.")));
            void Execute(string body) => executor.ExecuteProgram(records.GetEffective(Key(0x600)), records.GetEffective(Key(0x601)),
                FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
            Execute("ClearNoActivationSound\nset done to 7");
            Require(feedback.Capture() is null && resolves == 0 && quests.Variable(Key(0x600), 1) == 7 && world.Sounds.ActiveVoices == 1,
                "Clearing resolved a default too early, canceled an existing voice or lost the script suffix.");
            feedback.RejectActivation(Key(0x14));
            Require(feedback.Capture()?.Sound == Key(0x100) && resolves == 1 && world.Sounds.LastRequest!.Source.EditorId == "DefaultSound",
                "ClearNoActivationSound did not lazily return to the owned default.");
            Execute("SetNoActivationSound OverrideSound\nset done to 8");
            var before = feedback.Capture();
            Reject(() => Execute("SetNoActivationSound done\nset done to 99"));
            Reject(() => Execute("ClearNoActivationSound 1\nset done to 99"));
            Require(feedback.Capture() == before && quests.Variable(Key(0x600), 1) == 8, "Invalid feedback commands mutated the selection or ran their suffix.");

            using var coldWorld = new FalloutReferenceWorld(records);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(quests.Capture()); coldQuests.SetVariable(Key(0x600), 1, 1);
            var cold = new FalloutQuestScripts(records, coldQuests, new HashSet<FalloutFormKey>(), new(), defaultProcessingDelay: 0, references: coldWorld);
            cold.Restore(saved); cold.Advance(0);
            Require(cold.NoActivationSound.Capture() == saved.Session!.NoActivationSound && cold.Sounds.LastRequest is null &&
                cold.Capture().Instances.Single().Error is null, "Cold restoration dropped the selection or replayed an executed prefix.");
            var state = saved.Session!.NoActivationSound!;
            Reject(() => cold.Session.Restore(saved.Session with { NoActivationSound = state with { Sha256 = new string('0', 64) } }));
            Reject(() => cold.Session.Restore(saved.Session with { NoActivationSound = state with { Sound = Key(0x600) } }));
            Reject(() => cold.Session.Restore(saved.Session with { NoActivationSound = state with { Sha256 = "bad" } }));
            Require(cold.NoActivationSound.Capture() == state, "Invalid cold source identity changed an already admitted selection.");
            cold.Session.Restore(saved.Session with { NoActivationSound = null });
            Require(cold.NoActivationSound.Capture() is null, "Legacy cold state inherited a stale override.");
            Reject(() => cold.NoActivationSound.RejectActivation(Key(0x14)));
            while (world.Sounds.LastRequest is { } last && world.Sounds.IsActive(last.Id)) world.Sounds.Complete(last.Id);
            fail = true; var lastId = world.Sounds.LastRequest!.Id;
            Reject(() => feedback.RejectActivation(Key(0x14)));
            Require(world.Sounds.LastRequest.Id == lastId && feedback.Capture() == before && feedback.LastError is not null,
                "Media failure committed a request, reset the selected sound or hid divergence.");
            Console.WriteLine("OPENNV_NO_ACTIVATION_SOUND_CONTRACT_PASS sourceAssociation=true shared=true typed=true winning=true prefix=true activeSuppression=true cold=true sourceHash=true noColdReplay=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Associations()
    {
        const uint codeBase = 0x600000, slot = 0x800100, handler = 0x800200, name = 0x700100;
        var code = new byte[220];
        new byte[] { 0x55, 0x8b, 0xec, 0xe8, 0, 0, 0, 0, 0xb0, 1, 0x5d, 0xc3 }.CopyTo(code, 0);
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(4), 32 - 8);
        new byte[] { 0x55, 0x8b, 0xec, 0xc7, 0x05 }.CopyTo(code, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(37), slot); code[45] = 0x5d; code[46] = 0xc3;
        var getter = code.AsSpan(64, 40);
        new byte[] { 0x55, 0x8b, 0xec, 0x83, 0x3d }.CopyTo(getter);
        BinaryPrimitives.WriteUInt32LittleEndian(getter[5..], slot); getter[10] = 0x75; getter[11] = 0x15; getter[12] = 0x68;
        BinaryPrimitives.WriteUInt32LittleEndian(getter[13..], name); getter[17] = 0x8b; getter[18] = 0x0d;
        BinaryPrimitives.WriteUInt32LittleEndian(getter[19..], handler); getter[23] = 0xe8;
        BinaryPrimitives.WriteInt32LittleEndian(getter[24..], 112 - (64 + 28)); getter[28] = 0xa3; getter[33] = 0xa1;
        BinaryPrimitives.WriteUInt32LittleEndian(getter[29..], slot); BinaryPrimitives.WriteUInt32LittleEndian(getter[34..], slot);
        getter[38] = 0x5d; getter[39] = 0xc3; new byte[] { 0x55, 0x8b, 0xec }.CopyTo(code, 112);
        string? Literal(uint address) => address == name ? "SourceDeclaredDefault" : null;
        bool Writable(uint address) => address is slot or handler;
        string Read(byte[] bytes) => FalloutExecutableStringTable.ReadNoActivationSoundAssociation(bytes, codeBase, 0, Literal, Writable);
        Require(Read(code) == "SourceDeclaredDefault", "Source association did not retain its declared editor ID.");
        Reject(() => Read(code[..11])); Reject(() => Read(code[..103]));
        Reject(() => FalloutExecutableStringTable.ReadNoActivationSoundAssociation(code, codeBase, 0, _ => null, Writable));
        Reject(() => FalloutExecutableStringTable.ReadNoActivationSoundAssociation(code, codeBase, 0, Literal, _ => false));
        var foreign = code.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(foreign.AsSpan(64 + 29), slot + 4); Reject(() => Read(foreign));
        var outOfBounds = code.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(outOfBounds.AsSpan(4), int.MaxValue); Reject(() => Read(outOfBounds));
        var duplicate = code.ToArray(); code.AsSpan(64, 40).CopyTo(duplicate.AsSpan(160));
        BinaryPrimitives.WriteInt32LittleEndian(duplicate.AsSpan(184), 112 - (160 + 28)); Reject(() => Read(duplicate));
    }
    private static FalloutFormKey Key(uint id) => new("Feedback.esm", id);
    private static byte[] Sound(uint id, string name, string path)
    {
        var data = new byte[36]; BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22), 100);
        return Record("SOUN", id, Field("EDID", Text(name)), Field("FNAM", Text(path)), Field("SNDD", data));
    }
    private static byte[] Local(uint index, string name)
    {
        var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index);
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported activation-sound behavior was accepted.");
    }
}
