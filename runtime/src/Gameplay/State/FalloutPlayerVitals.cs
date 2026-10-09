using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerVitals
{
    private readonly double _baseHealth;
    private readonly double _healthEndurance, _healthLevel, _apBase, _apAgility, _xpBase, _xpBump;
    private readonly int _initialLevel;
    private readonly FalloutPlayerActorValues? _actorValues;
    private GameplayVitals _state = null!;
    internal GameplayVitals State
    {
        get
        {
            if (_actorValues is not null) SetDerived(GameplayVitals.Derive(_baseHealth, _state.Level,
                _actorValues.ReadPermanent(7), _actorValues.ReadBoundedCurrent(10), _healthEndurance, _healthLevel,
                _apBase, _apAgility, _state.ExperiencePoints, _xpBase, _xpBump));
            return _state;
        }
    }
    internal void Damage(float amount) => _state = State.Damage(amount);
    internal void Damage(float amount, byte part, float limbMultiplier) => _state = State.Damage(amount, part, limbMultiplier);
    internal void ResetHealth()
    {
        var current = State;
        if (current.ExactHitPoints == 0)
            throw new NotSupportedException("ResetHealth cannot substitute for the player resurrection owner.");
        Publish(current with { HitPoints = current.MaximumHitPoints, HitPointFraction = 0, LimbDamage = null });
    }
    internal void Publish(GameplayVitals state) { state.Validate(); _state = state; }
    internal int ExperienceThreshold(int level)
    {
        if (level < 1) throw new InvalidDataException("Player XP threshold requires a positive level.");
        if (level == 1) return 0;
        var value = (level - 1) * ((level - 2) * _xpBump / 2 + _xpBase);
        if (!double.IsFinite(value) || value < 0 || value > int.MaxValue || value != Math.Truncate(value))
            throw new NotSupportedException("Source XP threshold exceeds admitted integral player storage.");
        return (int)value;
    }

    internal FalloutPlayerVitals(FalloutPluginStack records, FalloutFormKey player, FalloutNativeSpecialState special,
        GameplayVitals? restore = null) : this(records, player)
    {
        _state = restore ?? Derive(special, _initialLevel, 0);
        _state.Validate();
    }

    private FalloutPlayerVitals(FalloutPluginStack records, FalloutFormKey player)
    {
        var fields = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(player), 2).ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data;
        var actor = fields.Single(field => field.Signature == "ACBS").Data;
        if (data.Length != 11 || actor.Length != 24) throw new NotSupportedException("Player base-stat layout is unbound.");
        _baseHealth = BinaryPrimitives.ReadInt32LittleEndian(data.Span);
        _initialLevel = FalloutActorLevel.Resolve(actor.Span, null);
        _healthEndurance = FalloutGameSettingFloats.ReadRetained(records, "fAVDHealthEnduranceMult", nameof(FalloutPlayerVitals));
        _healthLevel = FalloutGameSettingFloats.ReadRetained(records, "fAVDHealthLevelMult", nameof(FalloutPlayerVitals));
        _apBase = FalloutGameSettingFloats.ReadRetained(records, "fAVDActionPointsBase", nameof(FalloutPlayerVitals));
        _apAgility = FalloutGameSettingFloats.ReadRetained(records, "fAVDActionPointsMult", nameof(FalloutPlayerVitals));
        _xpBase = FalloutGameSettingIntegers.ReadRetained(records, "iXPBase", nameof(FalloutPlayerVitals));
        _xpBump = FalloutGameSettingIntegers.ReadRetained(records, "iXPBumpBase", nameof(FalloutPlayerVitals));
    }

    internal static FalloutPlayerVitals FromActorValues(FalloutPluginStack records, FalloutPlayerActorValues actorValues,
        GameplayVitals? restore = null) => new(records, actorValues, restore);

    private FalloutPlayerVitals(FalloutPluginStack records, FalloutPlayerActorValues actorValues,
        GameplayVitals? restore) : this(records, actorValues.Source.Player)
    {
        var source = FalloutPlayerActorValueSource.Read(records);
        if (source.PlayerSha256 != actorValues.Source.PlayerSha256 || source.StatsSha256 != actorValues.Source.StatsSha256)
            throw new InvalidDataException("Player vitals and actor values have different sources.");
        _actorValues = actorValues;
        // The player's HP formula uses permanent Endurance and the player
        // offset/multiplier; AP uses bounded current Agility. NPC DATA health
        // does not replace this engine-created player formula.
        _baseHealth = FalloutGameSettingFloats.ReadRetained(records, "fAVDHealthEnduranceOffset", nameof(FalloutPlayerVitals)) * _healthEndurance;
        _state = restore ?? GameplayVitals.Derive(_baseHealth, actorValues.Source.Level, actorValues.ReadPermanent(7),
                actorValues.ReadBoundedCurrent(10), _healthEndurance, _healthLevel, _apBase, _apAgility, 0, _xpBase, _xpBump);
        _state.Validate();
        _ = State;
    }

    private GameplayVitals Derive(FalloutNativeSpecialState special, int level, int experience) => GameplayVitals.Derive(
        _baseHealth, level, special.Endurance, special.Agility, _healthEndurance, _healthLevel,
        _apBase, _apAgility, experience, _xpBase, _xpBump);

    internal void SetSpecial(FalloutNativeSpecialState special)
    {
        if (_actorValues is not null) throw new InvalidOperationException("Player vitals are bound to the shared actor value owner.");
        var derived = Derive(special, State.Level, State.ExperiencePoints);
        SetDerived(derived);
    }

    private void SetDerived(GameplayVitals derived)
    {
        if (_state.MaximumHitPoints == derived.MaximumHitPoints && _state.MaximumActionPoints == derived.MaximumActionPoints &&
            _state.Level == derived.Level && _state.ExperiencePoints == derived.ExperiencePoints &&
            _state.NextLevelExperiencePoints == derived.NextLevelExperiencePoints) return;
        var hitPoints = Math.Clamp(derived.MaximumHitPoints - (_state.MaximumHitPoints - _state.ExactHitPoints), 0, derived.MaximumHitPoints);
        var displayed = checked((int)MathF.Ceiling(hitPoints));
        var state = derived with
        {
            HitPoints = displayed,
            HitPointFraction = displayed - hitPoints,
            LimbDamage = _state.LimbDamage is null ? null : new Dictionary<byte, float>(_state.LimbDamage),
            RadiationRads = _state.RadiationRads,
            ActionPoints = Math.Clamp(derived.MaximumActionPoints - (_state.MaximumActionPoints - _state.ActionPoints), 0, derived.MaximumActionPoints),
        };
        state.Validate(); _state = state;
    }
}
