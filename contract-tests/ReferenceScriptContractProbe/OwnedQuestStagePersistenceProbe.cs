using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedQuestStagePersistenceProbe
{
    internal static void Run(string game, string mod, string root, FalloutFormKey quest, short stage, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var record = records.GetEffective(quest);
        if (record.Signature != "QUST" || record.IsDeleted) throw new InvalidDataException("Selected source is not a winning quest.");
        var hash = Convert.ToHexString(SHA256.HashData(record.ReadData()));
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var effects = new List<FalloutReferenceScriptEffect>();
        FalloutQuestStages? stages = null;
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            effects.Add(effect);
            if (effect.Kind == FalloutReferenceEffectKind.SetStage)
            {
                stages!.Enter(effect.Target!.Value, effect.Stage);
                return;
            }
            // Explicit component boundary; this is not a native campaign or
            // a reproduction of a later caller's receiver/physics failure.
            throw new NotSupportedException($"Selected source reached native effect {effect.Kind}; this read-only component has no such native owner.");
        }));
        stages = new(records, quests, scripts.StageSteps, quests.Evaluate, evaluateRunOn: true);
        Exception? failed = null;
        try { stages.Enter(quest, stage); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        { failed = error; }
        if (failed is null || stages.HasPendingResults || !stages.HasUnfinishedResults)
            throw new InvalidDataException("Selected source exercised no complete closed-failure receipt.");
        var results = Copy(stages.CaptureResults().ToArray());
        var driver = stages.ClosedFailureFor(failed);
        FalloutQuestStages.ValidateDriverFailure(results, driver);
        var savedQuests = Copy(quests.Capture().ToArray());
        var savedReferences = Copy(world.Capture().ToArray());
        var savedValues = Copy(world.ScriptValues.Capture());
        var savedOverrides = Copy(world.CaptureActorOverrides().ToArray());
        using var coldWorld = new FalloutReferenceWorld(records);
        coldWorld.ScriptValues.Restore(savedValues); coldWorld.Restore(savedReferences); coldWorld.RestoreActorOverrides(savedOverrides);
        var coldQuests = new FalloutQuestState(records); coldQuests.Restore(savedQuests);
        var coldStages = new FalloutQuestStages(records, coldQuests,
            (_, _, _) => throw new InvalidDataException("Cold closed source receipt replayed a result body."),
            _ => throw new InvalidDataException("Cold closed source receipt reevaluated a predicate."), evaluateRunOn: true);
        coldStages.RestoreResults(results);
        foreach (var item in results.Where(item => item.Error is not null))
        {
            Exception? retained = null;
            try { coldStages.Enter(item.Quest, item.Stage); }
            catch (NotSupportedException error) { retained = error; }
            if (retained?.Message != item.Error) throw new InvalidDataException("Cold source result changed its exact retained failure.");
        }
        coldStages.Continue();
        if (JsonSerializer.Serialize(results) != JsonSerializer.Serialize(coldStages.CaptureResults()) ||
            JsonSerializer.Serialize(savedQuests) != JsonSerializer.Serialize(coldQuests.Capture()) ||
            JsonSerializer.Serialize(savedReferences) != JsonSerializer.Serialize(coldWorld.Capture()) ||
            JsonSerializer.Serialize(savedValues) != JsonSerializer.Serialize(coldWorld.ScriptValues.Capture()) ||
            JsonSerializer.Serialize(savedOverrides) != JsonSerializer.Serialize(coldWorld.CaptureActorOverrides()) ||
            Convert.ToHexString(SHA256.HashData(record.ReadData())) != hash)
            throw new InvalidDataException("Cold source receipt changed consumed effects, random state, journal or original source bytes.");
        Console.WriteLine("OPENNV_OWNED_QUEST_STAGE_PERSISTENCE_PASS " + JsonSerializer.Serialize(new
        {
            content.SaveCompatibilityId,
            runtimeMvid = typeof(FalloutQuestStages).Assembly.ManifestModule.ModuleVersionId,
            selected = new { quest = quest.ToString(), stage, winner = record.Plugin.Name, sha256 = hash },
            results, driver, nativeEffectsReached = effects.Select(effect => effect.Kind.ToString()).ToArray(),
            exactClosedSourceFailure = failed.Message,
            proof = "unchanged source program at explicit native component boundary; not actual later campaign GetDetected reproduction",
            coldResultAndPredicateReplay = false, consumedQuestWorldRandomAndSourceRetained = true,
            sourceReadOnly = true, nativeExecution = false, campaignCheckpoint = false, parity = "unverified"
        }));
    }

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
