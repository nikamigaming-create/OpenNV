using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedRewardXpProbe
{
    internal static void Run(string gameRoot, string output, FalloutFormKey questKey, short selectedStage, string[] selection)
    {
        var mod = selection is ["--mod", var id, var root, .. var dependencies]
            ? new FalloutModStackSelection([new(id, root, dependencies)]).Resolve(gameRoot)
            : throw new ArgumentException("Owned RewardXP audit requires the selected mod graph.");
        var destination = Path.GetFullPath(output);
        if (!Path.GetExtension(destination).Equals(".json", StringComparison.OrdinalIgnoreCase) || File.Exists(destination) || Directory.Exists(destination))
            throw new InvalidDataException("Owned RewardXP audit requires a fresh JSON output.");
        foreach (var source in mod.ContentRoots.Prepend(gameRoot))
        {
            var rootPath = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (destination.Equals(rootPath, StringComparison.OrdinalIgnoreCase) ||
                destination.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Audit output may not be inside an owned source root.");
        }
        using var outputStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        RuntimeLiveContentSource.Configure(gameRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            mod.ContentRoots.Skip(1).ToArray(), mod.ActivePlugins, mod.Settings);
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            var quest = records.GetEffective(questKey);
            if (quest.Signature != "QUST") throw new InvalidDataException("RewardXP stage source is not QUST.");
            var sourceHash = Convert.ToHexString(SHA256.HashData(quest.ReadData()));
            var script = FalloutScriptLocals.AttachedScript(records, quest) ?? throw new InvalidDataException("Selected quest lacks its source script.");
            short stage = -1;
            var commands = new List<(int Ordinal, string Command, double Operand)>();
            foreach (var field in quest.ReadSubrecords())
            {
                if (field.Signature == "INDX")
                {
                    if (field.Data.Length != 2) throw new InvalidDataException("Stage source extent is invalid.");
                    stage = BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span);
                }
                if (stage != selectedStage || field.Signature != "SCTX") continue;
                var ordinal = 0;
                foreach (var line in FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)))
                {
                    ++ordinal;
                    var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (!words[0].Split('.')[^1].Equals("RewardXP", StringComparison.OrdinalIgnoreCase)) continue;
                    if (words.Length != 2 || !double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var operand))
                        throw new NotSupportedException("Selected XP component requires a constant original operand.");
                    commands.Add((ordinal, line, operand));
                }
            }
            if (commands.Count == 0) throw new InvalidDataException("Selected source stage has no RewardXP command.");
            var player = records.GetEffective(records.RuntimeFormKey(7));
            var data = FalloutActorTemplateOwner.Resolve(records, player, 2).ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
            if (data.Length != 11) throw new InvalidDataException("Owned player stat extent is unbound.");
            var special = new FalloutNativeSpecialState(data[4], data[5], data[6], data[7], data[8], data[9], data[10]);
            var vitals = new FalloutPlayerVitals(records, player.FormKey, special);
            var xp = new FalloutPlayerExperience(records, vitals, () => []);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var executor = new FalloutReferenceScripts(records, world, quests,
                new((_, _) => false, _ => throw new InvalidDataException("XP component invented an unrelated presentation effect."),
                    RewardXp: amount => xp.Reward(amount)));
            foreach (var command in commands)
                executor.ExecuteProgram(quest, script, FalloutGameModeProgram.Read("begin GameMode\n" + command.Command + "\nend"), 0);
            var saved = JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(vitals.State))!;
            var cold = new FalloutPlayerVitals(records, player.FormKey, special, saved);
            var settingOrder = records.Plugins.ToDictionary(plugin => plugin.Plugin.Name, plugin => plugin.LoadOrderIndex,
                StringComparer.OrdinalIgnoreCase);
            var capDeclarations = records.EffectiveRecords("GMST").Where(record =>
                FalloutDialogueTopic.ScriptText(record.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span)
                    .Equals("iMaxCharacterLevel", StringComparison.OrdinalIgnoreCase))
                .OrderBy(record => settingOrder[record.Plugin.Name]).ThenBy(record => record.HeaderOffset).ToArray();
            var cap = unchecked((int)FalloutGameSettingIntegers.Read(records, "iMaxCharacterLevel"));
            if (capDeclarations.Length > 0 && cap != BinaryPrimitives.ReadInt32LittleEndian(
                capDeclarations[^1].ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span))
                throw new InvalidDataException("Named character cap did not use the last winning source declaration.");
            if (cold.State.ExperiencePoints != vitals.State.ExperiencePoints || cold.State.Level != vitals.State.Level)
                throw new InvalidDataException("Owned XP component lost its cold vitals value.");
            if (Convert.ToHexString(SHA256.HashData(quest.ReadData())) != sourceHash)
                throw new InvalidDataException("Owned source changed during isolated XP command execution.");
            JsonSerializer.Serialize(outputStream, new
            {
                schema = "opennv-owned-reward-xp-component/v1", quest = quest.FormKey.ToString(),
                winner = quest.Plugin.Name, sourceHash, stage = selectedStage,
                script = script.FormKey.ToString(), scriptHash = Convert.ToHexString(SHA256.HashData(script.ReadData())),
                commands = commands.Select(value => new { sourceOrdinal = value.Ordinal, amount = value.Operand }).ToArray(),
                after = vitals.State.ExperiencePoints, level = vitals.State.Level, next = vitals.State.NextLevelExperiencePoints,
                cap,
                capDeclarations = capDeclarations.Select(record => new { form = record.FormKey.ToString(),
                    winner = record.Plugin.Name, loadOrder = settingOrder[record.Plugin.Name],
                    value = BinaryPrimitives.ReadInt32LittleEndian(record.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span),
                    sourceHash = Convert.ToHexString(SHA256.HashData(record.ReadData())) }).ToArray(),
                candidateMvid = typeof(FalloutPlayerExperience).Assembly.ManifestModule.ModuleVersionId,
                sourceReadonly = true, cold = true, recording = false,
                boundary = "isolated original constant XP commands; disposable player state with no acquired XP modifiers; " +
                    "whole stage, compiled SCDA authority, live XP modifiers, XP presentation, level-up and campaign parity unverified"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
