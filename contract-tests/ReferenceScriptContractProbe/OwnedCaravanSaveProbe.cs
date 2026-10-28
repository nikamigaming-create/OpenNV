using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCaravanSaveProbe
{
    internal static void Run(string game, string savePath)
    {
        var originalBytes = File.ReadAllBytes(savePath);
        var sourceHash = SHA256.HashData(originalBytes);
        var original = JsonSerializer.Deserialize<FalloutNativeCampaignState>(originalBytes) ??
            throw new InvalidDataException("Owned campaign checkpoint is absent.");
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
        var cell = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
        var loaded = FalloutNativeCampaignSave.Read(savePath, content.SaveCompatibilityId, records,
            FalloutNativeVigorResolver.Resolve(records, cell), FalloutNativeTagSkillResolver.Resolve(records, controls),
            FalloutOpeningInventoryGrantResolver.Resolve(records, controls, "VCG01"),
            FalloutNativeTraitFarewellResolver.Resolve(records, controls, cell));
        if (original.Schema != FalloutNativeCampaignSave.BeforeCaravanCardsSchema || original.PlayerCaravanCards is not null ||
            loaded.State.Schema != FalloutNativeCampaignSave.ExpectedSchema || loaded.State.PlayerCaravanCards is not null)
            throw new InvalidDataException("Owned legacy checkpoint admission invented Caravan collection history.");
        using var world = new FalloutReferenceWorld(records);
        world.PlayerCaravanCards.Restore(loaded.State.PlayerCaravanCards);
        if (world.PlayerCaravanCards.Known) throw new InvalidDataException("Legacy Caravan history became known empty.");
        var inMemory = loaded.State with { PlayerCaravanCards = world.PlayerCaravanCards.Capture() };
        var decoded = JsonSerializer.Deserialize<FalloutNativeCampaignState>(JsonSerializer.Serialize(inMemory)) ??
            throw new InvalidDataException("In-memory campaign JSON roundtrip failed.");
        using var cold = new FalloutReferenceWorld(records);
        cold.PlayerCaravanCards.Restore(decoded.PlayerCaravanCards);
        if (cold.PlayerCaravanCards.Known || !SHA256.HashData(File.ReadAllBytes(savePath)).SequenceEqual(sourceHash))
            throw new InvalidDataException("Unknown collection history or the read-only checkpoint changed.");
        Console.WriteLine("OPENNV_OWNED_CARAVAN_LEGACY_SAVE_PASS actualReader=true unknownPreserved=true inMemoryJson=true sourceUnchanged=true gameplayAndFullColdSave=unverified");
    }
}
