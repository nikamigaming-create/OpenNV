using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.Content;

internal static class FalloutReferenceIdentity
{
    internal static bool IsInList(FalloutPluginStack records, FalloutFormKey reference, FalloutFormKey listKey,
        IFalloutFormLists? current = null)
    {
        var list = records.GetEffective(listKey);
        if (list.Signature != "FLST") throw new InvalidDataException("IsInList requires a winning FLST argument.");
        var baseForm = Base(records, reference);
        var matches = false;
        foreach (var entry in FalloutFormList.Entries(records, listKey, current))
        {
            if (entry is not { } member) continue;
            if (!FalloutEngineIdentity.IsPlayerReference(records, member)) _ = records.GetEffective(member);
            // GECK IsInList compares the reference's base object. A placed
            // reference entry or a nested list is not that base object.
            matches |= member == baseForm;
        }
        return matches;
    }

    internal static bool Matches(FalloutPluginStack records, FalloutFormKey reference, FalloutFormKey requested)
    {
        foreach (var key in new[] { reference, requested })
            if (!FalloutEngineIdentity.IsPlayerReference(records, key) &&
                (!records.TryGetEffective(key, out var placed) || placed.Signature is not ("REFR" or "ACHR" or "ACRE")))
                throw new InvalidDataException("Reference comparison has no winning placed source.");
        return reference == requested;
    }

    internal static FalloutFormKey Base(FalloutPluginStack records, FalloutFormKey reference)
    {
        FalloutFormKey form;
        if (FalloutEngineIdentity.IsPlayerReference(records, reference)) form = records.RuntimeFormKey(FalloutEngineForms.PlayerBase);
        else
        {
            if (!records.TryGetEffective(reference, out var placed) || placed.Signature is not ("REFR" or "ACHR" or "ACRE"))
                throw new InvalidDataException("Reference identity has no winning placed source.");
            form = FalloutDialogueTopic.RequiredForm(placed, "NAME");
        }
        if (!records.TryGetEffective(form, out var source))
            throw new InvalidDataException("Reference identity has no winning base source.");
        if (source.Signature is "LVLN" or "LVLC" or "LVLI")
            throw new NotSupportedException("Leveled reference identity requires its selected permanent base owner.");
        if (FalloutEngineIdentity.IsPlayerReference(records, reference) && source.Signature != "NPC_")
            throw new InvalidDataException("Player reference identity has no winning NPC base.");
        return form;
    }
}
