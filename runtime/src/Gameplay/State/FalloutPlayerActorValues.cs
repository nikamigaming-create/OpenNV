using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerActorValuesSnapshot(string Schema, uint Reference, FalloutFormKey Player,
    string PlayerWinner, string PlayerSha256, FalloutFormKey StatsOwner, string StatsWinner, string StatsSha256,
    IReadOnlyDictionary<int, FalloutActorValue> Values,
    FalloutFormKey? AiOwner = null, string? AiWinner = null, string? AiSha256 = null, FalloutKarmaSource? KarmaSource = null);

// Mutable engine-created player values. Source constant effects are evaluated
// into their evidenced pools on demand; they are never saved a second time.
internal sealed class FalloutPlayerActorValues
{
    internal const string SnapshotSchema = "opennv-player-actor-values/v4";
    internal const string BeforeFatigueSchema = "opennv-player-actor-values/v3";
    internal const string BeforeKarmaSchema = "opennv-player-actor-values/v2";
    internal const string SpecialOnlySchema = "opennv-player-special-values/v1";
    internal const uint PlayerReference = 0x14;
    private readonly FalloutPlayerActorValueSource _source;
    private readonly FalloutPluginStack _records;
    private FalloutActorAiData? _aiData;
    private FalloutKarmaSource? _karmaSource;
    private string? _karmaRewardFailure;
    private Dictionary<int, FalloutActorValue> _values;
    private Func<int, FalloutActorValuePool, IReadOnlyList<FalloutAbilityModifier>>? _constantModifiers;
    private readonly HashSet<int> _evaluating = [];
    private Action? _abilityLifecycle;
    private Func<int>? _fatigueLevel;
    internal event Action? Changed;
    internal FalloutPlayerActorValueSource Source => _source;

    internal FalloutPlayerActorValues(FalloutPluginStack records, FalloutPlayerActorValuesSnapshot? restore = null,
        FalloutNativeSpecialState? legacy = null)
    {
        if (restore is not null && legacy is not null)
            throw new InvalidDataException("Player values have two competing restore authorities.");
        _source = FalloutPlayerActorValueSource.Read(records);
        _records = records;
        var initial = legacy?.Values ?? _source.Special;
        _values = Enumerable.Range(5, 7).ToDictionary(value => value, value => new FalloutActorValue(initial[value - 5]));
        _values.Add(21, new(_source.SpeedMultiplier));
        _values.Add(72, new(0));
        if (restore is not null) Restore(restore);
        else if (legacy is null)
        {
            _karmaSource = FalloutKarmaSource.Read(records, _source.Player);
            _values.Add(23, new(_karmaSource.Initial));
        }
    }

    internal void BindConstantModifiers(Func<int, FalloutActorValuePool, IReadOnlyList<FalloutAbilityModifier>> modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        if (_constantModifiers is not null) throw new InvalidOperationException("Player constant modifier owner is already bound.");
        _constantModifiers = modifiers;
    }

    internal void BindAbilityLifecycle(Action synchronize)
    {
        ArgumentNullException.ThrowIfNull(synchronize);
        if (_abilityLifecycle is not null) throw new InvalidOperationException("Player active-effect lifecycle is already bound.");
        _abilityLifecycle = synchronize;
    }

    internal void BindFatigueLevel(Func<int> read) => _fatigueLevel = read;

