using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

/// <summary>Winning game-setting price factors for one native barter session.</summary>
internal sealed class FalloutBarterPricing
{
    private readonly float _buyBase, _buyMultiplier, _sellBase, _sellMultiplier;
    private readonly float _conditionBase, _conditionMultiplier, _conditionExponent;

    internal FalloutBarterPricing(FalloutPluginStack records)
    {
        _buyBase = Setting(records, "fBarterBuyBase");
        _buyMultiplier = Setting(records, "fBarterBuyMult");
        _sellBase = Setting(records, "fBarterSellBase");
        _sellMultiplier = Setting(records, "fBarterSellMult");
        _conditionBase = Setting(records, "fItemConditionValueBase");
        _conditionMultiplier = Setting(records, "fItemConditionValueMult");
        _conditionExponent = Setting(records, "fItemConditionValueExp");
    }

    internal int Total(FalloutCampaignItem item, int count, bool vendorSells, float barterSkill, int discount)
    {
        if (count <= 0 || count > item.Count) throw new ArgumentOutOfRangeException(nameof(count));
        if (discount is < -100 or > 100) throw new ArgumentOutOfRangeException(nameof(discount));
        if (!float.IsFinite(barterSkill) || barterSkill is < 0 or > 100)
            throw new InvalidDataException("Player Barter skill is outside runtime bounds.");
        var value = item.Value ?? throw new NotSupportedException($"Item {item.FormKey} has no source-owned trade value.");
        var baseFactor = vendorSells
            ? _buyBase + _buyMultiplier * (barterSkill / 100)
            : _sellBase + _sellMultiplier * (barterSkill / 100);
        var discountFactor = vendorSells ? 1 - discount / 100f : 1 + discount / 100f;
        if (!float.IsFinite(baseFactor) || baseFactor < 0)
            throw new InvalidDataException("Winning barter settings produce a negative or non-finite base price.");
        // Bound ordinary source barter prices by the item's current value
        // before applying an explicit ShowBarterMenu discount.
        baseFactor = vendorSells ? Math.Max(1, baseFactor) : Math.Min(1, baseFactor);
        var factor = baseFactor * discountFactor;
        if (!float.IsFinite(factor) || factor < 0)
            throw new InvalidDataException("Winning barter settings produce a negative or non-finite price.");

        var variants = item.Variants ?? [new(item.Count)];
        var remaining = count;
        var total = 0;
        foreach (var variant in variants)
        {
            if (remaining == 0) break;
            var units = Math.Min(remaining, variant.Count);
            var currentValue = value * ConditionValueMultiplier(item.RecordType, variant.Condition);
            var exact = currentValue * factor;
            if (!double.IsFinite(exact) || exact < 0 || exact > int.MaxValue)
                throw new NotSupportedException($"Item {item.FormKey} trade value exceeds runtime storage.");
            var unitPrice = checked((int)Math.Round(exact, MidpointRounding.AwayFromZero));
            total = checked(total + checked(unitPrice * units));
            remaining -= units;
        }
        if (remaining != 0) throw new InvalidDataException($"Item {item.FormKey} variants do not cover the trade quantity.");
        return total;
    }

    private float ConditionValueMultiplier(string signature, float? condition)
    {
        if (signature is not ("ARMO" or "WEAP")) return 1;
        var current = condition ?? 1;
        if (!float.IsFinite(current) || current is < 0 or > 1)
            throw new InvalidDataException("Trade item condition is outside 0..1.");
        var conditionScale = MathF.Pow(current * 10, _conditionExponent);
        var multiplier = _conditionBase + (1 - _conditionBase) * _conditionMultiplier * conditionScale;
        if (!float.IsFinite(multiplier) || multiplier < 0)
            throw new InvalidDataException("Winning item-condition settings produce an invalid value.");
        return multiplier;
    }

    private static float Setting(FalloutPluginStack records, string name)
    {
        var value = FalloutGameSettingFloats.Read(records, name);
        if (!float.IsFinite(value)) throw new InvalidDataException($"Barter setting {name} is non-finite.");
        return value;
    }
}
