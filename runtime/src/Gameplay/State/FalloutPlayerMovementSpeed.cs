using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

/// <summary>The player movement calculation shared by locomotion and travel time.</summary>
internal readonly record struct FalloutPlayerMovement(float Speed, int CrippledLegs, float LimbMultiplier, bool Overencumbered, bool Running);

internal sealed class FalloutPlayerMovementSpeed
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPlayerInventory _inventory;
    private readonly FalloutBodyPartData _body;
    private float _base, _run, _sneak, _holstered, _oneLeg, _twoLeg, _mediumArmor, _heavyArmor, _rifle, _bigGun;
    private long _inventoryRevision = -1, _settingsRevision = -1;
    private float _armorPenalty, _weaponPenalty;

    internal FalloutPlayerMovementSpeed(FalloutPluginStack records, FalloutPlayerInventory inventory)
    {
        _records = records; _inventory = inventory;
        _body = FalloutBodyPartData.Read(records.GetEffective(records.RuntimeFormKey(0x1d)));
    }

    private void RefreshSettings()
    {
        if (_settingsRevision == _records.NumericSettings.Revision) return;
        float Setting(string name)
        {
            var value = FalloutGameSettingFloats.Read(_records, name);
            return float.IsFinite(value) && value >= 0 ? value :
                throw new InvalidDataException($"Movement setting {name} is invalid.");
        }
        _base = Setting("fMoveBaseSpeed"); _run = Setting("fMoveRunMult");
        _sneak = Setting("fMoveSneakMult"); _holstered = Setting("fMoveNoWeaponMult");
        _oneLeg = Setting("fMoveOneCrippledLegSpeedMult"); _twoLeg = Setting("fMoveTwoCrippledLegsSpeedMult");
        _mediumArmor = Setting("fMoveMediumArmorPenalty"); _heavyArmor = Setting("fMoveHeavyArmorPenalty");
        _rifle = Setting("fMove2HRPenalty"); _bigGun = Setting("fMove2HBigPenalty");
        _settingsRevision = _records.NumericSettings.Revision;
        _inventoryRevision = -1;
    }

    internal (float Weight, float Capacity) InventoryBurden(Func<int, float> actorValue,
        IReadOnlyList<FalloutPerkEntry> perks, Func<FalloutCondition, float> condition)
    {
        RefreshEquipment();
        var weight = Finite(actorValue(46), "InventoryWeight");
        if (weight < int.MinValue || (double)weight > int.MaxValue)
            throw new NotSupportedException("Carried weight exceeds its source integer query.");
        var capacity = FalloutPlayerPerkNumericEffects.Apply(22, Finite(actorValue(13), "CarryWeight"), perks, condition);
        return (weight, capacity);
    }

    internal bool Overencumbered(Func<int, float> actorValue, IReadOnlyList<FalloutPerkEntry> perks,
        Func<FalloutCondition, float> condition)
    {
        var burden = InventoryBurden(actorValue, perks, condition);
        return (int)burden.Weight > burden.Capacity;
    }

    internal FalloutPlayerMovement Calculate(bool running, bool sneaking, bool weaponDrawn, GameplayVitals vitals,
        Func<int, float> actorValue, IReadOnlyList<FalloutPerkEntry> perks, Func<FalloutCondition, float> condition)
    {
        RefreshEquipment();
        var speedMult = Finite(actorValue(21), "SpeedMult");
        var ignore = Finite(actorValue(72), "IgnoreCrippledLimbs");
        if (ignore < int.MinValue || (double)ignore > int.MaxValue)
            throw new NotSupportedException("IgnoreCrippledLimbs exceeds its source integer query.");
        var crippled = (_body.LimbCondition(29, vitals.MaximumHitPoints, vitals.LimbDamage) <= 0 ? 1 : 0) +
            (_body.LimbCondition(30, vitals.MaximumHitPoints, vitals.LimbDamage) <= 0 ? 1 : 0);
        var overencumbered = Overencumbered(actorValue, perks, condition);
        var limbMultiplier = !overencumbered && (int)ignore != 0 ? 1 :
            crippled == 2 ? _twoLeg : crippled == 1 ? _oneLeg : 1;
        var speed = _base * (speedMult / 100) * limbMultiplier;
        if (!weaponDrawn) speed *= _holstered;
        speed *= 1 - _armorPenalty - (weaponDrawn ? _weaponPenalty : 0);
        if (sneaking) speed *= _sneak;
        speed = Math.Max(0, speed);
        running &= !overencumbered;
        if (running) speed *= _run;
        if (running) speed = FalloutPlayerPerkNumericEffects.Apply(42, speed, perks, condition);
        speed = Finite(speed, "calculated speed");
        if (speed < 0) throw new NotSupportedException("A negative perk-adjusted movement speed is unbound.");
        return new(speed, crippled, limbMultiplier, overencumbered, running);
    }

    private static float Finite(float value, string field) => float.IsFinite(value) ? value :
        throw new InvalidDataException($"Player movement {field} is not finite.");

    private void RefreshEquipment()
    {
        RefreshSettings();
        if (_inventoryRevision == _inventory.Revision) return;
        var armorPenalty = 0f;
        if (_inventory.EquippedObject(_records, 2) is { } armor)
        {
            var record = _records.GetEffective(armor);
            if (record.Signature != "ARMO") throw new InvalidDataException("Player torso equipment is not armor.");
            var data = record.ReadSubrecords().Single(field => field.Signature == "BMDT").Data.Span;
            if (data.Length != 8) throw new InvalidDataException("Player armor biped flags have an invalid extent.");
            armorPenalty = (data[4] & 0x80) != 0 ? _heavyArmor : (data[4] & 8) != 0 ? _mediumArmor : 0;
        }
        var weaponPenalty = 0f;
        var weapons = _inventory.Equipped.Select(_records.RuntimeFormKey)
            .Select(_records.GetEffective).Where(record => record.Signature == "WEAP").ToArray();
        if (weapons.Length > 1) throw new NotSupportedException("Player movement has multiple equipped weapons.");
        if (weapons.Length == 1)
        {
            var data = weapons[0].ReadSubrecords().Single(field => field.Signature == "DNAM").Data.Span;
            if (!FalloutWeaponPresentation.IsSupportedDnamExtent(data.Length))
                throw new NotSupportedException("Movement weapon has an unsupported DNAM extent.");
            // These are source weapon categories, not GetWeaponAnimType's group numbers.
            weaponPenalty = BinaryPrimitives.ReadUInt32LittleEndian(data) switch
            {
                5 or 6 => _rifle,
                8 or 9 => _bigGun,
                <= 13 => 0,
                _ => throw new NotSupportedException("Movement weapon category is unbound."),
            };
        }
        _armorPenalty = armorPenalty; _weaponPenalty = weaponPenalty;
        _inventoryRevision = _inventory.Revision;
    }
}
