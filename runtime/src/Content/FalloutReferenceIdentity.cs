using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static class FalloutReferenceIdentity
{
    internal static bool IsInList(FalloutPluginStack records, FalloutFormKey reference, FalloutFormKey listKey)
    {
        var list = records.GetEffective(listKey);
        if (list.Signature != "FLST") throw new InvalidDataException("IsInList requires a winning FLST argument.");
        var baseForm = Base(records, reference);
        var matches = false;
        foreach (var field in list.ReadSubrecords().Where(field => field.Signature == "LNAM"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("IsInList has an invalid source member extent.");
            var member = list.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            _ = records.GetEffective(member);
            // GECK IsInList compares the reference's base object. A placed
            // reference entry or a nested list is not that base object.
            matches |= member == baseForm;
        }
        return matches;
    }

    internal static bool Matches(FalloutPluginStack records, FalloutFormKey reference, FalloutFormKey requested)
    {
        foreach (var key in new[] { reference, requested })
            if (key != records.RuntimeFormKey(0x14) &&
                (!records.TryGetEffective(key, out var placed) || placed.Signature is not ("REFR" or "ACHR" or "ACRE")))
                throw new InvalidDataException("Reference comparison has no winning placed source.");
        return reference == requested;
    }

    internal static FalloutFormKey Base(FalloutPluginStack records, FalloutFormKey reference)
    {
        FalloutFormKey form;
        if (reference == records.RuntimeFormKey(0x14)) form = records.RuntimeFormKey(7);
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
        if (reference == records.RuntimeFormKey(0x14) && source.Signature != "NPC_")
            throw new InvalidDataException("Player reference identity has no winning NPC base.");
        return form;
    }
}
