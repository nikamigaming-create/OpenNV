using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

// Skill BASE is a live source formula plus the player's stored offset. Source
// abilities remain in their own pools and are not baked into this snapshot.
internal sealed record FalloutPlayerSkillPools(float BaseOffset = 0, float Permanent = 0)
{
    internal bool IsFinite => float.IsFinite(BaseOffset) && float.IsFinite(Permanent);
}

internal sealed record FalloutPlayerSkillSource(int ActorValue, FalloutFormKey Form, string Winner, string Sha256);
internal sealed record FalloutPlayerSkillValuesSnapshot(string Schema, uint Reference,
    FalloutFormKey Player, string PlayerWinner, string PlayerSha256,
    FalloutFormKey StatsOwner, string StatsWinner, string StatsSha256,
    IReadOnlyDictionary<int, FalloutPlayerSkillPools> Pools, IReadOnlyList<FalloutPlayerSkillSource> Sources);

internal sealed partial class FalloutPlayerSkills
{
    internal const string ValuesSchema = "opennv-player-skill-values/v1";
    private Dictionary<int, FalloutPlayerSkillPools> _skillPools = [];
    private FalloutPlayerActorValueSource? _skillPlayerSource;
    internal event Action? Changed;
    internal IReadOnlyList<int> SkillOrder => _skills.Select(skill => skill.Value).ToArray();

    internal bool IsTaggedSkill(int value)
    {
        RequireSkill(value);
        return _tagged(_skills.Single(skill => skill.Value == value).Name);
    }

    internal bool IsSkill(string name) => SkillSlot(name) is not null;
    private int? SkillSlot(string name)
    {
        // SmallGuns is the immutable engine AV name even when the selected
        // AVIF presents that slot as Guns. This is an API alias, not content.
        if (name.Equals("SmallGuns", StringComparison.OrdinalIgnoreCase) || name.Equals("Guns", StringComparison.OrdinalIgnoreCase)) return 41;
        var skill = _skills.SingleOrDefault(skill => skill.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return skill.Name is null ? null : skill.Value;
    }
    private int RequireSkill(string name) => SkillSlot(name) ??
        throw new NotSupportedException($"Player skill {name} is not declared by the selected skill catalog.");
    private void RequireSkill(int value)
    {
        if (!_skills.Any(skill => skill.Value == value))
            throw new NotSupportedException($"Player skill slot {value} is not declared by the selected skill catalog.");
    }

    private float SkillFormula(int value)
    {
        var skill = _skills.Single(skill => skill.Value == value);
        var result = Setting("fAVDSkill" + skill.Setting + "Base") +
            MathF.Floor(Setting("fAVDSkillPrimaryBonusMult") * Value(skill.Attribute)) +
            MathF.Ceiling(Setting("fAVDSkillLuckBonusMult") * Value(11)) +
            (_tagged(skill.Name) ? Setting("fAVDTagSkillBonus") : 0);
        return float.IsFinite(result) ? result : throw new InvalidDataException("Player skill formula exceeds finite storage.");
    }

    internal float ReadSkill(string name, FalloutActorValueRead kind) => ReadSkill(RequireSkill(name), kind);
    internal float ReadSkill(int value, FalloutActorValueRead kind)
    {
        RequireSkill(value);
        if (!_evaluating.Add(value)) throw new NotSupportedException("Player skill conditions have a recursive value dependency.");
        try
        {
            var pools = _skillPools.GetValueOrDefault(value) ?? new();
            var basis = SkillFormula(value) + pools.BaseOffset;
            if (!float.IsFinite(basis)) throw new InvalidDataException("Player skill BASE exceeds finite storage.");
            if (kind == FalloutActorValueRead.Base) return basis;
            var permanent = Pool(FalloutActorValuePool.Permanent, pools.Permanent);
            if (kind == FalloutActorValueRead.Permanent) return Finite((double)basis + permanent);
            if (kind != FalloutActorValueRead.Current) throw new ArgumentOutOfRangeException(nameof(kind));
            return Finite((double)basis + permanent + Pool(FalloutActorValuePool.Temporary, 0));

            float Pool(FalloutActorValuePool pool, float initial)
            {
                foreach (var effect in Modifiers(value, pool)) initial += effect.Amount;
                return float.IsFinite(initial) ? initial : throw new InvalidDataException("Player skill modifier pool exceeds finite storage.");
            }
        }
        finally { _evaluating.Remove(value); }
        static float Finite(double number) => float.IsFinite((float)number) ? (float)number :
            throw new InvalidDataException("Player skill result exceeds finite Float32 storage.");
    }

    internal float ReadUnmodifiedSkill(int value) => ReadSkill(value, FalloutActorValueRead.Base);
    internal void WriteUnmodifiedSkill(int value, float requested)
    {
        RequireSkill(value);
        if (!float.IsFinite(requested)) throw new InvalidDataException("Player skill BASE write is non-finite.");
        var formula = SkillFormula(value);
        var offset = requested - formula;
        if (!float.IsFinite(offset) || formula + offset != requested)
            throw new NotSupportedException("Player skill BASE cannot represent the requested value in its source formula pool.");
        var current = _skillPools.GetValueOrDefault(value) ?? new();
        Publish(value, current with { BaseOffset = offset });
    }

    internal void ChangeSkill(string name, string operation, double argument)
    {
        var operand = FalloutPlayerActorValues.SignedInteger(argument);
        var value = RequireSkill(name);
        var before = _skillPools.GetValueOrDefault(value) ?? new();
        switch (operation.ToLowerInvariant())
        {
            case "setav" or "setactorvalue": WriteUnmodifiedSkill(value, operand); return;
            case "modav" or "modactorvalue": Publish(value, before with { Permanent = before.Permanent + operand }); return;
            case "forceav" or "forceactorvalue":
                var difference = operand - ReadSkill(value, FalloutActorValueRead.Current);
                Publish(value, before with { Permanent = before.Permanent + difference }); return;
            default: throw new NotSupportedException($"Player skill command {operation} has no pool owner.");
        }
    }

    private void Publish(int value, FalloutPlayerSkillPools changed)
    {
        if (!changed.IsFinite || !float.IsFinite((float)((double)SkillFormula(value) + changed.BaseOffset +
                changed.Permanent)))
            throw new InvalidDataException("Player skill pools exceed finite Float32 storage.");
        _skillPools[value] = changed;
        Changed?.Invoke();
    }

    internal FalloutPlayerSkillValuesSnapshot CaptureValues()
    {
        var source = _skillPlayerSource ??= FalloutPlayerActorValueSource.Read(_records);
        return new(ValuesSchema, FalloutPlayerActorValues.PlayerReference, source.Player, source.PlayerWinner,
            source.PlayerSha256, source.StatsOwner, source.StatsWinner, source.StatsSha256,
            new Dictionary<int, FalloutPlayerSkillPools>(_skillPools), Sources(_skillPools.Keys));
    }

    internal void RestoreValues(FalloutPlayerSkillValuesSnapshot snapshot)
    {
        ValidateValues(snapshot);
        var source = _skillPlayerSource ??= FalloutPlayerActorValueSource.Read(_records);
        if (snapshot.Player != source.Player || snapshot.PlayerWinner != source.PlayerWinner || snapshot.PlayerSha256 != source.PlayerSha256 ||
            snapshot.StatsOwner != source.StatsOwner || snapshot.StatsWinner != source.StatsWinner || snapshot.StatsSha256 != source.StatsSha256 ||
            !Sources(snapshot.Pools.Keys).SequenceEqual(snapshot.Sources))
            throw new InvalidDataException("Saved player skills differ from their winning player/stats/AVIF sources.");
        foreach (var value in snapshot.Pools.Keys) RequireSkill(value);
        var replacement = snapshot.Pools.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (value, pools) in replacement)
            if (!float.IsFinite((float)((double)SkillFormula(value) + pools.BaseOffset + pools.Permanent)))
                throw new InvalidDataException("Saved player skill formula/pools exceed finite storage.");
        _skillPools = replacement;
        Changed?.Invoke();
    }

