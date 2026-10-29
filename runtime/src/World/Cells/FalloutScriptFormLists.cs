using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Both parsed-script hosts call the same mutable owner. Compiled execution must
// use this host too; it must not mutate the immutable plugin records.
internal static class FalloutScriptFormLists
{
    internal static bool Supports(string name) => name is "addformtoformlist" or "listgetcount" or "listgetnthform";

    internal static FalloutScriptFunction? Function(string name, FalloutPluginStack records,
        Func<FalloutFormListState> owner)
    {
        if (name == "addformtoformlist")
            return FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Value], arguments =>
            {
                var list = arguments[0].Value.FormKey(records);
                var form = arguments[1].Value.FormKey(records);
                owner().Add(list, form);
                return 0;
            });
        if (name == "listgetcount")
            return FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Value], arguments =>
            {
                var entries = owner().Entries(arguments[0].Value.FormKey(records));
                return entries.Count == 0 || entries[0] is null ? 0 : entries.Count;
            }, readOnly: true);
        if (name == "listgetnthform")
            return FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Number], arguments =>
            {
                var entries = owner().Entries(arguments[0].Value.FormKey(records));
                var raw = arguments[1].Number;
                if (!double.IsFinite(raw) || raw != Math.Truncate(raw) || raw < int.MinValue || raw > int.MaxValue)
                    throw new InvalidDataException("Form-list index must be a signed integer.");
                var index = (int)raw;
                if (index == FalloutFormListIndex.End) index = entries.Count - 1;
                return FalloutScriptValue.Form(index >= 0 && index < entries.Count && entries[index] is { } form
                    ? records.RuntimeFormId(form) : 0);
            }, readOnly: true);
        return null;
    }
}
