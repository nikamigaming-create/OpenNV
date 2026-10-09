using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedChallengeProbe
{
    // This selected-source lane constructs and restores genuine current owners
    // without invoking a quest stage. Ordinary state/event/HUD/audio execution
    // is independent; selecting text lines cannot establish that acceptance.
    internal static void Run(string mod, string root, string baseRoot, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var hash = SHA256.HashData(quest.ReadData());
            using var world = new FalloutReferenceWorld(records);
            world.ConfigureCampaignPlayerRuntime(new());
            var saved = world.Challenges.Capture(); var counters = world.PlayerStatistics.Capture();
            var rows = new List<object>(); var failures = new List<Exception>();
            var fields = quest.ReadSubrecords().ToArray(); var stage = -1; var ordinal = 0;
            foreach (var index in Enumerable.Range(0, fields.Length))
            {
                if (fields[index].Signature == "INDX") stage = index;
                if (fields[index].Signature == "QOBJ") stage = -1;
                if (fields[index].Signature != "QSDT") continue;
                try
                {
                    var scope = FalloutScriptScope.QuestEntry(quest, stage, index);
                    var sourceFields = scope.Count(field => field.Signature == "SCTX");
                    if (!scope.Compiled)
                    {
                        if (scope.Any(field => field.Signature is "SCHR" or "SCTX"))
                            throw new NotSupportedException("Original challenge result has no authoritative compiled body; diagnostic source execution is refused.");
                        rows.Add(new { ordinal = ordinal++, scope.StageOrdinal, scope.ResultOrdinal, scope.ScopeSha256,
                            disposition = "no-program", sourceFields, runtime = "UNEXECUTED" });
                        continue;
                    }
                    var program = FalloutCompiledScriptProgram.Read(quest, scope, standalone: false);
                    _ = FalloutCompiledControlFlow.Read(program.ResultInstructions());
                    rows.Add(new { ordinal = ordinal++, scope.StageOrdinal, scope.ResultOrdinal, scope.ScopeSha256,
                        disposition = program.CodeBytes == 0 ? "authored-empty-SCDA" : "binary-structure-read",
                        program.ProgramSha256, program.CodeBytes, sourceFields,
                        uninspectedCommandInstructions = program.Instructions.Count(instruction => instruction.Opcode >= 0x1000),
                        runtime = "UNEXECUTED" });
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                    rows.Add(new { ordinal = ordinal++, field = index, disposition = "refused",
                        failure = failure.GetType().FullName, error = failure.Message, runtime = "UNEXECUTED" });
                }
            }
            using var cold = new FalloutReferenceWorld(records);
            cold.ConfigureCampaignPlayerRuntime(new(),
                JsonSerializer.Deserialize<FalloutPlayerStatisticsSnapshot>(JsonSerializer.Serialize(counters))!,
                JsonSerializer.Deserialize<FalloutChallengesSnapshot>(JsonSerializer.Serialize(saved))!);
            if (JsonSerializer.Serialize(cold.Challenges.Capture()) != JsonSerializer.Serialize(saved) ||
                JsonSerializer.Serialize(cold.PlayerStatistics.Capture()) != JsonSerializer.Serialize(counters) ||
                saved.Events != 0 || counters.Operations != 0 || world.InstanceCount != 0 || cold.InstanceCount != 0 ||
                !hash.AsSpan().SequenceEqual(SHA256.HashData(quest.ReadData())) ||
                saved.Entries.Any(entry => entry.SourceSha256 != FalloutChallengeDefinition.Read(records.GetEffective(entry.Form)).Sha256))
                throw new InvalidDataException("Original challenge constructor/source/no-effect cold state differed.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-challenge-source-audit/v2", quest = quest.FormKey,
                source = saved.Source?.Identity, sourceDefinitions = saved.Entries.Count, sourceRanges = rows,
                sourceFailures = failures.Count, originalsUnchanged = true, constructorColdOnly = true,
                compiledExecution = "UNEXECUTED", nativeStatsMenu = "UNOWNED", nativeCue = "UNOWNED",
                boundary = "selected-winning-source-and-constructor-only;ordinary-event-awards-HUD-audio-and-parity-unverified"
            }));
            if (failures.Count != 0) throw new AggregateException("Original challenge result source ranges retained refused owners.", failures);
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
