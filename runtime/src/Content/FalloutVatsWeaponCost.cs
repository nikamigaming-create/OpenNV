using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// Default New Vegas attack AP contract: an authored override supersedes the
// weapon-type GMST; Action Point Cost (entry 40) is applied once afterwards.
// Source layout: xEdit wbDefinitionsFNV WEAP.DNAM; native behavior independently
// checked against the owned executable. Private code/addresses are not inputs.
internal sealed record FalloutVatsWeaponCost(FalloutFormKey? Weapon, uint WeaponType, float BaseCost, byte BaseToHit)
{
    internal static FalloutVatsWeaponCost Read(FalloutPluginStack records, FalloutFormKey? weapon)
    {
        uint type = 0;
        var overridden = false;
        var value = 0f;
        byte toHit = 0;
        if (weapon is { } form)
        {
            var source = records.GetEffective(form);
            if (source.Signature != "WEAP") throw new InvalidDataException("VATS attack source is not WEAP.");
            var data = source.ReadSubrecords().Single(field => field.Signature == "DNAM").Data.Span;
            if (!FalloutWeaponPresentation.IsSupportedDnamExtent(data.Length))
                throw new NotSupportedException("VATS weapon DNAM extent is unbound.");
            type = BinaryPrimitives.ReadUInt32LittleEndian(data);
            overridden = (BinaryPrimitives.ReadUInt32LittleEndian(data[56..]) & 8) != 0;
            if (overridden) value = FalloutProjectile.Number(data, 68);
            toHit = data[40];
        }
        if (!overridden)
        {
            var category = type switch
            {
                0 => "Unarmed",
                1 => "OneHandMelee",
                2 => "TwoHandMelee",
                3 or 4 => "Pistol",
                5 or 6 or 7 => "Rifle",
                8 => "Handle",
                9 => "Launcher",
                10 => "Grenade",
                11 => "Mine",
                13 => "Thrown",
                _ => throw new NotSupportedException($"Weapon type {type} has no supported VATS AP category."),
            };
            value = FalloutGameSettingFloats.Read(records, "fActionPointsAttack" + category);
        }
        if (!float.IsFinite(value) || value < 0) throw new InvalidDataException("VATS source AP cost is invalid.");
        return new(weapon, type, value, toHit);
    }

    internal float ApplyPerks(IReadOnlyList<FalloutPerkEntry> perks, Func<byte, FalloutCondition, float> condition)
        => FalloutPlayerPerkNumericEffects.ApplyContext(40, BaseCost, perks, condition);
}
