using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedChallengeProbe
{
    internal static void Run(string mod, string root, string baseRoot, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId); var hash = SHA256.HashData(quest.ReadData());
            var queue = new FalloutHudNotifications(); var challenges = new FalloutChallenges(records, queue);
            using var world = new FalloutReferenceWorld(records);
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Challenge emitted an unrelated effect."), Challenges: challenges));
            var fields = quest.ReadSubrecords().ToArray(); var commands = 0;
            for (var index = 0; index < fields.Length; index++)
            {
                if (fields[index].Signature != "QSDT") continue;
                var next = index + 1;
                while (next < fields.Length && fields[next].Signature is not ("QSDT" or "INDX" or "QOBJ")) next++;
                var entry = fields[index..next];
                var source = entry.SingleOrDefault(field => field.Signature == "SCTX").Data;
                if (source.IsEmpty) continue;
                var selected = FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(source.Span)).Where(line =>
                    line.StartsWith("UnlockChallenge ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("IncrementScriptedChallenge ", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (selected.Length == 0) continue;
                executor.ExecuteStage(quest, entry, string.Join('\n', selected)); commands += selected.Length;
            }
            var saved = challenges.Capture(); var cold = new FalloutChallenges(records, new());
            cold.Restore(JsonSerializer.Deserialize<FalloutChallengesSnapshot>(JsonSerializer.Serialize(saved))!);
            if (commands == 0 || saved.ChallengesCompleted == 0 || saved.Entries.Any(entry => entry.Error is not null) ||
                JsonSerializer.Serialize(cold.Capture()) != JsonSerializer.Serialize(saved) || !hash.AsSpan().SequenceEqual(SHA256.HashData(quest.ReadData())))
                throw new InvalidDataException("Owned challenge command, cold state or source immutability differed.");
            foreach (var entry in saved.Entries.Where(entry => entry.Completed)) cold.IncrementScripted(entry.Form);
            if (JsonSerializer.Serialize(cold.Capture()) != JsonSerializer.Serialize(saved)) throw new InvalidDataException("Owned completion replayed.");
            var hud = FalloutExecutableStringTable.ReadChallengeHudDeclaration(Path.Combine(baseRoot, "FalloutNV.exe"));
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-owned-challenge-audit/v1", quest = quest.FormKey, commands,
                saved.ChallengesCompleted, challenges = saved.Entries.Select(entry => new { entry.Form, entry.Progress, entry.Completed }),
                hud.Seconds, cold = true, boundary = "isolated-owned-commands;ordinary-route-HUD-audio-and-parity-unverified" }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
