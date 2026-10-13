using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class NativePluginDiscoveryContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-native-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var fo3 = Install("Fo3", "Fallout3");
            var fnv = Install("Fnv", "FalloutNV");
            var patch = Path.Combine(directory, "Patch");
            Directory.CreateDirectory(patch);
            var overridden = Write(Path.Combine(fo3.Data, "FOSE", "Plugins", "B_Plugin.dll"), ["FOSEPlugin_Query"]);
            var winner = Write(Path.Combine(patch, "FoSe", "Plugins", "B_Plugin.DLL"), ["FOSEPlugin_Query", "FOSEPlugin_Load"]);
            Write(Path.Combine(fo3.Data, "FOSE", "Plugins", "A_Helper.dll"), []);
            Write(Path.Combine(fo3.Data, "FOSE", "Plugins", "C_Broken.dll"), ["FOSEPlugin_Query"]);
            Write(Path.Combine(fo3.Data, "FOSE", "Plugins", "Nested", "D_Plugin.dll"), ["FOSEPlugin_Query", "FOSEPlugin_Load"]);
            Write(Path.Combine(fo3.Data, "NVSE", "Plugins", "WrongEdition.dll"), ["NVSEPlugin_Query", "NVSEPlugin_Load"]);
            Write(Path.Combine(fnv.Data, "FOSE", "Plugins", "WrongEdition.dll"), ["FOSEPlugin_Query", "FOSEPlugin_Load"]);
            Write(Path.Combine(fnv.Data, "NVSE", "Plugins", "A_Helper.dll"), []);
            Write(Path.Combine(fnv.Data, "NVSE", "Plugins", "B_Foreign.dll"), ["FOSEPlugin_Query", "FOSEPlugin_Load"]);
            var before = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);

            using (var source = Open(fo3, RuntimeLiveContentSource.Fallout3Game, [patch]))
            {
                var paths = source.NativePluginResourcePaths().ToArray();
                Require(paths.Length == 4 && paths.All(path => path.StartsWith("fose\\plugins\\", StringComparison.Ordinal)),
                    "standalone FO3 selects its complete FOSE namespace");
                var declaration = source.EnsureNativePluginDeclarations();
                Require(ReferenceEquals(declaration.Source, source) && ReferenceEquals(source.NativePluginDeclarations, declaration) &&
                    declaration.Modules.Count == 0 && declaration.NvsePath.Length == 0 && declaration.NvseSha256.Length == 0,
                    "FOSE source discovery creates no NVSE host or executable module admission");
                var selected = declaration.Inventory.Single(row => Path.GetFileName(row.PhysicalPath).Equals("B_Plugin.dll", StringComparison.OrdinalIgnoreCase));
                Require(selected.PhysicalPath == winner && selected.PhysicalPath != overridden && selected.Sha256 == before[winner] &&
                    selected.Disposition == FalloutNativeModuleDisposition.SourceFailure && selected.QueryHandle is null &&
                    selected.Failure!.Contains("standalone FOSE host ABI owner", StringComparison.Ordinal),
                    "genuine winning FOSE Query/Load retains its exact unsupported host and source identity");
                Require(declaration.Inventory.Select(row => row.Disposition).SequenceEqual([
                    FalloutNativeModuleDisposition.LoaderNotPlugin, FalloutNativeModuleDisposition.SourceFailure,
                    FalloutNativeModuleDisposition.SourceFailure, FalloutNativeModuleDisposition.AvailableNested]) &&
                    declaration.Inventory.All(row => row.QueryHandle is null),
                    "helpers, malformed entries and nested availability remain separate without fabricated FOSE handles");
                Require(declaration.Inventory.Single(row => row.LogicalPath.EndsWith("c_broken.dll", StringComparison.Ordinal)).Failure!
                    .Contains("genuine direct executable Query/Load exports", StringComparison.Ordinal),
                    "a missing FOSE Load export remains an explicit source failure");

                using var records = FalloutPluginStack.Load(source.PluginSources);
                using var world = new FalloutReferenceWorld(records);
                var quests = new FalloutQuestState(records);
                var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
                    defaultProcessingDelay: 1, references: world);
                using var campaign = new FalloutNativePluginCampaign(records, quests, scripts, "unlaunched-companion", directory, declaration);
                Require(campaign.Modules.Count == 2 && campaign.Modules.All(row => row.Generation is null && row.ProcessId is null &&
                    row.Query is null && row.Load is null && row.Failure is not null), "FOSE failures remain authoritative without native execution");
                Refuse(() => campaign.RequireIdleForSave(), "Selected native module state is incomplete");
            }

            using (var source = Open(fnv, RuntimeLiveContentSource.FalloutNewVegasGame, []))
            {
                var declaration = source.EnsureNativePluginDeclarations();
                Require(declaration.Modules.Count == 0 && declaration.Inventory.Count == 2 &&
                    declaration.Inventory.All(row => row.Disposition == FalloutNativeModuleDisposition.LoaderNotPlugin) &&
                    declaration.Inventory.Select(row => row.QueryHandle).SequenceEqual(new uint?[] { 1, 2 }) &&
                    declaration.Inventory.All(row => row.LogicalPath.StartsWith("nvse\\plugins\\", StringComparison.Ordinal)),
                    "New Vegas preserves its NVSE namespace and original top-level Query handle gaps");
            }
            using (var source = Open(fo3, RuntimeLiveContentSource.Fallout3Game, [patch], enabled: false))
            {
                var declaration = source.EnsureNativePluginDeclarations();
                Require(!source.NativePluginResourcePaths().Any() && declaration.Inventory.Count == 0 && declaration.Modules.Count == 0 &&
                    declaration.LoadOrderOwner == "explicit-user-native-selection:none", "explicit native selection none stays distinct");
            }
            Require(before.All(row => File.Exists(row.Key) && Hash(row.Key) == row.Value), "source discovery preserves all declaring bytes");
            Console.WriteLine("OPENNV_NATIVE_PLUGIN_DISCOVERY_PASS fo3=FOSE fnv=NVSE winningSource=true unsupportedHost=visible " +
                "malformed=visible helperAndNested=distinct save=refused nativeExecution=NONE sourceUnchanged=true");

            (string Game, string Data, string Ini, string Master) Install(string name, string game)
            {
                var gameRoot = Path.Combine(directory, name); var data = Path.Combine(gameRoot, "Data");
                Directory.CreateDirectory(data);
                ModInstallationContracts.WritePlugin(data, game + ".esm");
                ModContentContracts.WriteArchive(Path.Combine(data, "Base.bsa"), "authored fixture");
                File.WriteAllBytes(Path.Combine(gameRoot, game + ".exe"), Image([], dll: false));
                var ini = Path.Combine(gameRoot, "Archives.ini");
                File.WriteAllText(ini, "[Archive]\nsArchiveList=Base.bsa\n");
                return (gameRoot, data, ini, game + ".esm");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static RuntimeLiveContentSource Open((string Game, string Data, string Ini, string Master) install,
        string game, string[] additional, bool enabled = true) => RuntimeLiveContentSource.Open(install.Game, game,
            additional, [install.Master], install.Ini, nativePluginsEnabled: enabled);

    private static string Write(string path, string[] exports)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Image(exports, dll: true));
        return path;
    }

    // First-party PE32 declarations only; neither fixtures nor original DLLs
    // are loaded. Exports point at authored RET instructions in a code section.
    private static byte[] Image(string[] exports, bool dll)
    {
        var bytes = new byte[0x600];
        Short(0, 0x5a4d); Word(0x3c, 0x80); Word(0x80, 0x4550);
        Short(0x84, 0x14c); Short(0x86, 2); Short(0x94, 0xe0); Short(0x96, dll ? (ushort)0x2102 : (ushort)0x102);
        Short(0x98, 0x10b); Word(0xb4, 0x400000); Word(0xb8, 0x1000); Word(0xbc, 0x200);
        Word(0xd0, 0x3000); Word(0xd4, 0x200); Short(0xdc, 3); Word(0xf4, 16);
        Section(0x178, ".text", 0x1000, 0x200, 0x60000020);
        Section(0x1a0, ".rdata", 0x2000, 0x400, 0x40000040);
        if (exports.Length > 0)
        {
            Word(0xf8, 0x2000); Word(0xfc, 0x180);
            Word(0x410, 1); Word(0x414, checked((uint)exports.Length)); Word(0x418, checked((uint)exports.Length));
            Word(0x41c, 0x2040); Word(0x420, 0x2060); Word(0x424, 0x2080);
            var literal = 0x490;
            for (var index = 0; index < exports.Length; index++)
            {
                bytes[0x200 + index] = 0xc3;
                Word(0x440 + index * 4, checked((uint)(0x1000 + index)));
                Word(0x460 + index * 4, checked((uint)(0x2000 + literal - 0x400)));
                Short(0x480 + index * 2, checked((ushort)index));
                var name = Encoding.ASCII.GetBytes(exports[index] + "\0");
                name.CopyTo(bytes, literal); literal += name.Length;
            }
        }
        return bytes;
        void Word(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        void Short(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        void Section(int at, string name, uint rva, uint file, uint flags)
        {
            Encoding.ASCII.GetBytes(name).CopyTo(bytes, at);
            Word(at + 8, 0x200); Word(at + 12, rva); Word(at + 16, 0x200); Word(at + 20, file); Word(at + 36, flags);
        }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidDataException("Native plugin discovery: " + reason); }
    private static void Refuse(Action action, string expected)
    {
        try { action(); }
        catch (NotSupportedException error) when (error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
        throw new InvalidDataException("Native plugin discovery lost its exact refusal: " + expected);
    }
}
