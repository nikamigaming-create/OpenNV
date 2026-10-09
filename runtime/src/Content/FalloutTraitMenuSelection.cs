using System.Globalization;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutTraitMenuChoice(FalloutNativeTraitIdentity Trait, string Description, string? Icon, bool Enabled);

// A menu draft does not grant/revoke perks before the shared acceptance owner.
internal sealed class FalloutTraitMenuSelection
{
    private readonly FalloutTraitMenuContract _contract;
    private readonly FalloutPluginStack _records;
    private readonly int _playerLevel;
    private readonly Func<FalloutCondition, float> _evaluate;
    private readonly List<FalloutNativeTraitIdentity> _selected;
    internal IReadOnlyList<FalloutTraitMenuChoice> Choices { get; }
    internal IReadOnlyList<FalloutNativeTraitIdentity> Selected => _selected.ToArray();
    internal int Maximum => _contract.MaximumTraits;
    internal int Remaining => Maximum - _selected.Count;

    internal FalloutTraitMenuSelection(FalloutPluginStack records, FalloutTraitMenuContract contract,
        IReadOnlyList<FalloutNativeTraitIdentity> current, int playerLevel, Func<FalloutCondition, float> evaluate)
    {
        FalloutTraitMenuCatalogue.Validate(records, contract, current);
        _records = records; _contract = contract; _selected = [.. current]; _playerLevel = playerLevel; _evaluate = evaluate;
        Choices = contract.Traits.Select(trait =>
        {
            var record = records.GetEffective(records.RuntimeFormKey(trait.RuntimeFormId));
            if (record.Signature != "PERK") throw new InvalidDataException("Trait menu choice is not a PERK.");
            var fields = record.ReadSubrecords().ToArray();
            string? Text(string signature)
            {
                var matches = fields.Where(field => field.Signature == signature).ToArray();
                if (matches.Length > 1) throw new InvalidDataException("Trait menu source field is duplicated: " + signature);
                return matches.Length == 0 ? null : FalloutDialogueTopic.Text(matches[0].Data.Span);
            }
            if (Text("EDID") != trait.EditorId || Text("FULL") != trait.DisplayName)
                throw new InvalidDataException("Trait menu identity differs from its winning PERK.");
            return new FalloutTraitMenuChoice(trait, Text("DESC") ?? "", Text("ICON"),
                FalloutTraitMenuCatalogue.Eligible(records, trait, playerLevel, evaluate));
        }).OrderBy(choice => choice.Trait.DisplayName, StringComparer.Ordinal).ToArray();
    }

    internal bool Toggle(FalloutNativeTraitIdentity trait)
    {
        if (!Choices.Any(choice => choice.Trait == trait)) throw new InvalidDataException("Trait menu choice is outside its source contract.");
        if (_selected.Remove(trait)) return true;
        if (!FalloutTraitMenuCatalogue.Eligible(_records, trait, _playerLevel, _evaluate)) return false;
        if (_selected.Count >= Maximum) return false;
        _selected.Add(trait); return true;
    }
    internal void Reset() => _selected.Clear();
    internal IReadOnlyList<FalloutNativeTraitIdentity> Submit()
    {
        var selected = _selected.OrderBy(trait => trait.RuntimeFormId).ToArray();
        FalloutTraitMenuCatalogue.Validate(_records, _contract, selected);
        if (selected.Any(trait => !FalloutTraitMenuCatalogue.Eligible(_records, trait, _playerLevel, _evaluate)))
            throw new InvalidDataException("Selected trait is no longer eligible in the authoritative player state.");
        return selected;
    }
    internal static int ReadMaximum(FalloutPluginStack records)
    {
        var value = FalloutGameSettingIntegers.ReadRetained(records, "iTraitMenuMaxNumTraits", "trait selection contract");
        if (value > int.MaxValue) throw new InvalidDataException("Trait menu maximum is a negative or unsupported signed count.");
        return (int)value;
    }
    internal static string FormatCount(string format, int count)
    {
        var at = format.IndexOf("%d", StringComparison.Ordinal);
        if (at < 0 || format[..at].Contains('%') || format[(at + 2)..].Contains('%'))
            throw new NotSupportedException("Trait menu count string requires another source format owner.");
        return format[..at] + count.ToString(CultureInfo.InvariantCulture) + format[(at + 2)..];
    }
}
