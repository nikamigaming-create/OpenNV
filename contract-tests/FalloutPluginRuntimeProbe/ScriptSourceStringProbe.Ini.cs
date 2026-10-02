using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ScriptSourceStringProbe
{
    private static void IniOwners()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-ini-hosts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string body = "if !name\nname = \"fSource:Display\"\nendif\nprefix += 1\n" +
                "literal = GetNumericINISetting \"FSOURCE:DISPLAY\"\nobserved = GetNumericINISetting name\n" +
                "frame = 1 || GetNumericINISetting MissingOwner.name\n" +
                "frame += 1 || IsModLoaded (GetGameLoaded)\n" +
                "name = \"sourcestring.ESM\"\nstatus = IsModLoaded name\nqualified = IsModLoaded \"Inactive.esp\"\n" +
                "literal = IsModLoaded prefix\nprefix += 100\n";
            var plugin = Header().Concat(Script(0x100, body, 11, false)).Concat(Script(0x102, body, 31, true)).Concat(Function())
                .Concat(Record("ACTI", 0x400, Field("SCRI", BitConverter.GetBytes(0x100u))))
                .Concat(Record("ACTI", 0x401))
                .Concat(Record("QUST", 0x700, Field("EDID", Text("SourceQuest")), Field("DATA", new byte[8]),
                    Field("SCRI", BitConverter.GetBytes(0x102u))))
                .Concat(Cell()).ToArray();
            var path = Path.Combine(directory, "SourceString.esm");
            File.WriteAllBytes(path, plugin);
            File.WriteAllBytes(Path.Combine(directory, "Inactive.esp"), Header());
            var declarations = new[] { new FalloutIniDeclaration("fSource:Display", FalloutIniCollection.Main, 0x3f800000) };
            FalloutInstallationSettings Installation(float value) => FalloutInstallationSettings.ReadIniLayers(() => declarations,
                [new("synthetic-profile", null, [new("Display", "fSource", value.ToString(System.Globalization.CultureInfo.InvariantCulture))])], "synthetic-executable");
            var sources = new[] { new FalloutPluginSource("SourceString.esm", path) };
            using var records = FalloutPluginStack.Load(sources, false, out _, Installation(73.25f));
            using var otherRecords = FalloutPluginStack.Load(sources, false, out _, Installation(81.5f));
            using var mounted = FalloutPluginStack.Load([sources[0], new("Inactive.esp", Path.Combine(directory, "Inactive.esp"))],
                false, out _, Installation(73.25f));
            Require(otherRecords.IniSettings.Get("fSource:Display") == 81.5 && records.IniSettings.Get("fSource:Display") == 73.25,
                "Explicit installation owners leaked across independent source graphs.");
            Require(!records.IsPluginLoaded("Inactive.esp") && mounted.IsPluginLoaded("inactive.ESP") &&
                !records.IsPluginLoaded("SourceString") && !records.IsPluginLoaded(path) && !records.IsPluginLoaded(""),
                "Plugin queries inferred loaded state from file presence, path normalization or another graph.");
            using var unowned = FalloutPluginStack.Load(sources);
            Reject(() => unowned.IniSettings.Get("fSource:Display"));
            var state = new FalloutQuestState(records);
            var cell = FalloutCellSceneReader.Read(records, Key(0x600));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            FalloutReferenceScripts Executor(FalloutReferenceWorld owner) => new(records, owner, state,
                new((_, _) => false, _ => throw new InvalidDataException("INI reads requested presentation.")));
            var executor = Executor(world);
            var failed = executor.Dispatch(Key(0x500), "GameMode");
            Require(failed.Error is not null && world.Get(Key(0x500)).Read(12) == 1 &&
                world.Get(Key(0x500)).Read(13) == 73.25 && world.Get(Key(0x500)).Read(14) == 73.25 &&
                world.Get(Key(0x500)).Read(15) == 2 && world.Get(Key(0x500)).Read(16) == 0 && world.Get(Key(0x500)).Read(17) == 1,
                "Reference INI reads lost typed strings, lazy evaluation or the failed prefix: " + failed.Error);
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(RoundTrip(world.Capture().ToArray())); cold.ScriptValues.Restore(RoundTrip(world.ScriptValues.Capture()));
            cold.ValidateValueHandles(); cold.LoadCell(cell); Executor(cold).Dispatch(Key(0x500), "GameMode");
            Require(JsonSerializer.Serialize(world.Capture()) == JsonSerializer.Serialize(cold.Capture()),
                "Cold reference INI failure replayed its prefix.");
            var questState = new FalloutQuestState(records); questState.SetRunning(Key(0x700), true);
            FalloutQuestScripts Scripts(FalloutQuestState owner) => new(records, owner, new HashSet<FalloutFormKey>(),
                new FalloutPlayerInventory(), defaultProcessingDelay: 1);
            var scripts = Scripts(questState); scripts.Advance(1);
            Require(scripts.Capture().Instances.Single().Error is not null && questState.Variable(Key(0x700), 32) == 1 &&
                questState.Variable(Key(0x700), 33) == 73.25 && questState.Variable(Key(0x700), 34) == 73.25 &&
                questState.Variable(Key(0x700), 35) == 2 && questState.Variable(Key(0x700), 36) == 0 && questState.Variable(Key(0x700), 37) == 1,
                "Fallback quest INI reads lost typed strings, lazy evaluation or the failed prefix.");
            var coldState = new FalloutQuestState(records); coldState.Restore(RoundTrip(questState.Capture()));
            var coldScripts = Scripts(coldState); coldScripts.Restore(RoundTrip(scripts.Capture())); coldScripts.Advance(1);
            Require(JsonSerializer.Serialize(questState.Capture()) == JsonSerializer.Serialize(coldState.Capture()) &&
                JsonSerializer.Serialize(scripts.Capture()) == JsonSerializer.Serialize(coldScripts.Capture()),
                "Cold fallback INI failure changed its saved handles or replayed the prefix.");
        }
        finally
        {
            File.Delete(Path.Combine(directory, "SourceString.esm")); File.Delete(Path.Combine(directory, "Inactive.esp"));
            Directory.Delete(directory);
        }
    }
}