    private FalloutPlayerSkillSource[] Sources(IEnumerable<int> values)
    {
        var wanted = values.Order().ToArray();
        if (wanted.Length == 0) return [];
        var catalog = FalloutNativeTagSkillResolver.ResolveSkills(_records);
        return wanted.Select(value =>
        {
            RequireSkill(value);
            var name = _skills.Single(skill => skill.Value == value).Name;
            var identity = catalog.Single(skill => FalloutNativeTagSkillResolver.ActorValueName(_records, skill).Equals(name, StringComparison.OrdinalIgnoreCase));
            var source = _records.GetEffective(_records.RuntimeFormKey(identity.RuntimeFormId));
            return new FalloutPlayerSkillSource(value, source.FormKey, source.Plugin.Name,
                Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant());
        }).ToArray();
    }

    internal static void ValidateValues(FalloutPlayerSkillValuesSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Schema != ValuesSchema || state.Reference != FalloutPlayerActorValues.PlayerReference ||
            state.Player.ObjectId == 0 || state.StatsOwner.ObjectId == 0 ||
            string.IsNullOrWhiteSpace(state.PlayerWinner) || string.IsNullOrWhiteSpace(state.StatsWinner) ||
            !Hash(state.PlayerSha256) || !Hash(state.StatsSha256) || state.Pools is null || state.Pools.Count > 13 ||
            state.Pools.Any(pair => pair.Key is < 32 or > 45 || pair.Value is null || !pair.Value.IsFinite) || state.Sources is null ||
            state.Sources.Any(source => source is null || source.Form.ObjectId == 0 || string.IsNullOrWhiteSpace(source.Winner) || !Hash(source.Sha256)) ||
            !state.Pools.Keys.Order().SequenceEqual(state.Sources.Select(source => source.ActorValue)) ||
            state.Sources.Select(source => source.Form).Distinct().Count() != state.Sources.Count)
            throw new InvalidDataException("Saved player skill pools or source identities are invalid.");
        static bool Hash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
