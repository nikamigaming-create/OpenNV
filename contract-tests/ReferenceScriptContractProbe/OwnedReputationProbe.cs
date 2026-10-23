using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedReputationProbe
{
    internal static void Run(string game, string savePath, ushort conditionFunction = 573)
    {
        var bytes = File.ReadAllBytes(savePath);
        var original = JsonSerializer.Deserialize<FalloutNativeCampaignState>(bytes)!;
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        RunConditions(records, conditionFunction);
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

    private static void RunConditions(FalloutPluginStack records, ushort function)
    {
        using var world = new FalloutReferenceWorld(records);
        var count = 0;
        var recordsWithConditions = 0;
        var selectors = new HashSet<uint>();
        var kinds = new HashSet<string>();
        foreach (var kind in new[] { "INFO", "QUST", "MESG", "PACK", "IDLE", "TERM" })
            foreach (var owner in records.EffectiveRecords(kind))
            {
                var conditions = owner.ReadSubrecords().Where(field => field.Signature == "CTDA")
                    .Select(field => FalloutCondition.Read(owner, field.Data.Span)).Where(value => value.Function == function).ToArray();
                if (conditions.Length == 0) continue;
                var hash = SHA256.HashData(owner.ReadData());
                recordsWithConditions++;
                foreach (var condition in conditions)
                {
                    if (condition.RunOn > 2) continue; // Other run-on owners remain explicitly unadmitted.
                    var form = condition.FormArgument1;
                    world.SetReputation(form, 1, -17.125);
                    world.SetReputation(form, 0, 83.25);
                    var expected = condition.Argument2 == 1 ? -17.125f : 83.25f;
                    if (function == 574)
                    {
                        var capacity = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                            records.GetEffective(form).ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span);
                        expected = (float)((double)expected / capacity);
                        Require(float.IsFinite(expected), "Owned percentage declaration requires a finite result.");
                        Console.WriteLine($"OPENNV_OWNED_REPUTATION_PERCENTAGE_SOURCE owner={owner.FormKey} reputation={form} " +
                            $"selector={condition.Argument2} runOn={condition.RunOn} capacity={capacity} " +
                            $"sourceHash={Convert.ToHexString(hash)}");
                    }
                    if (function == 575)
                    {
                        expected = PrepareThreshold(records, world, form, condition.Argument2);
                        if (count < 3) Console.WriteLine($"OPENNV_OWNED_REPUTATION_THRESHOLD_SOURCE owner={owner.FormKey} reputation={form} " +
                            $"selector={condition.Argument2} runOn={condition.RunOn} sourceHash={Convert.ToHexString(hash)}");
                    }
                    var before = JsonSerializer.Serialize(world.CaptureReputations());
                    Require(world.EvaluateReputationCondition(condition) == expected,
                        "Actual owned CTDA did not query its explicit REPU/selector.");
                    using var cold = new FalloutReferenceWorld(records);
                    cold.RestoreReputations(JsonSerializer.Deserialize<FalloutReputationsSnapshot>(before));
                    Require(cold.EvaluateReputationCondition(condition) == expected &&
                        JsonSerializer.Serialize(world.CaptureReputations()) == before,
                        "Actual owned CTDA changed state or lost its cold axis.");
                    count++; selectors.Add(condition.Argument2); kinds.Add(kind);
                }
                Require(SHA256.HashData(owner.ReadData()).SequenceEqual(hash), "Owned condition record changed.");
            }
        Console.WriteLine($"OPENNV_OWNED_REPUTATION_CONDITION_DISCOVERED function={function} conditions={count} records={recordsWithConditions}");
        Require(count > 0, "No actual owned reputation conditions were exercised.");
        Console.WriteLine($"OPENNV_OWNED_REPUTATION_CONDITION_PASS function={function} conditions={count} records={recordsWithConditions} " +
            $"kinds={string.Join(',', kinds.Order())} selectors={string.Join(',', selectors.Order())} " +
            "actualWinningCTDA=true controlledKnownAxes=true cold=true noMutation=true " +
            "fullRecordEligibilityOrdinaryDialogueQuestAndRetail=unverified");
    }

    private static float PrepareThreshold(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey form, uint selector)
    {
        var one = FalloutGameSettingFloats.Read(records, "fReputationThresholdOne");
        var two = FalloutGameSettingFloats.Read(records, "fReputationThresholdTwo");
        var three = FalloutGameSettingFloats.Read(records, "fReputationThresholdThree");
        Require(one < two && two < three, "Owned audit cannot select interior tier points for this threshold profile.");
        var capacity = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
            records.GetEffective(form).ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span);
        float[] ratios = [one - Math.Max(1, Math.Abs(one)), (one + two) / 2, (two + three) / 2];
        var mode = selector & 255;
        var positive = mode == 1 ? 2 : mode is 0 or 2 ? 1 : 0;
        var negative = mode == 2 ? 2 : mode is 0 or 1 ? 1 : 0;
        world.SetReputation(form, 1, ratios[positive] * capacity);
        world.SetReputation(form, 0, ratios[negative] * capacity);
        // This independent controlled-input oracle uses the source contract's
        // (1,1)->3, (2,1)/(1,2)->2, and prior both-zero->1 cases.
        return mode == 0 ? 3 : mode is 1 or 2 ? 2 : 1;
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (NotSupportedException) { return; }
        throw new InvalidDataException("Unknown owned reputation was invented.");
    }
}
