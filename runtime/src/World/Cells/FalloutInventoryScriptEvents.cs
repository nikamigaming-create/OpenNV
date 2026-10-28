using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// A supplied execution owner must stage all writes and effects before publishing
// inventory. This dispatcher does not fabricate item references, state or
// rollback; it selects authored event blocks and runs the shared script VM.
internal sealed record FalloutInventoryScriptExecution(FalloutScriptValueContext Values,
    Func<string, FalloutScriptFunction?> Function,
    Action<string, IReadOnlyList<string>> Command,
    Func<string, string, FalloutScriptFunction>? UserFunction = null);

internal static class FalloutInventoryScriptEvents
{
    internal static int Dispatch(FalloutPluginStack records, FalloutPluginRecord item, string eventName,
        FalloutFormKey recipient, FalloutInventoryScriptExecution execution)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(execution);
        if (!FalloutReferenceWorld.IsInventoryItem(item.Signature))
            throw new InvalidDataException("Inventory event source is not an item definition.");
        if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentException("Inventory event name is absent.", nameof(eventName));
        var script = FalloutScriptLocals.AttachedScript(records, item);
        if (script is null) return 0;
        var fields = script.ReadSubrecords().ToArray();
        var source = fields.Where(field => field.Signature == "SCTX").ToArray();
        if (source.Length != 1) throw new NotSupportedException("Inventory event requires an unambiguous source script.");
        var bindings = new FalloutScriptBindings(records, item, script, fields);
        var blocks = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(source[0].Data.Span));
        var budget = new FalloutScriptExecutionBudget(); var executed = 0;
        foreach (var block in blocks)
        {
            if (!block.Event.Equals(eventName, StringComparison.OrdinalIgnoreCase)) continue;
            if (block.Filter is { } filter && bindings.Reference(filter) != recipient) continue;
            block.Program.Execute(name => execution.Values.Read(name).Number,
                (name, value) => execution.Values.Write(name, value), execution.Command,
                execution.Function, execution.UserFunction, budget, execution.Values);
            ++executed;
        }
        return executed;
    }
}
