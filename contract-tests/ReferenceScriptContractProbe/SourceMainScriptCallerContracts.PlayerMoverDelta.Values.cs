using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void VerifyMoverLoadedValues()
    {
        var defaults = FalloutDefaultStatusActorValues.Create(FalloutSourceMainFamily.Fallout3, [new(47, "Paralysis")]);
        using var fixture = new MoverDeltaFixture();
        var values = new FalloutPlayerActorValues(fixture.Records, statusSource: defaults);
        values.BindConstantModifiers((_, _) => []);
        var source = FalloutPlayerMoverActorValueSource.Read(fixture.Source, fixture.Records);
        var basis = fixture.Records.GetEffective(fixture.Actor.Base);
        values.BindLoadedMoverValues(source, fixture.Records, basis);
        Require(values.ReadCurrent(21) == 100 && values.ReadCurrent(29) == 100 && values.ReadCurrent(30) == 100 &&
            values.ReadMoverDeltaIgnoreCrippling() == 0 && values.Capture().StatusValues!.Values.Count == 1 &&
            values.Capture().MoverValues!.Values.Count == 4,
            "Loaded mover values invented a default speed or shadowed the actual existing modifier maps.");
        Require(fixture.Records.NumericSettings.Set("fAVDDefaultBodyPartCondition", 75) && values.ReadCurrent(29) == 75,
            "A live derived limb getter used a copied GMST instead of its actual mutable setting field.");
        foreach (var name in new[] { "LeftMobilityCondition", "RightMobilityCondition", "IgnoreCrippledLimbs" })
            foreach (var operation in new[] { "SetAV", "ModAV", "ForceAV" }) Reject(() => values.Change(name, operation, 1));
        values.AddModifier(72, FalloutActorValuePool.Temporary, .75f);
        values.AddModifier(72, FalloutActorValuePool.Permanent, .75f);
        values.AddModifier(72, FalloutActorValuePool.Damage, -.75f);
        Require(values.ReadMoverDeltaIgnoreCrippling() == 0,
            "Integer movement getter truncated a prior summed Float32 result instead of its three independent pools.");
        values.WriteBaseInteger(21, 95); values.AddModifier(21, FalloutActorValuePool.Temporary, .5f);
        values.AddModifier(21, FalloutActorValuePool.Permanent, 1.25f); values.AddModifier(21, FalloutActorValuePool.Damage, -.75f);
        Require(values.ReadCurrent(21) == 96, "The current speed getter omitted source Float32 pool stores or its actual base override.");
        var savedValues = JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(values.Capture()))!;
        var restored = new FalloutPlayerActorValues(fixture.Records, savedValues, defaults); restored.BindConstantModifiers((_, _) => []);
        Reject(() => restored.ReadCurrent(29));
        restored.BindLoadedMoverValues(source, fixture.Records, basis);
        Require(restored.ReadCurrent(21) == 96 && restored.ReadCurrent(29) == 75 && restored.ReadMoverDeltaIgnoreCrippling() == 0 &&
            JsonSerializer.Serialize(restored.Capture()) == JsonSerializer.Serialize(values.Capture()),
            "Cold movement values replayed the loaded producer or lost current-GMST rebinding/modifier pools.");

        var actualValues = new FalloutPlayerActorValues(fixture.Records, statusSource: defaults);
        actualValues.BindConstantModifiers((_, _) => []);
        var cache = new FalloutPlayerMoverSpeedCacheState(fixture.Source, fixture.Records, fixture.Stack, fixture.Runtime,
            fixture.Scene.Constructor, fixture.Actor, fixture.RequireLoad, fixture.RequireLife);
        Reject(() => fixture.ReadFrameLeaf(frame => cache.ReadCurrent(frame, fixture.RequireFrame)));
        fixture.DataCall(FalloutReferenceModelDataIngress.ThreeDLoad,
            invocation => cache.PopulateFromLoadedPlayer(invocation, actualValues, basis));
        Require(fixture.ReadFrameLeaf(frame => cache.ReadCurrent(frame, fixture.RequireFrame)).Bits == BitConverter.SingleToUInt32Bits(340),
            "Loaded cache did not execute the actual default speed/limb/settings producer.");
        var savedCache = JsonSerializer.Deserialize<FalloutPlayerMoverSpeedCacheSnapshot>(JsonSerializer.Serialize(cache.Capture()))!;
        var producer = JsonSerializer.Serialize(savedCache.Load);
        cache.Retire();
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            using var cold = new MoverDeltaFixture();
            var rebound = new FalloutPlayerMoverSpeedCacheState(cold.Source, cold.Records, cold.Stack, cold.Runtime,
                cold.Scene.Constructor, cold.Actor, _ => throw new InvalidOperationException("Cold cache replayed the source loaded producer."),
                cold.RequireLife, savedCache);
            Require(cold.ReadFrameLeaf(frame => rebound.ReadCurrent(frame, cold.RequireFrame)).Bits == savedCache.Bits &&
                JsonSerializer.Serialize(rebound.Capture().Load) == producer,
                "Cold current cache used loader zero or reexecuted an old Player load/factory.");
            savedCache = JsonSerializer.Deserialize<FalloutPlayerMoverSpeedCacheSnapshot>(JsonSerializer.Serialize(rebound.Capture()))!;
            rebound.Retire();
        }
        var missing = savedCache.Load! with { RightLeg = null };
        Reject(() => FalloutPlayerMoverSpeedCacheState.Validate(savedCache with { Load = missing }));
        var settings = new Dictionary<string, float> { ["fMoveBaseSpeed"] = 85, ["fMoveRunMult"] = 4,
            ["fMoveWeightMin"] = 0, ["fMoveWeightMax"] = 150, ["fMoveEncumEffect"] = .4f,
            ["fMoveOneCrippledLegSpeedMult"] = .85f, ["fMoveTwoCrippledLegsSpeedMult"] = .75f };
        Require(FalloutPlayerMoverSpeedCacheState.LoadedSpeed(0, 0, 0, 100, name => settings[name]) == 255 &&
            FalloutPlayerMoverSpeedCacheState.LoadedSpeed(0, 0, 1, 100, name => settings[name]) == 340,
            "Source limb/ignore branches lost their actual float multiplier or integer predicate.");
    }
}
