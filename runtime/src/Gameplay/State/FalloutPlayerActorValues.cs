using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerActorValuesSnapshot(string Schema, uint Reference, FalloutFormKey Player,
    string PlayerWinner, string PlayerSha256, FalloutFormKey StatsOwner, string StatsWinner, string StatsSha256,
    IReadOnlyDictionary<int, FalloutActorValue> Values);

// Mutable engine-created player values. Source constant effects are evaluated
// into their evidenced pools on demand; they are never saved a second time.
internal sealed class FalloutPlayerActorValues
{
    internal const string SnapshotSchema = "opennv-player-special-values/v1";
    internal const uint PlayerReference = 0x14;
    private readonly FalloutPlayerActorValueSource _source;
    private Dictionary<int, FalloutActorValue> _values;
    private Func<int, FalloutActorValuePool, IReadOnlyList<FalloutAbilityModifier>>? _constantModifiers;
    private readonly HashSet<int> _evaluating = [];
    internal event Action? Changed;
    internal FalloutPlayerActorValueSource Source => _source;

    internal FalloutPlayerActorValues(FalloutPluginStack records, FalloutPlayerActorValuesSnapshot? restore = null,
        FalloutNativeSpecialState? legacy = null)
    {
        if (restore is not null && legacy is not null)
            throw new InvalidDataException("Player values have two competing restore authorities.");
        _source = FalloutPlayerActorValueSource.Read(records);
        var initial = legacy?.Values ?? _source.Special;
        _values = Enumerable.Range(5, 7).ToDictionary(value => value, value => new FalloutActorValue(initial[value - 5]));
        if (restore is not null) Restore(restore);
    }

    internal void BindConstantModifiers(Func<int, FalloutActorValuePool, IReadOnlyList<FalloutAbilityModifier>> modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        if (_constantModifiers is not null) throw new InvalidOperationException("Player constant modifier owner is already bound.");
        _constantModifiers = modifiers;
    }

    internal static int SpecialValue(string name)
    {
        var slot = FalloutNativeVigorResolver.AttributeNames.ToList().FindIndex(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return slot >= 0 ? slot + 5 : throw new NotSupportedException($"Player actor value {name} has no SPECIAL pool owner.");
    }

    private FalloutActorValue Value(int actorValue) => _values.TryGetValue(actorValue, out var value) ? value :
        throw new NotSupportedException($"Player actor value {actorValue} has no base/formula pool owner.");

    internal float ReadBase(int actorValue) => Value(actorValue).Base;
    internal float ReadPermanent(int actorValue) => Evaluate(actorValue, () => Math.Clamp(ReadBase(actorValue) +
        Pool(actorValue, FalloutActorValuePool.Permanent), 1, 10));
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

    internal void WriteBaseInteger(int actorValue, int value) => Publish(actorValue, Value(actorValue) with { Base = value });

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
        var value = SignedInteger(argument);
        var actorValue = SpecialValue(name);
        switch (operation.ToLowerInvariant())
        {
            case "setav" or "setactorvalue": WriteBaseInteger(actorValue, value); break;
            case "modav" or "modactorvalue": AddModifier(actorValue, FalloutActorValuePool.Permanent, value); break;
            case "forceav" or "forceactorvalue":
                AddModifier(actorValue, FalloutActorValuePool.Permanent, value - ReadCurrent(actorValue)); break;
            default: throw new NotSupportedException($"Player value command {operation} is unbound.");
        }
    }

    internal static int SignedInteger(double value)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException("Actor value command argument exceeds signed integer storage.");
        if (value != Math.Truncate(value))
            throw new NotSupportedException("Fractional actor value command integer coercion is unbound.");
        return checked((int)value);
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
        _ = Value(actorValue);
        if (!_evaluating.Add(actorValue)) throw new NotSupportedException("Player ability conditions have a recursive actor value dependency.");
        try
        {
            var result = read();
            return float.IsFinite(result) ? result : throw new InvalidDataException("Player value result is non-finite.");
        }
        finally { _evaluating.Remove(actorValue); }
    }

    internal FalloutPlayerActorValuesSnapshot Capture() => new(SnapshotSchema, PlayerReference,
        _source.Player, _source.PlayerWinner, _source.PlayerSha256, _source.StatsOwner, _source.StatsWinner, _source.StatsSha256,
        new Dictionary<int, FalloutActorValue>(_values));

    internal void Restore(FalloutPlayerActorValuesSnapshot snapshot)
    {
        Validate(snapshot);
        if (snapshot.Player != _source.Player || snapshot.PlayerWinner != _source.PlayerWinner || snapshot.PlayerSha256 != _source.PlayerSha256 ||
            snapshot.StatsOwner != _source.StatsOwner || snapshot.StatsWinner != _source.StatsWinner || snapshot.StatsSha256 != _source.StatsSha256)
            throw new InvalidDataException("Saved player values differ from their winning source identity.");
        var replacement = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        _values = replacement;
        Changed?.Invoke();
    }

    internal static void Validate(FalloutPlayerActorValuesSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Schema != SnapshotSchema || snapshot.Reference != PlayerReference || snapshot.Player.ObjectId == 0 ||
            snapshot.StatsOwner.ObjectId == 0 || string.IsNullOrWhiteSpace(snapshot.PlayerWinner) || string.IsNullOrWhiteSpace(snapshot.StatsWinner) ||
            !Hash(snapshot.PlayerSha256) || !Hash(snapshot.StatsSha256) || snapshot.Values is null || snapshot.Values.Count != 7 ||
            snapshot.Values.Any(pair => pair.Key is < 5 or > 11 || pair.Value is null || !pair.Value.IsFinite ||
                pair.Value.Base != MathF.Truncate(pair.Value.Base) || pair.Value.Base < int.MinValue || (double)pair.Value.Base > 2147483648d))
            throw new InvalidDataException("Saved player SPECIAL pools are invalid.");
        static bool Hash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
