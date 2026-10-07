using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedGameTimeCommandProbe
{
    internal static void Run(string game, string mod, string root, string checkpoint, string output,
        string selector, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        var destination = Path.GetFullPath(output);
        if (!Path.GetExtension(destination).Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(destination) || Directory.Exists(destination))
            throw new InvalidDataException("Owned time audit requires a fresh JSON output.");
        foreach (var input in setup.ContentRoots.Prepend(game))
        {
            var source = Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (destination.Equals(source, StringComparison.OrdinalIgnoreCase) ||
                destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Time audit output may not be inside an owned source root.");
        }
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var checkpointHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(checkpoint)));
            var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
            var opening = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
            var saved = FalloutNativeCampaignSave.Read(checkpoint, content.SaveCompatibilityId, records,
                FalloutNativeVigorResolver.Resolve(records, opening), FalloutNativeTagSkillResolver.Resolve(records, controls),
                FalloutOpeningInventoryGrantResolver.Resolve(records, controls, "VCG01"),
                FalloutNativeTraitFarewellResolver.Resolve(records, controls, opening)).State;
            if (saved.Globals is null || saved.GameTime is null || saved.Quests is null || saved.Scripts?.Session is null)
                throw new InvalidDataException("Time audit requires the complete selected-source clock, globals, quests and player session.");
            var calendar = FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe"));
            var forms = FalloutGameTimeBindings.Read(records);
            var script = FalloutDialogueTopic.Find(records, "SCPT", selector);
            var scriptHash = Hash(script);
            var quest = records.EffectiveRecords("QUST").Single(record =>
                FalloutScriptLocals.AttachedScript(records, record)?.FormKey == script.FormKey);
            var rows = FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(script.ReadSubrecords()
                .Single(field => field.Signature == "SCTX").Data.Span))
                .Select((line, index) => (Ordinal: index + 1, Command: line,
                    Words: line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).ToArray();
            var commands = rows.Where(row => row.Words[0].Equals("SetGameHour", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (commands.Length == 0) throw new InvalidDataException("Selected script has no original SetGameHour statements.");
            var cases = new List<object>();
            foreach (var command in commands)
            {
                if (command.Words.Length != 2) throw new NotSupportedException("Time component requires one original hour operand.");
                var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals);
                var clock = new FalloutGameTime(globals, forms, calendar); clock.Restore(saved.GameTime);
                var quests = new FalloutQuestState(records); quests.Restore(saved.Quests);
                using var world = new FalloutReferenceWorld(records);
                var session = new FalloutScriptSession(world.NoActivationSound, records.QuestObjects);
                session.Restore(saved.Scripts.Session);
                var executor = new FalloutReferenceScripts(records, world, quests,
                    new((_, _) => throw new InvalidDataException("Time component queried furniture."),
                        _ => throw new InvalidDataException("Time component emitted a presentation effect."),
                        Globals: globals, IsHardcore: () => session.Hardcore, GameTime: clock));
                void Execute(string line) => executor.ExecuteProgram(quest, script,
                    FalloutGameModeProgram.Read("begin GameMode\n" + line + "\nend"), 0);
                int? queryOrdinal = null;
                float operand;
                if (double.TryParse(command.Words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var constant))
                    operand = FalloutGameTime.SourceHour(constant);
                else
                {
                    var global = FalloutDialogueTopic.Find(records, "GLOB", command.Words[1]);
                    var query = rows.Single(row => row.Words.Length >= 4 &&
                        row.Words[0].Equals("set", StringComparison.OrdinalIgnoreCase) &&
                        row.Words[1].Equals(command.Words[1], StringComparison.OrdinalIgnoreCase) &&
                        row.Words[2].Equals("to", StringComparison.OrdinalIgnoreCase) &&
                        row.Words.Any(word => word.Equals("GetGameDaysPassed", StringComparison.OrdinalIgnoreCase)));
                    var before = globals.Capture(); var beforeClock = clock.Capture();
                    Execute(query.Command);
                    if (clock.Capture() != beforeClock || before.Values.Where(value => value.Form != global.FormKey)
                        .Any(value => BitConverter.SingleToInt32Bits(value.Value) != BitConverter.SingleToInt32Bits(globals.Get(value.Form))))
                        throw new InvalidDataException("Original date query changed an unrelated clock/global.");
                    operand = FalloutGameTime.SourceHour(globals.Get(global.FormKey)); queryOrdinal = query.Ordinal;
                }
                var beforeHour = clock.Hour;
                Execute(command.Command);
                var expected = beforeHour <= operand ? operand : 24 + operand;
                if (BitConverter.SingleToInt32Bits(clock.Hour) != BitConverter.SingleToInt32Bits(expected))
                    throw new InvalidDataException("Original hour statement did not publish its source Float32 target.");
                var pending = globals.Capture(); var pendingClock = clock.Capture();
                var coldGlobals = FalloutGlobalState.Read(records);
                coldGlobals.Restore(JsonSerializer.Deserialize<FalloutGlobalStateSnapshot>(JsonSerializer.Serialize(pending))!);
                var cold = new FalloutGameTime(coldGlobals, forms, calendar);
                cold.Restore(JsonSerializer.Deserialize<FalloutGameTimeSnapshot>(JsonSerializer.Serialize(pendingClock))!);
                for (var frame = 0; frame < 31; ++frame) { clock.AdvanceSimulation(1f / 60); cold.AdvanceSimulation(1f / 60); }
                if (!globals.Capture().Values.SequenceEqual(coldGlobals.Capture().Values) || clock.Capture() != cold.Capture())
                    throw new InvalidDataException("Original pending-hour component lost its cold next-tick state.");
                cases.Add(new
                {
                    sourceOrdinal = command.Ordinal, queryOrdinal, operand, beforeHour,
                    pendingHour = expected, pendingHourBits = BitConverter.SingleToInt32Bits(expected),
                    year = globals.Get(forms.Year), month = globals.Get(forms.Month), day = globals.Get(forms.Day),
                    hour = clock.Hour, hourBits = BitConverter.SingleToInt32Bits(clock.Hour),
                    clock = clock.Capture(), cold = true
                });
            }
            if (Hash(script) != scriptHash || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(checkpoint))) != checkpointHash)
                throw new InvalidDataException("Owned time audit changed an input source or immutable checkpoint.");
            using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, new
            {
                schema = "opennv-owned-game-time-component/v1", script = script.FormKey.ToString(),
                winner = script.Plugin.Name, scriptHash, quest = quest.FormKey.ToString(), questHash = Hash(quest),
                checkpointHash, calendarHash = calendar.SourceSha256, cases,
                candidateMvid = typeof(FalloutGameTime).Assembly.ManifestModule.ModuleVersionId,
                sourceReadonly = true, checkpointReadonly = true, recording = false,
                boundary = "isolated original source time statements after complete checkpoint clock/global restoration; " +
                    "whole travel script, compiled SCDA/DLL authority, forced Hardcore needs, native movies, " +
                    "world/inventory/control transitions, ordinary train traversal and retail parity unverified"
            }, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"OPENNV_OWNED_GAME_TIME_COMPONENT_PASS script={script.FormKey} cases={cases.Count} " +
                "sourceReadonly=true checkpointReadonly=true pendingHourCold=true recording=false gameplayAndDllCompatibility=unverified");
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }

    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
}