    internal static int SpecialValue(string name)
    {
        var slot = FalloutNativeVigorResolver.AttributeNames.ToList().FindIndex(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return slot >= 0 ? slot + 5 : throw new NotSupportedException($"Player actor value {name} has no SPECIAL pool owner.");
    }

    internal static bool HasPool(string name) => name.Equals("SpeedMult", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Fatigue", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Karma", StringComparison.OrdinalIgnoreCase) ||
        FalloutActorValueNames.TryParse(name, out var id) && FalloutActorAiData.Contains(id) ||
        name.Equals("IgnoreCrippledLimbs", StringComparison.OrdinalIgnoreCase) ||
        FalloutNativeVigorResolver.AttributeNames.Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    internal static int PoolValue(string name) => name.ToLowerInvariant() switch
    {
        "speedmult" => 21,
        "fatigue" => 22,
        "karma" => 23,
        "ignorecrippledlimbs" => 72,
        _ => FalloutActorValueNames.TryParse(name, out var id) && FalloutActorAiData.Contains(id) ? (int)id : SpecialValue(name),
    };
    internal static bool HasPool(int value) => value is >= 5 and <= 11 or 21 or 22 or 23 or 72 || FalloutActorAiData.Contains((FalloutActorValueId)value);

    private FalloutActorValue Value(int actorValue)
    {
        if (actorValue == 22)
        {
            var basis = FalloutActorFatigueSource.Player(_records, Math.Clamp(ReadPermanent(7), 1, 10), _fatigueLevel?.Invoke() ?? _source.Level);
            var fatigue = _values.GetValueOrDefault(22, new(basis)) with { Base = basis };
            _values[22] = fatigue;
            return fatigue;
        }
        if (_values.TryGetValue(actorValue, out var value)) return value;
        if (actorValue == 23) throw new NotSupportedException("Restored player Karma has no historical value receipt; source initialization cannot reconstruct past awards.");
        if (!FalloutActorAiData.Contains((FalloutActorValueId)actorValue))
            throw new NotSupportedException($"Player actor value {actorValue} has no base/formula pool owner.");
        _aiData ??= FalloutActorAiData.Read(_records, _source.Player);
        _values.Add(actorValue, value = new(_aiData.Value((FalloutActorValueId)actorValue)));
        return value;
    }

    internal float ReadBase(int actorValue) => Value(actorValue).Base;
    internal float ReadPermanent(int actorValue) => Evaluate(actorValue, () =>
    {
        var value = ReadBase(actorValue) + Pool(actorValue, FalloutActorValuePool.Permanent);
        return actorValue is >= 5 and <= 11 ? Math.Clamp(value, 1, 10) : value;
    });
    internal float ReadCurrent(int actorValue) => Evaluate(actorValue, () =>
        // The native float getter sums temporary, permanent and damage pools
        // with a wider intermediate before returning its Float32 result.
        (float)((double)ReadBase(actorValue) + Pool(actorValue, FalloutActorValuePool.Temporary) +
            Pool(actorValue, FalloutActorValuePool.Permanent) + Pool(actorValue, FalloutActorValuePool.Damage)));
    internal float ReadBoundedCurrent(int actorValue) => Math.Clamp(ReadCurrent(actorValue), 1, 10);
    internal float Read(int actorValue, FalloutActorValueRead kind) => kind switch
    {
        FalloutActorValueRead.Base => ReadBase(actorValue),
        FalloutActorValueRead.Permanent => ReadPermanent(actorValue),
        FalloutActorValueRead.Current => ReadCurrent(actorValue),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal FalloutNativeSpecialState BaseSpecial => new(BaseInteger(5), BaseInteger(6), BaseInteger(7),
        BaseInteger(8), BaseInteger(9), BaseInteger(10), BaseInteger(11));
    private int BaseInteger(int actorValue)
    {
        var value = ReadBase(actorValue);
        // The signed integer setter converts to native Float32 storage. The
        // upper integer boundary rounds to 2^31; retain that authoritative
        // value while keeping the older integer view representable.
        if (value == 2147483648f) return int.MaxValue;
        if (value < int.MinValue || (double)value > int.MaxValue)
            throw new NotSupportedException("Player base value exceeds the legacy seven-integer view.");
        return checked((int)value);
    }

    internal FalloutSpecialAllocationBinding AllocationBinding => new("engine-player-special-pools", ReadPermanent, WriteBaseInteger);

    internal void WriteBaseInteger(int actorValue, int value)
    {
        if (actorValue == 22) throw new NotSupportedException("Fatigue BASE writes require their derived-value override owner.");
        if (actorValue == 23) throw new NotSupportedException("Karma has no direct BASE-write owner; rewards change its permanent modifier.");
        Publish(actorValue, Value(actorValue) with { Base = value });
    }

    internal void AddModifier(int actorValue, FalloutActorValuePool pool, float amount)
    {
        if (!float.IsFinite(amount)) throw new InvalidDataException("Player modifier is non-finite.");
        var previous = Value(actorValue);
        var changed = pool switch
        {
            FalloutActorValuePool.Permanent => previous with { Permanent = previous.Permanent + amount },
            FalloutActorValuePool.Temporary => previous with { Temporary = previous.Temporary + amount },
            FalloutActorValuePool.Damage => previous with { Damage = previous.Damage + amount },
            _ => throw new ArgumentOutOfRangeException(nameof(pool)),
        };
        Publish(actorValue, changed);
    }

    internal void Change(string name, string operation, double argument)
    {
        var value = (float)FalloutActorValueCommands.Argument(operation, argument);
        var actorValue = PoolValue(name);
        var kind = FalloutActorValueCommands.Operation(operation) ?? throw new NotSupportedException($"Player value command {operation} is unbound.");
        if (actorValue == 22 && kind == FalloutActorValueOperation.SetBase)
            throw new NotSupportedException("Fatigue BASE writes require their derived-value override owner.");
        // Source Karma is non-modifiable by the ordinary script AV commands.
        if (actorValue == 23) return;
        var current = kind == FalloutActorValueOperation.ForceCurrent ? ReadCurrent(actorValue) : Value(actorValue).Current;
        Publish(actorValue, actorValue == 22 && kind == FalloutActorValueOperation.Damage
            ? FalloutFatigue.Damage(_records, Value(actorValue), value, ReadCurrent(actorValue))
            : FalloutActorValueCommands.Apply(Value(actorValue), kind, value, current));
    }

    internal Action PrepareTouchEnchantment(FalloutIngestible source, Func<FalloutCondition, float> condition, bool areaDelivered = false)
    {
        var before = new Dictionary<int, FalloutActorValue>();
        FalloutActorValue Read(int value)
        {
            var pool = Value(value);
            before.TryAdd(value, pool);
            return pool;
        }
        var after = FalloutInstantTouchEffects.Prepare(_records, source, Read, condition, ReadCurrent, areaDelivered);
        return () =>
        {
            if (before.Any(pair => Value(pair.Key) != pair.Value))
                throw new InvalidOperationException("Delivered player enchantment has a stale value baseline.");
            foreach (var (value, pool) in after) _values[value] = pool;
            if (after.Count != 0) Changed?.Invoke();
        };
    }

    internal void AdvanceFatigue(double seconds)
    {
        if (!_values.ContainsKey(22)) return;
        var previous = Value(22);
        var after = FalloutFatigue.Advance(_records, previous, seconds);
        if (after != previous) Publish(22, after);
    }

    internal static int SignedInteger(double value)
    {
        return FalloutActorValueCommands.Integer(value);
    }

    private void Publish(int actorValue, FalloutActorValue value)
    {
        if (!value.IsFinite) throw new InvalidDataException("Player value exceeds finite Float32 storage.");
        _values[actorValue] = value;
        Changed?.Invoke();
    }

    private float Pool(int actorValue, FalloutActorValuePool pool)
    {
        var value = Value(actorValue);
        var result = pool switch
        {
            FalloutActorValuePool.Permanent => value.Permanent,
            FalloutActorValuePool.Temporary => value.Temporary,
            FalloutActorValuePool.Damage => value.Damage,
            _ => throw new ArgumentOutOfRangeException(nameof(pool)),
        };
        foreach (var modifier in (_constantModifiers ?? throw new NotSupportedException("Player constant modifiers have no source evaluator."))(actorValue, pool))
        {
            if (modifier.ActorValue != actorValue || modifier.Pool != pool || !float.IsFinite(modifier.Amount))
                throw new InvalidDataException("Source player modifier differs from its declared pool.");
            result += modifier.Amount;
        }
        return float.IsFinite(result) ? result : throw new InvalidDataException("Player modifier pool exceeds finite storage.");
    }

    private float Evaluate(int actorValue, Func<float> read)
    {
        _abilityLifecycle?.Invoke();
        _ = Value(actorValue);
        if (!_evaluating.Add(actorValue)) throw new NotSupportedException("Player ability conditions have a recursive actor value dependency.");
        try
        {
            var result = read();
            return float.IsFinite(result) ? result : throw new InvalidDataException("Player value result is non-finite.");
        }
        finally { _evaluating.Remove(actorValue); }
    }

    internal int KarmaInteger()
    {
        var result = 0;
        _ = Evaluate(23, () =>
        {
            result = unchecked(SignedInteger(Math.Floor(ReadBase(23))) +
                SignedInteger(Pool(23, FalloutActorValuePool.Temporary)) +
                SignedInteger(Pool(23, FalloutActorValuePool.Permanent)) +
                SignedInteger(Pool(23, FalloutActorValuePool.Damage)));
            return 0;
        });
        return result;
    }

    internal FalloutActorValue PrepareKarmaReward(int amount)
    {
        var previous = Value(23);
        var next = previous with { Permanent = previous.Permanent + (float)amount };
        if (!next.IsFinite) throw new InvalidDataException("Karma reward exceeds finite actor-value storage.");
        return next;
    }
    internal void CommitKarmaReward(FalloutActorValue next) => Publish(23, next);
    internal void FailKarmaReward(Exception error) => _karmaRewardFailure ??= error.Message;

    internal FalloutPlayerActorValuesSnapshot Capture()
    {
        if (_karmaRewardFailure is not null) throw new NotSupportedException("Karma reward has a failed committed source prefix: " + _karmaRewardFailure);
        return new(SnapshotSchema, PlayerReference,
        _source.Player, _source.PlayerWinner, _source.PlayerSha256, _source.StatsOwner, _source.StatsWinner, _source.StatsSha256,
        new Dictionary<int, FalloutActorValue>(_values), _aiData?.Owner, _aiData?.Winner, _aiData?.Sha256, _karmaSource);
    }

    internal void Restore(FalloutPlayerActorValuesSnapshot snapshot)
    {
        Validate(snapshot);
        if (snapshot.Player != _source.Player || snapshot.PlayerWinner != _source.PlayerWinner || snapshot.PlayerSha256 != _source.PlayerSha256 ||
            snapshot.StatsOwner != _source.StatsOwner || snapshot.StatsWinner != _source.StatsWinner || snapshot.StatsSha256 != _source.StatsSha256)
            throw new InvalidDataException("Saved player values differ from their winning source identity.");
        var replacement = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        FalloutKarmaSource? replacementKarma = null;
        if (snapshot.KarmaSource is { } karma)
        {
            var actual = FalloutKarmaSource.Read(_records, _source.Player);
            if (karma != actual) throw new InvalidDataException("Saved Karma differs from its source traits owner.");
            replacementKarma = actual;
        }

        if (snapshot.AiOwner is { } aiOwner)
        {
            var source = FalloutActorAiData.Read(_records, _source.Player);
            if (source.Owner != aiOwner || source.Winner != snapshot.AiWinner || source.Sha256 != snapshot.AiSha256)
                throw new InvalidDataException("Saved player AI-data values differ from their source owner.");
            _aiData = source;
        }
        else _aiData = null;
        if (snapshot.Schema == SpecialOnlySchema)
        {
            replacement.Add(21, new(_source.SpeedMultiplier));
            replacement.Add(72, new(0));
        }
        _karmaSource = replacementKarma;
        _values = replacement;
        Changed?.Invoke();
    }

    internal static void Validate(FalloutPlayerActorValuesSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Schema is not (SnapshotSchema or BeforeFatigueSchema or BeforeKarmaSchema or SpecialOnlySchema) || snapshot.Reference != PlayerReference || snapshot.Player.ObjectId == 0 ||
            snapshot.Schema != SnapshotSchema && snapshot.Values?.ContainsKey(22) == true ||
            snapshot.StatsOwner.ObjectId == 0 || string.IsNullOrWhiteSpace(snapshot.PlayerWinner) || string.IsNullOrWhiteSpace(snapshot.StatsWinner) ||
            !Hash(snapshot.PlayerSha256) || !Hash(snapshot.StatsSha256) || snapshot.Values is null ||
            (snapshot.Schema == SpecialOnlySchema ? snapshot.Values.Count != 7 :
                Enumerable.Range(5, 7).Concat([21, 72]).Any(id => !snapshot.Values.ContainsKey(id))) ||
            snapshot.Values.Keys.Any(id => FalloutActorAiData.Contains((FalloutActorValueId)id)) && snapshot.AiOwner is null ||
            snapshot.AiOwner is not null && !snapshot.Values.Keys.Any(id => FalloutActorAiData.Contains((FalloutActorValueId)id)) ||
            snapshot.AiOwner is not null && (string.IsNullOrWhiteSpace(snapshot.AiWinner) || !Hash(snapshot.AiSha256!)) ||
            snapshot.AiOwner is null && (snapshot.AiWinner is not null || snapshot.AiSha256 is not null) ||
            snapshot.Values.ContainsKey(23) != (snapshot.KarmaSource is not null) ||
            snapshot.KarmaSource is { } karma && (snapshot.Schema is not (SnapshotSchema or BeforeFatigueSchema) || karma.Owner.ObjectId == 0 ||
                string.IsNullOrWhiteSpace(karma.Winner) || !Hash(karma.Sha256) || snapshot.Values[23] is not { } karmaValue || karmaValue.Base != (float)karma.Initial) ||
            snapshot.Values.Any(pair => (snapshot.Schema == SpecialOnlySchema ? pair.Key is < 5 or > 11 : !HasPool(pair.Key)) || pair.Value is null || !pair.Value.IsFinite ||
                pair.Key != 22 && pair.Value.Base != MathF.Truncate(pair.Value.Base) || pair.Value.Base < int.MinValue || (double)pair.Value.Base > 2147483648d))
            throw new InvalidDataException("Saved player actor-value pools are invalid.");
        static bool Hash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
