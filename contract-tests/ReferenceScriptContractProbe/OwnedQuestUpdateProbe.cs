using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedQuestUpdateProbe
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
            var fields = quest.ReadSubrecords().ToArray();
            var samples = new List<object>(); short stage = -1;
            for (var index = 0; index < fields.Length; index++)
            {
                if (fields[index].Signature == "INDX") stage = BinaryPrimitives.ReadInt16LittleEndian(fields[index].Data.Span);
                if (fields[index].Signature != "QSDT") continue;
                var next = index + 1;
                while (next < fields.Length && fields[next].Signature is not ("QSDT" or "INDX" or "QOBJ")) next++;
                var entry = fields[index..next];
                var source = entry.SingleOrDefault(field => field.Signature == "SCTX").Data;
                if (source.IsEmpty) continue;
                var commands = FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(source.Span))
                    .Where(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant() is
                        "completeallobjectives" or "autodisplayobjectives" or "killquestupdates" or "kqu").ToArray();
                if (!commands.Any(line => line.StartsWith("CompleteAllObjectives", StringComparison.OrdinalIgnoreCase))) continue;
                Require(commands.Length == 3 && commands[0].StartsWith("CompleteAllObjectives ", StringComparison.OrdinalIgnoreCase) &&
                    commands[1].Equals("AutoDisplayObjectives 0", StringComparison.OrdinalIgnoreCase) &&
                    commands[2].Equals("KillQuestUpdates", StringComparison.OrdinalIgnoreCase),
                    "Selected source quest-update sequence needs a different component audit.");
                var queue = new FalloutHudNotifications(); var quests = new FalloutQuestState(records, queue);
                _ = quests.Stage(quest.FormKey);
                var declared = quests.Capture().Single().Objectives!;
                Require(declared.Count >= 2, "Owned objective audit requires displayed and hidden source objectives.");
                // Isolated owner setup exercises both display states. It does
                // not enter the source stage or claim ordinary route progress.
                quests.ApplyObjective(quest.FormKey, declared[0].Index, true, true);
                var before = quests.Capture().Single();
                var session = new FalloutScriptSession { AutoDisplayObjectives = true };
                var autoCalls = 0;
                var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
                {
                    Require(effect.Kind == FalloutReferenceEffectKind.AutoDisplayObjectives &&
                        quests.Capture().Single().Objectives!.All(value => value.Completed) &&
                        !queue.Capture().QuestUpdateCancellationRequested,
                        "Owned objective commands lost source order before the HUD cancellation request.");
                    session.AutoDisplayObjectives = effect.Enable; autoCalls++;
                }));
                executor.ExecuteStage(quest, entry, string.Join('\n', commands));
                var after = quests.Capture().Single();
                Require(after.Stage == before.Stage && after.Running == before.Running && after.Completed == before.Completed &&
                    after.EnteredStages.SequenceEqual(before.EnteredStages) && after.Objectives!.All(value => value.Completed) &&
                    after.Objectives!.Select(value => value.Displayed).SequenceEqual(before.Objectives!.Select(value => value.Displayed)) &&
                    autoCalls == 1 && !session.AutoDisplayObjectives && queue.Capture().QuestUpdateCancellationRequested,
                    "Owned command slice changed quest progress, display flags or session policy.");
                var saved = JsonSerializer.Deserialize<FalloutHudNotificationsSnapshot>(JsonSerializer.Serialize(queue.Capture()))!;
                var coldQueue = new FalloutHudNotifications(); coldQueue.Restore(saved);
                var coldQuests = new FalloutQuestState(records, coldQueue);
                coldQuests.Restore(JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!);
                var coldSession = new FalloutScriptSession();
                coldSession.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(session.Capture()))!);
                Require(coldQueue.ConsumeQuestUpdateCancellation(false) && coldQueue.Capture().Pending.Count == 0 &&
                    !coldSession.AutoDisplayObjectives && JsonSerializer.Serialize(coldQuests.Capture()) == JsonSerializer.Serialize(quests.Capture()),
                    "Cold restoration lost a pending cancellation, objective flags or session policy.");
                samples.Add(new { sourceStage = stage, declaredObjectives = declared.Count, selectedCommands = commands.Length,
                    completed = after.Objectives!.Count, displayed = after.Objectives.Count(value => value.Displayed) });
            }
            Require(samples.Count != 0 && SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(hash),
                "No owned completion sequence was audited or its source bytes changed.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-quest-updates-audit/v1", quest = quest.FormKey, winner = quest.Plugin.Name,
                samples, sourceOrder = true, questProgressRetained = true, displayRetained = true,
                coldCancellation = true, sessionPolicyRetained = true, sourceReadOnly = true, recording = false,
                boundary = "isolated-owned-result-command-slice;stage-entry-ordinary-input-HUD-pixels-and-retail-parity-unverified"
            }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
