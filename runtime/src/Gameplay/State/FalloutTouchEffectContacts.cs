using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutTouchEffectContact(FalloutFormKey Reference, float X, float Y, float Z);

// Contacts use source coordinates. A blast has one caster identity; independent
// detonations call this separately and never share a suppression ledger.
internal static class FalloutTouchEffectContacts
{
    internal static int MaximumAreaRadius(FalloutPluginStack records, FalloutIngestible source)
    {
        FalloutInstantTouchEffects.RequireSourceOwner(source, areaDelivered: true, healthDelivered: true);
        var area = source.Effects.Where(effect => (effect.Flags & 0x2000) == 0).Select(effect => effect.Area).DefaultIfEmpty().Max();
        if (area == 0) return 0;
        var units = FalloutGameSettingFloats.Read(records, "fMagicUnitsPerFoot");
        // Native expansion truncates the units/foot scale before multiplying
        // by the authored integer area, rather than rounding the final radius.
        var integerUnits = MathF.Truncate(units);
        var radius = area * (double)integerUnits;
        if (integerUnits <= 0 || !double.IsFinite(radius) || radius > int.MaxValue)
            throw new NotSupportedException("Magic area scale exceeds its positive signed radius contract.");
        return (int)radius;
    }

    internal static IReadOnlyDictionary<FalloutFormKey, IReadOnlyList<FalloutIngestibleEffect>> Select(
        FalloutPluginStack records, FalloutIngestible source, IReadOnlyList<FalloutTouchEffectContact> contacts,
        IReadOnlySet<FalloutFormKey> direct, Func<FalloutTouchEffectContact, FalloutTouchEffectContact, bool> clearLineOfSight)
    {
        var maximum = MaximumAreaRadius(records, source);
        if (contacts.Select(contact => contact.Reference).Distinct().Count() != contacts.Count ||
            contacts.Any(contact => !float.IsFinite(contact.X) || !float.IsFinite(contact.Y) || !float.IsFinite(contact.Z)) ||
            direct.Any(reference => contacts.All(contact => contact.Reference != reference)))
            throw new InvalidDataException("Magic area contacts lack distinct authoritative positions.");
        var after = new Dictionary<FalloutFormKey, List<FalloutIngestibleEffect>>();
        foreach (var origin in contacts.Where(contact => direct.Contains(contact.Reference)))
        {
            foreach (var effect in source.Effects)
            {
                if ((effect.Flags & 0x2000) != 0) continue;
                Add(origin.Reference, effect);
                if (effect.Area == 0) continue;
                var radius = maximum / source.Effects.Where(value => (value.Flags & 0x2000) == 0).Max(value => value.Area) * effect.Area;
                foreach (var target in contacts)
                {
                    if (target.Reference == origin.Reference) continue;
                    var x = (double)target.X - origin.X; var y = (double)target.Y - origin.Y; var z = (double)target.Z - origin.Z;
                    if (x * x + y * y + z * z >= (double)radius * radius || !clearLineOfSight(origin, target)) continue;
                    Add(target.Reference, effect);
                }
            }
        }
        return after.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<FalloutIngestibleEffect>)pair.Value.ToArray());

        void Add(FalloutFormKey target, FalloutIngestibleEffect effect)
        {
            if (!after.TryGetValue(target, out var effects)) after.Add(target, effects = []);
            if ((source.EnchantmentFlags.GetValueOrDefault() & 2) != 0 && effects.Any(prior => SameDeclaration(prior, effect))) return;
            effects.Add(effect);
        }
    }

    private static bool SameDeclaration(FalloutIngestibleEffect left, FalloutIngestibleEffect right) =>
        left.Form == right.Form && left.Magnitude == right.Magnitude && left.Duration == right.Duration &&
        left.Area == right.Area && left.Range == right.Range && left.DeclaredActorValue == right.DeclaredActorValue &&
        left.Conditions.SequenceEqual(right.Conditions);
}
