using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedReputationProbe
{
    internal static void Run(string game, string savePath)
    {
        var bytes = File.ReadAllBytes(savePath);
        var original = JsonSerializer.Deserialize<FalloutNativeCampaignState>(bytes)!;
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
        var cell = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
        FalloutNativeCampaignRestore Read(string path) => FalloutNativeCampaignSave.Read(path,
            content.SaveCompatibilityId, records, FalloutNativeVigorResolver.Resolve(records, cell),
            FalloutNativeTagSkillResolver.Resolve(records, controls),
            FalloutOpeningInventoryGrantResolver.Resolve(records, controls, "VCG01"),
            FalloutNativeTraitFarewellResolver.Resolve(records, controls, cell));
        var restored = Read(savePath);
        Require(original.Reputations is null && restored.State.Reputations is null &&
            restored.State.Schema == FalloutNativeCampaignSave.ExpectedSchema &&
            JsonSerializer.Serialize(original.References) == JsonSerializer.Serialize(restored.State.References),
            "Actual legacy campaign admission changed reference history or invented reputation.");
        var sources = records.EffectiveRecords("REPU");
        Require(sources.Count > 0, "Owned source has no reputation declarations.");
        var hashes = sources.Select(source => SHA256.HashData(source.ReadData())).ToArray();
        using var fresh = new FalloutReferenceWorld(records);
        foreach (var source in sources)
            Require(fresh.Reputation(source.FormKey, 0) == 0 && fresh.Reputation(source.FormKey, 1) == 0,
                "Fresh owned reputation was not source initialized zero.");
        var selected = sources[0].FormKey;
        using var legacy = new FalloutReferenceWorld(records);
        legacy.RestoreReputations(restored.State.Reputations);
        Reject(() => legacy.Reputation(selected, 0)); Reject(() => legacy.Reputation(selected, 1));
        legacy.SetReputation(selected, 1, -17.125);
        Require(legacy.Reputation(selected, 1) == -17.125f, "Owned direct setter clamped or changed its finite source float.");
        Reject(() => legacy.Reputation(selected, 0));
        var snapshot = legacy.CaptureReputations();
        var temporary = Path.Combine(Path.GetTempPath(), $"opennv-reputation-{Guid.NewGuid():N}.json");
        try
        {
            var controlled = restored.State with { Reputations = snapshot };
            FalloutNativeCampaignSave.Write(temporary, controlled);
            var cold = Read(temporary);
            Require(JsonSerializer.Serialize(cold.State) == JsonSerializer.Serialize(controlled),
                "Actual current campaign codec changed reputation or another campaign lane.");
            using var world = new FalloutReferenceWorld(records);
            world.RestoreReputations(cold.State.Reputations);
            Require(world.Reputation(selected, 1) == -17.125f && !world.CaptureReputations().FreshBaseline,
                "Actual campaign cold reputation lost its explicit axis or unknown baseline.");
            Reject(() => world.Reputation(selected, 0));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        for (var index = 0; index < sources.Count; index++)
            Require(SHA256.HashData(sources[index].ReadData()).SequenceEqual(hashes[index]), "Owned reputation source changed.");
        Require(File.ReadAllBytes(savePath).SequenceEqual(bytes), "Original campaign bytes changed.");
        Console.WriteLine($"OPENNV_OWNED_REPUTATION_STATE_PASS declarations={sources.Count} selected={selected} " +
            $"sourceHash={Convert.ToHexString(hashes[0])} saveHash={Convert.ToHexString(SHA256.HashData(bytes))} " +
            "actualLegacyReader=true referenceHistoryUnchanged=true legacyUnknown=true freshZero=true " +
            "controlledExplicitAxis=true fullCurrentCodec=true unknownOtherAxisPreserved=true sourceAndSaveUnchanged=true " +
            "addRemoveCrimeNotificationOrdinaryAndRetail=unverified");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (NotSupportedException) { return; }
        throw new InvalidDataException("Unknown owned reputation was invented.");
    }
}
