using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class LoadingScreenContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-loading-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[20]; header[16] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1);
            var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, 1);
            File.WriteAllBytes(Path.Combine(directory, "Screens.esm"), Join(Header(), Record("WRLD", 0x200),
                Record("CELL", 0x300, Field("DATA", [0])), Record("LSCT", 0x500, Field("DATA", TipData())),
                Screen(0x100, 0x400), Screen(0x101), Screen(0x102, location: Location(0x300)),
                Screen(0x103, location: Location(0x200)), Screen(0x104, location: Location(0, 0x200, -3, 4)),
                Record("QUST", 0x601, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x600u))),
                Record("SCPT", 0x600, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("sample")),
                    Field("SCTX", Text("short sample\nbegin GameMode\nSetLocationSpecificLoadScreensOnly 1\nset sample to GetLocationSpecificLoadScreensOnly\nend"))),
                Record("QUST", 0x630, Field("DATA", [0, 0]), Field("INDX", new byte[2]), Field("QSDT", [0]),
                    Field("SCTX", Text("SetLocationSpecificLoadScreensOnly 1")))));
            var cell = new FalloutCellDefinition(Key(0x300), "Fixture", 0, (-3, 4), Key(0x200), null);
            using (var basis = FalloutPluginStack.Load(directory, ["Screens.esm"]))
                Require(Ids(FalloutLoadingScreenCatalog.InGame(basis, cell, true)).SequenceEqual([0x102u, 0x103u, 0x104u]),
                    "Direct CELL, WRLD or indirect grid eligibility was lost.");
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Screens.esm"),
                Screen(0x102, location: Location(0x300), texture: "winning.dds"), Record("LSCR", 0x103, flags: 0x20)));
            using var records = FalloutPluginStack.Load(directory, ["Screens.esm", "Patch.esp"]);
            var specific = FalloutLoadingScreenCatalog.InGame(records, cell, true);
            Require(Ids(specific).SequenceEqual([0x102u, 0x104u]) &&
                Ids(FalloutLoadingScreenCatalog.InGame(records, cell, false)).SequenceEqual([0x101u, 0x102u, 0x104u]) &&
                Ids(FalloutLoadingScreenCatalog.MainMenu(records)).SequenceEqual([0x100u]) &&
                specific.Single(screen => screen.Identity == Key(0x102)).TexturePath == "textures\\winning.dds",
                "Generic policy, winning replacement, deleted record or main-menu separation was lost.");
            Require(FalloutLoadingScreenCatalog.InGame(records, cell with { FormKey = Key(0x301), Coordinates = (5, 6) }, true).Count == 0,
                "A nonmatching grid or CELL was admitted.");
            var tip = FalloutLoadingScreenType.Tip(records.GetEffective(Key(0x500)));
            Require(tip is { X: 20, Y: 30, Width: 400, Height: 200, Font: 7, Red: 11, Green: 22, Blue: 33, Alignment: 2 } &&
                specific.All(screen => screen.Type == Key(0x500) && screen.Description == "Source tip café"),
                "LSCT layout, font indexing or winning text/type identity was lost.");
            ScriptPolicy(records);
            File.WriteAllBytes(Path.Combine(directory, "Bad.esp"), Join(Header("Screens.esm"), Screen(0x102, location: new byte[11])));
            using (var bad = FalloutPluginStack.Load(directory, ["Screens.esm", "Bad.esp"]))
                Reject(() => FalloutLoadingScreenCatalog.InGame(bad, cell, true));
            File.WriteAllBytes(Path.Combine(directory, "Type.esp"), Join(Header("Screens.esm"), Record("LSCT", 0x500, Field("DATA", TipData(1)))));
            using (var bad = FalloutPluginStack.Load(directory, ["Screens.esm", "Type.esp"]))
                Reject(() => FalloutLoadingScreenType.Tip(bad.GetEffective(Key(0x500))));
            Console.WriteLine("OPENNV_LOADING_SCREEN_CONTRACT_PASS winning=true deleted=true directCell=true directWorld=true grid=true policy=true sharedScripts=true bootstrap=true cold=true legacyDefault=true invalidAtomic=true unsupportedVisible=true parity=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static void ScriptPolicy(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0);
        scripts.Advance(0);
        Require(scripts.Session.LocationSpecificLoadScreensOnly && quests.Variable(Key(0x601), 1) == 1 &&
            scripts.Capture().Instances.Single().Error is null, "Fallback quest did not execute the policy and query.");
        var snapshot = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(scripts.Capture()))!;
        var coldQuests = new FalloutQuestState(records); coldQuests.Restore(quests.Capture());
        var cold = new FalloutQuestScripts(records, coldQuests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0);
        cold.Restore(snapshot);
        Require(cold.Session.LocationSpecificLoadScreensOnly, "Cold script session lost loading policy.");
        var legacy = JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>("{\"Hardcore\":false,\"AutoDisplayObjectives\":true,\"Achievements\":[]}")!;
        Require(!legacy.LocationSpecificLoadScreensOnly, "Legacy session acquired a new loading restriction.");
        var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            if (effect.Kind != FalloutReferenceEffectKind.LoadingScreenPolicy) throw new InvalidDataException("Unexpected effect.");
            scripts.Session.LocationSpecificLoadScreensOnly = effect.Enable;
        }, LocationSpecificLoadScreensOnly: () => scripts.Session.LocationSpecificLoadScreensOnly));
        void Run(string body) => executor.ExecuteProgram(records.GetEffective(Key(0x601)), records.GetEffective(Key(0x600)),
            FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
        Run("SetLocationSpecificLoadScreensOnly 0\nset sample to GetLocationSpecificLoadScreensOnly");
        Require(!scripts.Session.LocationSpecificLoadScreensOnly && quests.Variable(Key(0x601), 1) == 0, "Reference result did not share the session.");
        Reject(() => Run("SetLocationSpecificLoadScreensOnly 2\nset sample to 99"));
        Require(!scripts.Session.LocationSpecificLoadScreensOnly && quests.Variable(Key(0x601), 1) == 0,
            "Rejected policy mutated shared state or executed its suffix.");
        var bootstrap = new FalloutNewGameBootstrap(records,
            FalloutInstallationSettings.ReadLayers([], [new("General", "SCharGenQuest", "00000630")]), quests, scripts, world,
            (_, _, _, _) => throw new InvalidDataException("Unexpected startup command."),
            _ => throw new InvalidDataException("Unexpected startup effect."), () => true);
        bootstrap.Start();
        Require(scripts.Session.LocationSpecificLoadScreensOnly, "Pre-world source result did not retain loading policy.");
    }

    private static uint[] Ids(IReadOnlyList<FalloutLoadingScreen> screens) => screens.Select(screen => screen.Identity.ObjectId).Order().ToArray();
    private static FalloutFormKey Key(uint id) => new("Screens.esm", id);
    private static byte[] TipData(uint kind = 3)
    {
        var bytes = new byte[88];
        foreach (var (offset, value) in new[] { (0, kind), (4, 20u), (8, 30u), (12, 400u), (16, 200u), (24, 6u), (40, 2u) })
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        foreach (var (offset, value) in new[] { (28, 11f), (32, 22f), (36, 33f) }) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
        return bytes;
    }
    private static byte[] Location(uint direct, uint world = 0, short x = 0, short y = 0)
    {
        var bytes = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, direct);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), world);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(8), y); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(10), x); return bytes;
    }
    private static byte[] Screen(uint id, uint flags = 0, byte[]? location = null, string texture = "source.dds") =>
        Record("LSCR", id, flags, Field("ICON", Text(texture)), Field("DESC", Encoding.Latin1.GetBytes("Source tip café\0")), Field("WMI1", BitConverter.GetBytes(0x500u)),
            location is null ? [] : Field("LNAM", location));
    private static byte[] Header(string? master = null) => Record("TES4", 0, 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Record(string name, uint id, params byte[][] fields) => Record(name, id, 0, fields);
    private static byte[] Record(string name, uint id, uint flags, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), flags); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported loading behavior was accepted.");
    }
}
