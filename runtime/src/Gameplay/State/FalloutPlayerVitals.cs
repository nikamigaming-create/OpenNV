using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed class FalloutPlayerVitals
{
    private readonly int _baseHealth;
    private readonly double _healthEndurance, _healthLevel, _apBase, _apAgility, _xpBase, _xpBump;
    internal GameplayVitals State { get; private set; }

    internal FalloutPlayerVitals(FalloutPluginStack records, FalloutFormKey player, FalloutNativeSpecialState special,
        GameplayVitals? restore = null)
    {
        var fields = records.GetEffective(player).ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data;
        var actor = fields.Single(field => field.Signature == "ACBS").Data;
        if (data.Length != 11 || actor.Length != 24) throw new NotSupportedException("Player base-stat layout is unbound.");
        _baseHealth = BinaryPrimitives.ReadInt32LittleEndian(data.Span);
        var level = BinaryPrimitives.ReadInt16LittleEndian(actor.Span[8..]);
        _healthEndurance = FalloutGameSettingFloats.Read(records, "fAVDHealthEnduranceMult");
        _healthLevel = FalloutGameSettingFloats.Read(records, "fAVDHealthLevelMult");
        _apBase = FalloutGameSettingFloats.Read(records, "fAVDActionPointsBase");
        _apAgility = FalloutGameSettingFloats.Read(records, "fAVDActionPointsMult");
        _xpBase = FalloutGameSettingIntegers.Read(records, "iXPBase");
        _xpBump = FalloutGameSettingIntegers.Read(records, "iXPBumpBase");
        State = restore ?? Derive(special, level, 0);
        State.Validate();
    }

    private GameplayVitals Derive(FalloutNativeSpecialState special, int level, int experience) => GameplayVitals.Derive(
        _baseHealth, level, special.Endurance, special.Agility, _healthEndurance, _healthLevel,
        _apBase, _apAgility, experience, _xpBase, _xpBump);

    internal void SetSpecial(FalloutNativeSpecialState special)
    {
        var derived = Derive(special, State.Level, State.ExperiencePoints);
        State = derived with
        {
            HitPoints = Math.Clamp(derived.MaximumHitPoints - (State.MaximumHitPoints - State.HitPoints), 0, derived.MaximumHitPoints),
            ActionPoints = Math.Clamp(derived.MaximumActionPoints - (State.MaximumActionPoints - State.ActionPoints), 0, derived.MaximumActionPoints),
        };
    }
}
