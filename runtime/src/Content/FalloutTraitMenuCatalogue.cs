namespace OpenNV.Runtime.Content;

internal sealed record FalloutPerkDeclaration(bool Trait, byte MinimumLevel, byte Ranks,
    bool Playable, bool Hidden, IReadOnlyList<FalloutCondition> Conditions)
{
    internal static FalloutPerkDeclaration Read(FalloutPluginRecord record)
    {
        if (record.Signature != "PERK" || record.IsDeleted)
            throw new InvalidDataException("Perk declaration requires a live winning PERK.");
        // Effect DATA/CTDA belong to their PRKE groups, independently of the
        // selection header and its eligibility conditions.
        var header = record.ReadSubrecords().TakeWhile(field => field.Signature != "PRKE").ToArray();
        var declarations = header.Where(field => field.Signature == "DATA").ToArray();
        if (declarations.Length != 1 || declarations[0].Data.Length is not (4 or 5))
            throw new NotSupportedException($"PERK {record.FormKey} selection declaration has an unowned extent.");
        var data = declarations[0].Data.Span;
        if (data[0] > 1 || data[3] > 1 || data.Length == 5 && data[4] > 1)
            throw new InvalidDataException($"PERK {record.FormKey} selection flags are not boolean.");
        return new(data[0] != 0, data[1], data[2], data[3] != 0, data.Length == 5 && data[4] != 0,
            header.Where(field => field.Signature == "CTDA")
                .Select(field => FalloutCondition.Read(record, field.Data.Span)).ToArray());
    }
}

internal sealed record FalloutTraitMenuContract(IReadOnlyList<FalloutNativeTraitIdentity> Traits, int MaximumTraits);

internal static class FalloutTraitMenuCatalogue
{
    internal static FalloutTraitMenuContract Read(FalloutPluginStack records)
    {
        var traits = records.EffectiveRecords("PERK").Where(record => !record.IsDeleted)
            .Where(record => IsSelectable(FalloutPerkDeclaration.Read(record)))
            .Select(record => Identity(records, record)).OrderBy(trait => trait.RuntimeFormId).ToArray();
        return new(traits, FalloutTraitMenuSelection.ReadMaximum(records));
    }

    internal static bool Eligible(FalloutPluginStack records, FalloutNativeTraitIdentity trait,
        int playerLevel, Func<FalloutCondition, float> evaluate)
    {
        if (playerLevel < 1) throw new InvalidDataException("Trait selection has an invalid player level.");
        var record = records.GetEffective(records.RuntimeFormKey(trait.RuntimeFormId));
        var declaration = FalloutPerkDeclaration.Read(record);
        RequireIdentity(records, record, trait, declaration);
        if (declaration.Ranks == 0)
            throw new NotSupportedException($"Trait {record.FormKey} has no selectable rank owner.");
        return playerLevel >= declaration.MinimumLevel &&
            FalloutCondition.AllPass(declaration.Conditions, evaluate, evaluateRunOn: true);
    }

    internal static void Validate(FalloutPluginStack records, IReadOnlyList<FalloutNativeTraitIdentity> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Any(trait => trait is null) ||
            selection.Select(trait => trait.RuntimeFormId).Distinct().Count() != selection.Count)
            throw new InvalidDataException("Player trait identities are empty or duplicated.");
        // Empty player state does not open a trait menu or demand an unrelated
        // installation's optional trait-menu setting.
        if (selection.Count == 0) return;
        if (selection.Count > FalloutTraitMenuSelection.ReadMaximum(records))
            throw new InvalidDataException("Player traits exceed the winning trait-menu limit.");
        foreach (var trait in selection)
        {
            var record = records.GetEffective(records.RuntimeFormKey(trait.RuntimeFormId));
            var declaration = FalloutPerkDeclaration.Read(record);
            RequireIdentity(records, record, trait, declaration);
            if (declaration.Ranks == 0)
                throw new InvalidDataException("Saved trait has no declared rank.");
        }
    }

    internal static void Validate(FalloutPluginStack records, FalloutTraitMenuContract contract,
        IReadOnlyList<FalloutNativeTraitIdentity> selection)
    {
        if (contract.MaximumTraits < 0 || selection.Count > contract.MaximumTraits ||
            contract.Traits.Select(trait => trait.RuntimeFormId).Distinct().Count() != contract.Traits.Count ||
            selection.Any(trait => !contract.Traits.Contains(trait)))
            throw new InvalidDataException("Player traits differ from the winning trait menu catalogue.");
        Validate(records, selection);
    }

    private static bool IsSelectable(FalloutPerkDeclaration declaration) =>
        declaration.Trait && declaration.Playable && !declaration.Hidden;

    private static FalloutNativeTraitIdentity Identity(FalloutPluginStack records, FalloutPluginRecord record)
    {
        string Text(string signature)
        {
            var fields = record.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
            if (fields.Length != 1) throw new InvalidDataException($"Trait {record.FormKey} has no unique {signature} identity.");
            var text = FalloutDialogueTopic.Text(fields[0].Data.Span);
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Trait identity is empty.");
            return text;
        }
        return new(records.RuntimeFormId(record.FormKey), Text("EDID"), Text("FULL"));
    }

    private static void RequireIdentity(FalloutPluginStack records, FalloutPluginRecord record,
        FalloutNativeTraitIdentity trait, FalloutPerkDeclaration declaration)
    {
        if (!IsSelectable(declaration) || Identity(records, record) != trait)
            throw new InvalidDataException("Trait menu identity differs from its winning PERK.");
    }
}
