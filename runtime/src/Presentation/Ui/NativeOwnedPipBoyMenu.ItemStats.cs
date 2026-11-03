using System.Globalization;
using System.Xml.Linq;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoyMenu
{
    private object? _inventoryStatCard;
    private object? _inventoryHeadline;
    internal Func<FalloutActorDefense>? InventoryDefense { get; init; }
    internal Func<float>? InventoryCarryCapacity { get; init; }
    internal Func<bool>? InventoryHardcore { get; init; }
    internal Func<FalloutFormKey, FalloutItemVariant, string>? InventoryDamage { get; init; }
    internal Func<FalloutFormKey, string>? InventoryStrength { get; init; }
    internal Func<FalloutFormKey, FalloutItemVariant, string?>? InventoryAmmo { get; init; }

    private void BindInventoryHeadline()
    {
        var weight = (_actorValue ?? throw new NotSupportedException("Inventory headline has no actual InventoryWeight owner."))("InventoryWeight");
        var capacity = (InventoryCarryCapacity ?? throw new NotSupportedException("Inventory headline has no actual carry-capacity owner."))();
        if (!float.IsFinite(weight) || weight < 0 || (double)weight >= int.MaxValue ||
            !float.IsFinite(capacity) || capacity < int.MinValue || (double)capacity >= int.MaxValue)
            throw new NotSupportedException("Inventory headline exceeds source integer storage.");
        _tiles.BindText(Tile("IM_Headline_PlayerWGInfo"), "_Value",
            string.Create(CultureInfo.InvariantCulture, $"{(int)MathF.Floor(weight)}/{(int)capacity}"));
        var defenseMissing = new List<string>();
        try
        {
            var defense = (InventoryDefense ?? throw new NotSupportedException("No shared player defense owner."))();
            if (!float.IsFinite(defense.Resistance) || !float.IsFinite(defense.Threshold))
                throw new InvalidDataException("Inventory defense is not finite.");
            _tiles.BindText(Tile("IM_Headline_PlayerDRInfo"), "_Value", MathF.Truncate(defense.Resistance).ToString("F0", CultureInfo.InvariantCulture));
            _tiles.BindText(Tile("IM_Headline_PlayerDTInfo"), "_Value", defense.Threshold.ToString("F1", CultureInfo.InvariantCulture));
            Bind("IM_Headline_PlayerDRInfo", "alpha", 0);
            if (defense.Resistance == 0) Bind("IM_Headline_PlayerDTInfo", "alpha", 255);
            else
            {
                Bind("IM_Headline_PlayerDTInfo", "alpha", 0);
                defenseMissing.Add("DR/DT:shared-alternating-alpha-phase");
            }
        }
        catch (NotSupportedException error)
        {
            Bind("IM_Headline_PlayerDRInfo", "alpha", 0); Bind("IM_Headline_PlayerDTInfo", "alpha", 0);
            defenseMissing.Add("DR/DT:" + error.Message);
        }
        _inventoryHeadline = new { weight, capacity, unbound = defenseMissing.ToArray() };
    }

    internal static void BindWeaponSkillBadge(NativeOwnedMenuTree tiles, XElement image, XElement number,
        FalloutWeaponSkillBadge badge)
    {
        // Native null assignment clears the prior filename. An actual empty
        // image has no draw; its independently admitted number remains visible.
        tiles.SetFilename(image, badge.Icon ?? string.Empty);
        tiles.Bind(image, "visible", badge.Icon is null ? 0 : 1);
        tiles.BindText(number, "string", badge.Requirement.ToString(CultureInfo.InvariantCulture));
        tiles.Bind(number, "visible", 1);
    }

    private void BindInventoryStatCard(FalloutCampaignItem item, FalloutItemVariant extra)
    {
        var missing = new List<string>();
        Hide("DamageResistInfo", "DPSInfo", "WeightInfo", "ValueInfo", "ConditionInfo", "AmmoInfo",
            "EffectsInfo", "ModInfoOne", "ModInfoTwo", "ModInfoThree", "StrengthReqInfo", "DAMInfo", "DamageThresholdInfo", "IM_ItemIconBadge", "IM_StrReq");
        Bind("IM_ItemInfoRect", "visible", 1);

        string? valueText = null;
        string? damageText = null;
        string? ammoText = null;
        string? strengthText = null;
        FalloutWeaponSkillBadge? skillBadge = null;
        try
        {
            valueText = FalloutInventoryItemStatistics.Value(_records, item, extra);
            _tiles.BindText(Tile("ValueInfo"), "_Value", valueText);
            Bind("ValueInfo", "visible", 1);
        }
        catch (NotSupportedException error) { missing.Add("ValueInfo:" + error.Message); }

        // Native card weight uses the actual selected entry quantity. Perk55
        // and weapon perk73 are separate reached owners, never a base-weight
        // substitute. Inspect current source declarations without executing them.
        string? weightText = null;
        var sourceWeight = item.RecordType == "AMMO" && !(InventoryHardcore ??
            throw new NotSupportedException("AMMO weight has no current Hardcore owner."))() ? 0 : item.Weight;
        if (sourceWeight is not { } baseWeight) missing.Add("WeightInfo:source-weight");
        else
        {
            try
            {
                var unitWeight = item.RecordType == "WEAP"
                    ? FalloutWeaponModifications.Weight(_records, item.FormKey, extra.WeaponMods, baseWeight) : baseWeight;
                var player = _records.RuntimeFormKey(0x14);
                var weightPoints = _traits.Select(trait => _records.RuntimeFormKey(trait.RuntimeFormId))
                    .Concat(_references.AcquiredPerks(player)).Distinct()
                    .SelectMany(form => FalloutLevelUpPerkSource.Read(_records, form).Effects)
                    .Where(effect => effect.Type == 2 && effect.Rank == 0 &&
                        (effect.EntryPoint == 55 || item.RecordType == "WEAP" && unitWeight >= 10 && effect.EntryPoint == 73))
                    .Select(effect => effect.EntryPoint!.Value).Distinct().Order().ToArray();
                if (weightPoints.Length != 0)
                    missing.Add("WeightInfo:perk-entry-" + string.Join(",", weightPoints));
                else
                {
                    var weight = (double)unitWeight * extra.Count;
                    if (!double.IsFinite(weight) || weight < 0 || extra.Count <= 0)
                        throw new InvalidDataException("Selected inventory entry has invalid effective weight.");
                    weightText = weight > 0 ? weight.ToString("0.00", CultureInfo.InvariantCulture) : "--";
                    _tiles.BindText(Tile("WeightInfo"), "_Value", weightText);
                    Bind("WeightInfo", "visible", 1);
                }
            }
            catch (NotSupportedException error)
            {
                // This optional row remains visibly absent and diagnostically
                // unbound; malformed data and unrelated menu errors still fail.
                missing.Add("WeightInfo:" + error.Message);
            }
        }

        float? condition = null;
        if (item.RecordType is "WEAP" or "ARMO")
        {
            condition = extra.Condition ?? 1;
            if (!float.IsFinite(condition.Value) || condition is < 0 or > 1)
                throw new InvalidDataException("Selected inventory condition is outside 0..1.");
            Bind("ConditionInfo", "user5", condition.Value);
            Bind("ConditionInfo", "user6", item.RecordType == "WEAP" ? 1 : 0);
            Bind("ConditionInfo", "visible", 1);
            var arrows = Tile("CNDArrows");
            // The malformed empty source color token assigns no native color;
            // inherit the actual PipBoy parent color rather than mint an identity.
            if (arrows.Element("systemcolor") is { HasElements: false } color && string.IsNullOrWhiteSpace(color.Value)) color.Remove();
            var meter = Tile("ConditionMeter");
            _tiles.Bind(arrows, "height", _tiles.Number(meter, "height"));
            _tiles.Bind(arrows, "y", _tiles.Number(meter, "y"));
        }
        if (item.RecordType == "WEAP")
        {
            try
            {
                skillBadge = FalloutWeaponSkillBadge.Read(_records, item.FormKey);
                BindWeaponSkillBadge(_tiles, Tile("IM_ItemIconBadge"), Tile("IM_StrReq"), skillBadge);
                // Preserve the authored initial alpha. The shared native
                // requirement-pulse clock is a separate, still-unbound lane.
                missing.Add("IM_ItemIconBadge/IM_StrReq:skill-shortage-alpha-phase");
            }
            catch (NotSupportedException error) { missing.Add("IM_ItemIconBadge/IM_StrReq:" + error.Message); }
            try
            {
                damageText = (InventoryDamage ?? throw new NotSupportedException("No actual selected player weapon damage owner."))(item.FormKey, extra);
                _tiles.BindText(Tile("DAMInfo"), "_Value", damageText);
                Bind("DAMInfo", "visible", 1); Bind("DAMInfo", "alpha", 255);
            }
            catch (NotSupportedException error) { missing.Add("DAMInfo:" + error.Message); }
            try
            {
                ammoText = (InventoryAmmo ?? throw new NotSupportedException("No current/source weapon ammo owner."))(item.FormKey, extra);
                if (ammoText is not null)
                {
                    _tiles.BindText(Tile("AmmoInfo"), "_Title", ammoText);
                    Bind("AmmoInfo", "visible", 1);
                }
            }
            catch (NotSupportedException error) { missing.Add("AmmoInfo:" + error.Message); }
            try
            {
                strengthText = (InventoryStrength ?? throw new NotSupportedException("No actual player requirement owner."))(item.FormKey);
                _tiles.BindText(Tile("StrengthReqInfo"), "_Value", strengthText);
                Bind("StrengthReqInfo", "visible", 1); Bind("StrengthReqInfo", "alpha", 255);
            }
            catch (NotSupportedException error) { missing.Add("StrengthReqInfo:" + error.Message); }
            missing.Add("DPSInfo:effective-attack-cadence");
            if (extra.WeaponMods != 0) missing.Add("ModInfo:source-mod-stat-rows");
        }
        else if (item.RecordType == "ARMO") missing.AddRange(["DamageResistInfo:effective-resistance", "DamageThresholdInfo:effective-threshold"]);
        else if (item.RecordType is "ALCH" or "INGR") missing.Add("EffectsInfo:effective-ingestible-stat-rows");
        _inventoryStatCard = new
        {
            form = item.FormKey.ToString(),
            variant = _selectedVariant,
            revision = _inventory.Revision,
            weight = weightText,
            value = valueText,
            damage = damageText,
            ammunition = ammoText,
            strengthRequirement = strengthText,
            skillBadge,
            condition,
            unbound = missing.ToArray()
        };
    }
}
