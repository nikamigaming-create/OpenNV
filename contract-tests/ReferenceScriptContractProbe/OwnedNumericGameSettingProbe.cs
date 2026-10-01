using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedNumericGameSettingProbe
{
    internal static void Run(string mod, string root, string baseRoot, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var hash = SHA256.HashData(quest.ReadData());
            var commands = quest.ReadSubrecords().Where(field => field.Signature == "SCTX")
                .SelectMany(field => FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)))
                .Where(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]
                    .Equals("SetNumericGameSetting", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (commands.Length == 0) throw new InvalidDataException("Selected owned quest has no numeric game-setting command.");
            var names = new Dictionary<string, (double Before, double Expected)>(StringComparer.OrdinalIgnoreCase);
            var script = FalloutScriptLocals.AttachedScript(records, quest) ?? throw new InvalidDataException("Owned quest has no compiled script owner.");
            var executor = new FalloutReferenceScripts(records, world, new FalloutQuestState(records), new((_, _) => false,
                _ => throw new InvalidDataException("Numeric setting component invented a presentation effect.")));
            // Admit the same existing derived-player boundary before the source
            // commands. Karma writes must not invalidate unrelated vitals copies.
            var player = records.GetEffective(records.RuntimeFormKey(7));
            var data = player.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
            if (data.Length != 11) throw new InvalidDataException("Owned player SPECIAL extent is unbound.");
            _ = new FalloutPlayerVitals(records, player.FormKey, new(data[4], data[5], data[6], data[7], data[8], data[9], data[10]));
            foreach (var command in commands)
            {
                var parts = command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3 || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new NotSupportedException("This owned component audit requires a constant numeric source operand.");
                var name = parts[1].Trim('"');
                var before = names.TryGetValue(name, out var prior) ? prior.Before : records.NumericSettings.Read(name);
                executor.ExecuteProgram(quest, script, FalloutGameModeProgram.Read("begin GameMode\n" + command + "\nend"), 0);
                if (FalloutGameSettingFloats.Read(records, name) != number || records.NumericSettings.Get(name) != number)
                    throw new InvalidDataException("Owned source setter did not reach both shared numeric readers.");
                names[name] = (before, number);
            }
            var session = new FalloutScriptSession();
            var saved = JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(session.Capture()))!;
            session.Restore(saved);
            if (names.Any(pair => records.NumericSettings.Read(pair.Key) != pair.Value.Expected))
                throw new InvalidDataException("Warm script-session restoration reset numeric settings.");
            using var cold = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            new FalloutScriptSession().Restore(saved);
            if (names.Any(pair => cold.NumericSettings.Read(pair.Key) != pair.Value.Before))
                throw new InvalidDataException("Owned numeric mutation was save-baked into a new stack.");
            if (!SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Owned source result bytes changed during numeric mutation.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-numeric-game-setting-audit/v1", quest = quest.FormKey, winner = quest.Plugin.Name,
                commands = commands.Length, settings = names.Select(pair => new { name = pair.Key, before = pair.Value.Before, after = pair.Value.Expected }).ToArray(),
                warm = true, coldReset = true, sourceReadonly = true, recording = false,
                boundary = "isolated-owned-setting-commands;karma-behavior-and-campaign-parity-unverified",
            }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
