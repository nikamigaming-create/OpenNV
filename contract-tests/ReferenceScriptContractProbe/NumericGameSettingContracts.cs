using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class NumericGameSettingContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-numeric-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2); header[16] = 1;
            const string source = "float sample\nstring_var settingName\nbegin GameMode\nlet settingName := \"fSample\"\n" +
                "SetNumericGameSetting settingName 6.25\nset sample to GetNumericGameSetting settingName\nend";
            File.WriteAllBytes(Path.Combine(directory, "Settings.esm"), Join(Header(),
                Setting(0x100, "fSample", BitConverter.GetBytes(1f)), Setting(0x101, "iSample", BitConverter.GetBytes(-3)),
                Setting(0x102, "bSample", BitConverter.GetBytes(0)), Setting(0x103, "uSample", BitConverter.GetBytes(uint.MaxValue)),
                Setting(0x104, "sSample", Text("Text")), Setting(0x105, "fGuarded", BitConverter.GetBytes(1f)),
                Setting(0x110, "fNamed", BitConverter.GetBytes(1f)),
                Record("QUST", 0x600, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x601u))),
                Record("SCPT", 0x601, Field("SCHR", header), Local(1, "sample"), Local(2, "settingName"), Field("SCTX", Text(source)))));
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Settings.esm"),
                Setting(0x100, "fSample", BitConverter.GetBytes(4f)), Setting(0x01000010, "fNamed", BitConverter.GetBytes(2f))));
            File.WriteAllBytes(Path.Combine(directory, "Final.esp"), Join(Header("Settings.esm", "Patch.esp"),
                Setting(0x110, "FNAMED", BitConverter.GetBytes(3f)),
                Setting(0x02000020, "iOrdered", BitConverter.GetBytes(4)), Setting(0x02000010, "iOrdered", BitConverter.GetBytes(5))));
            var names = new[] { "Settings.esm", "Patch.esp", "Final.esp" };
            using var records = FalloutPluginStack.Load(directory, names);
            var settings = records.NumericSettings;
            Require(FalloutGameSettingFloats.Read(records, "FSAMPLE") == 4 && settings.Get("iSample") == -3 &&
                settings.Get("uSample") == uint.MaxValue, "Winning setting identity, signed integer or unsigned payload was lost.");
            Require(settings.Get("sSample") == -1 && !settings.Set("sSample", 1) && settings.Get("fMissing") == -1 &&
                !settings.Set("fMissing", 1) && settings.Revision == 0, "Missing/string settings invented a numeric mutation.");
            Require(settings.Get("fNamed") == 3 && settings.Get("iOrdered") == 5,
                "Named aliases used FormID/first registration order instead of winning plugin and source declaration order.");
            Require(settings.Set("fnamed", 7) && settings.Get("FNAMED") == 7 &&
                FalloutGameSettingFloats.Read(records, "fNamed") == 7,
                "Setting aliases or case variants did not share one live typed value.");
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("Setting command invented a presentation effect.")));
            var quest = records.GetEffective(Key(0x600)); var script = records.GetEffective(Key(0x601));
            void Run(string body) => executor.ExecuteProgram(quest, script, FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
            Run("SetNumericGameSetting fSample -2.5\nset sample to GetGS fSample");
            Require(quests.Variable(quest.FormKey, 1) == -2.5 && FalloutGameSettingFloats.Read(records, "fSample") == -2.5,
                "Source bare-name command and classic query used separate setting copies.");
            Run("set sample to SetNumericGameSetting \"iSample\" -12.9");
            Require(quests.Variable(quest.FormKey, 1) == 1 && settings.Get("iSample") == -12 &&
                FalloutGameSettingIntegers.Read(records, "iSample") == unchecked((uint)-12), "Setter return, Float32 truncation or signed/raw readers diverged.");
            Require(settings.Set("bSample", -7) && settings.Get("bSample") == 1 && settings.Set("uSample", 42.9) &&
                settings.Get("uSample") == 42, "Boolean or unsigned numeric conversion diverged.");
            Run("set sample to GetNumericGameSetting \"fMissing\"");
            Require(quests.Variable(quest.FormKey, 1) == -1, "Missing numeric query lost its documented result.");
            var revision = settings.Revision;
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.MaxValue }) Reject(() => settings.Set("fSample", invalid));
            Reject(() => settings.Set("iSample", 2147483648)); Reject(() => settings.Set("uSample", -1));
            Require(settings.Revision == revision && settings.Get("fSample") == -2.5, "Rejected storage conversion partially changed session state.");
            Require(FalloutGameSettingFloats.ReadRetained(records, "fGuarded", "DerivedActorState") == 1 && settings.Set("fGuarded", 1),
                "An unchanged write invalidated retained derived state.");
            Reject(() => Run("SetNumericGameSetting fGuarded 2\nset sample to 99"));
            Require(settings.Get("fGuarded") == 1 && quests.Variable(quest.FormKey, 1) == -1,
                "Unbound retained consumer accepted a write or executed the script suffix.");
            var fallback = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
                defaultProcessingDelay: 0, references: world);
            fallback.Advance(0);
            Require(fallback.Capture().Instances.Single().Error is null && settings.Get("fSample") == 6.25 &&
                quests.Variable(quest.FormKey, 1) == 6.25,
                $"Fallback string-local setting mismatch: error={fallback.Capture().Instances.Single().Error}; value={settings.Get("fSample")}; sample={quests.Variable(quest.FormKey, 1)}.");
            var snapshot = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(fallback.Capture()))!;
            var warm = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0);
            warm.Restore(snapshot);
            Require(settings.Get("fSample") == 6.25, "Warm script restoration reset session settings.");
            using var coldRecords = FalloutPluginStack.Load(directory, names);
            var coldQuests = new FalloutQuestState(coldRecords); coldQuests.Restore(quests.Capture());
            var cold = new FalloutQuestScripts(coldRecords, coldQuests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0);
            cold.Restore(snapshot);
            Require(coldRecords.NumericSettings.Get("fSample") == 4 && coldRecords.NumericSettings.Get("iSample") == -3 &&
                coldRecords.NumericSettings.Get("fNamed") == 3 && coldRecords.NumericSettings.Get("iOrdered") == 5,
                "Cold script restoration baked numeric mutations into a new loaded stack.");
            Require(BitConverter.ToSingle(records.GetEffective(Key(0x100)).ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span) == 4,
                "Script setter modified winning source bytes.");
            Console.WriteLine("OPENNV_NUMERIC_GAME_SETTING_CONTRACT_PASS winning=true namedAliases=true pluginAndDeclarationOrder=true typed=true sourceCommands=true returns=true fallbackStrings=true warm=true coldReset=true invalidAtomic=true retainedConsumerGapVisible=true sourceReadonly=true parity=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static FalloutFormKey Key(uint id) => new("Settings.esm", id);
    private static byte[] Setting(uint id, string name, byte[] data) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", data));
    private static byte[] Header(params string[] masters) => Record("TES4", 0, Field("HEDR", new byte[12]),
        Join(masters.Select(master => Join(Field("MAST", Text(master)), Field("DATA", new byte[8]))).ToArray()));
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
        throw new InvalidOperationException("Invalid or unsupported numeric setting mutation was accepted.");
    }
}
