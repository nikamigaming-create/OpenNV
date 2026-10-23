using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedTerminalHackStateProbe
{
    internal static void Run(string game, string savePath)
    {
        var saveBytes = File.ReadAllBytes(savePath);
        var original = JsonSerializer.Deserialize<FalloutNativeCampaignState>(saveBytes) ??
            throw new InvalidDataException("Owned campaign is absent.");
        if (original.Schema != FalloutNativeCampaignSave.BeforeTerminalHackStateSchema)
            throw new InvalidDataException("This migration audit requires a genuine v56 campaign.");
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
        var cell = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
        var restored = FalloutNativeCampaignSave.Read(savePath, content.SaveCompatibilityId, records,
            FalloutNativeVigorResolver.Resolve(records, cell),
            FalloutNativeTagSkillResolver.Resolve(records, controls),
            FalloutOpeningInventoryGrantResolver.Resolve(records, controls, "VCG01"),
            FalloutNativeTraitFarewellResolver.Resolve(records, controls, cell));
        Require(restored.State.Schema == FalloutNativeCampaignSave.ExpectedSchema &&
            JsonSerializer.Serialize(restored.State.References) == JsonSerializer.Serialize(original.References) &&
            restored.State.References!.All(value => value.TerminalHackState is null),
            "Actual campaign reader changed reference history during v56 admission.");
        var selected = restored.State.References!.First(value => records.GetEffective(value.Base).Signature == "TERM");
        var referenceHash = SHA256.HashData(records.GetEffective(selected.Reference).ReadData());
        var baseHash = SHA256.HashData(records.GetEffective(selected.Base).ReadData());
        using var legacy = new FalloutReferenceWorld(records);
        legacy.Restore([selected]);
        Reject(() => legacy.TerminalHackResult(selected.Reference));
        using var fresh = new FalloutReferenceWorld(records);
        _ = fresh.Get(selected.Reference);
        Require(fresh.TerminalHackResult(selected.Reference).RawState == 0, "Fresh owned TERM was not known zero.");
        var originalLock = fresh.GetLocked(selected.Reference);
        fresh.SetTerminalHackFailure(selected.Reference, true);
        fresh.SetTerminalHacked(selected.Reference, true);
        var captured = fresh.Capture();
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(captured))!);
        Require(cold.TerminalHackResult(selected.Reference).RawState == 0x81 &&
            cold.GetLocked(selected.Reference) == originalLock &&
            JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(captured),
            "Owned result byte or independent lock changed cold.");
        var temporarySave = Path.Combine(Path.GetTempPath(), $"opennv-terminal-state-{Guid.NewGuid():N}.json");
        try
        {
            var knownCampaign = restored.State with
            {
                References = restored.State.References!.Select(value => value.Reference == selected.Reference
                    ? value with { TerminalHackState = cold.TerminalHackResult(selected.Reference) } : value).ToArray()
            };
            FalloutNativeCampaignSave.Write(temporarySave, knownCampaign);
            var knownCold = FalloutNativeCampaignSave.Read(temporarySave, content.SaveCompatibilityId, records,
                FalloutNativeVigorResolver.Resolve(records, cell),
                FalloutNativeTagSkillResolver.Resolve(records, controls),
                FalloutOpeningInventoryGrantResolver.Resolve(records, controls, "VCG01"),
                FalloutNativeTraitFarewellResolver.Resolve(records, controls, cell));
            Require(JsonSerializer.Serialize(knownCold.State) == JsonSerializer.Serialize(knownCampaign),
                "Actual current campaign codec changed known result state or another retained owner.");
        }
        finally { File.Delete(temporarySave); }
        Require(File.ReadAllBytes(savePath).SequenceEqual(saveBytes) &&
            SHA256.HashData(records.GetEffective(selected.Reference).ReadData()).SequenceEqual(referenceHash) &&
            SHA256.HashData(records.GetEffective(selected.Base).ReadData()).SequenceEqual(baseHash),
            "Owned campaign or winning records changed.");
        Console.WriteLine($"OPENNV_OWNED_TERMINAL_HACK_STATE_PASS reference={selected.Reference} base={selected.Base} " +
            $"saveHash={Convert.ToHexString(SHA256.HashData(saveBytes))} referenceHash={Convert.ToHexString(referenceHash)} " +
            $"baseHash={Convert.ToHexString(baseHash)} campaignReadMigration=true referenceHistoryUnchanged=true " +
            "legacyUnknown=true freshKnownZero=true coldResult129=true fullCurrentCodec=true " +
            "controlledResultInputs=true genericLockUnchanged=true nativeAndRetail=unverified");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (NotSupportedException error) when (error.Message.Contains("history was not retained", StringComparison.Ordinal)) { return; }
        throw new InvalidDataException("Legacy owned TERM acquired invented hacking history.");
    }
}
