using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

internal static partial class PlayerActorValueContracts
{
    private static void VerifyStatusValues(FalloutPluginStack records)
    {
        const string executable = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
        var source = FalloutDefaultStatusActorValues.Create(executable, [new(47, "Paralysis"), new(48, "Invisibility")]);
        var effect = 2f;
        var owner = new FalloutPlayerActorValues(records, statusSource: source); Bind(owner);
        Require(owner.StatusValue("pArAlYsIs") == 47 && owner.ReadBase(47) == 0 && owner.ReadCurrent(47) == 2 && owner.ReadCurrent(48) == 0,
            "Source status defaults lost their named identity, effect selection or independent value pool.");
        owner.Change("Paralysis", "SetAV", 1);
        owner.AddModifier(47, FalloutActorValuePool.Temporary, .5f);
        owner.AddModifier(47, FalloutActorValuePool.Permanent, .25f);
        owner.AddModifier(47, FalloutActorValuePool.Damage, -.75f);
        Require(owner.ReadCurrent(47) == 3 && owner.Capture().StatusValues!.Values[47] == new FalloutActorValue(1, .25f, .5f, -.75f),
            "Status current value conflated source effects with serialized mutable pools.");
        effect = 0;
        Require(owner.ReadCurrent(47) == 1, "Status conditional source effect was baked into a mutable pool.");
        var saved = JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(owner.Capture()))!;
        var cold = new FalloutPlayerActorValues(records, saved, source); Bind(cold);
        effect = 2;
        Require(cold.ReadCurrent(47) == 3 && JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(owner.Capture()),
            "Cold status values replayed an effect or changed independent source/mutable pool state.");
        Reject(() => new FalloutPlayerActorValues(records, saved with { StatusValues = null }, source));
        var different = FalloutDefaultStatusActorValues.Create(executable, [new(47, "Paralysis")]);
        Reject(() => new FalloutPlayerActorValues(records, saved, different));
        var damaged = saved.StatusValues! with { Values = new Dictionary<int, FalloutActorValue> { [47] = new(1) } };
        Reject(() => FalloutPlayerActorValues.Validate(saved with { StatusValues = damaged }));
        var before = JsonSerializer.Serialize(owner.Capture());
        Reject(() => owner.WriteBaseInteger(60, 1));
        Reject(() => owner.ReadPermanent(47));
        Require(before == JsonSerializer.Serialize(owner.Capture()), "Unowned status slot/getter changed a completed pool.");
        var callbackSource = FalloutDefaultStatusActorValues.Create(executable, [new(47, "Paralysis", true)]);
        var callbackOwner = new FalloutPlayerActorValues(records, statusSource: callbackSource); Bind(callbackOwner);
        var callbackBefore = JsonSerializer.Serialize(callbackOwner.Capture());
        foreach (var operation in new[] { "SetAV", "ModAV", "ForceAV" })
            Reject(() => callbackOwner.Change("Paralysis", operation, 1));
        Require(callbackBefore == JsonSerializer.Serialize(callbackOwner.Capture()),
            "A getter/default declaration silently admitted an unowned status command callback or partial pool write.");
        effect = 0;
        var rounded = new FalloutPlayerActorValues(records, statusSource: source); Bind(rounded);
        rounded.WriteBaseInteger(47, 16777216);
        rounded.AddModifier(47, FalloutActorValuePool.Temporary, 1);
        rounded.AddModifier(47, FalloutActorValuePool.Permanent, -16777216);
        Require(rounded.ReadCurrent(47) == 0, "Standalone status getter widened its intermediate Float32 pool additions.");
        rounded.WriteBaseInteger(5, 16777216);
        rounded.AddModifier(5, FalloutActorValuePool.Temporary, 1);
        rounded.AddModifier(5, FalloutActorValuePool.Permanent, -16777216);
        Require(rounded.ReadCurrent(5) == 0, "Selected Player Float32 getter was restricted to status values instead of the shared SPECIAL pools.");
        var reads = 0;
        var actual = FalloutActorIdleClassPredicate.Read(() => { ++reads; return .5f; });
        Require(actual is { Result: true, Value.Cached: false } && reads == 1, "Idle status predicate inferred false instead of querying its current value.");
        var cached = FalloutActorIdleClassPredicate.Read(() => throw new InvalidOperationException("Cached predicate queried the current getter."),
            new(0, true));
        Require(!cached.Result && cached.Value.Cached, "Idle cache did not short-circuit its independent source getter.");
        Require(!FalloutActorIdleClassPredicate.Read(() => float.NaN).Result && !FalloutActorIdleClassPredicate.Read(() => -0f).Result,
            "Idle status threshold admitted unordered or signed-zero values as ordered positive.");
        Reject(() => FalloutActorIdleClassPredicate.Read(() => throw new NotSupportedException("current-value-unbound")));
        Console.WriteLine("OPENNV_PLAYER_STATUS_VALUES_CONTRACT_PASS namedDefaults=true separateMutablePools=true conditionalEffects=true float32Order=true cold=true missingStateRefused=true unknownSlotRefused=true unboundCommandCallbackRefused=true idleCurrentGetter=true cacheShortCircuit=true gameplay=UNEXECUTED");

        void Bind(FalloutPlayerActorValues value) => value.BindConstantModifiers((index, pool) => index == 47 && pool == FalloutActorValuePool.Temporary
            ? [new(Key(12), Key(11), index, effect, [], pool)] : []);
    }
}
