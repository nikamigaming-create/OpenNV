using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private string? RecoverMissingPlayGroup(FalloutReferenceInstance instance, IReadOnlyList<FalloutReferenceScriptEvent> admitted)
    {
        if (host.PlayGroup is null || instance.Script is null || instance.ScriptError is not { } error) return null;
        var match = System.Text.RegularExpressions.Regex.Match(error,
            @"^(?<event>[^:]+): Reached (?:native script|object-script) command (?<command>\S+) \(2 arguments\) has no owner\.$");
        if (!match.Success || !match.Groups["command"].Value.Split('.')[^1].Equals("PlayGroup", StringComparison.OrdinalIgnoreCase)) return null;
        var eventName = match.Groups["event"].Value;
        if (!admitted.Any(item => item.Name.Equals(eventName, StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            var source = Program(instance);
            var blocks = source.Events.Where(block => block.Event.Equals(eventName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (blocks.Length != 1 || blocks[0].Filter is not null) return null;
            var safe = false;
            _ = Steps(instance.Reference, source.Bindings, blocks[0].Program, null, 0,
                inspectFunctions: functions => safe = blocks[0].Program.CanRetryMissingCommand(match.Groups["command"].Value, functions));
            if (!safe) return null;
            instance.ScriptError = null;
            return error;
        }
        catch (Exception failure) when (failure is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException)
        { return null; }
    }

    private string? RecoverMissingRead(FalloutReferenceInstance instance, IReadOnlyList<FalloutReferenceScriptEvent> admitted)
    {
        const string marker = ": Script operand ", suffix = " has no variable owner.";
        if (instance.Script is null || instance.ScriptError is not { } error || !error.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var split = error.IndexOf(marker, StringComparison.Ordinal);
        if (split < 1 || !admitted.Any(item => item.Name.Equals(error[..split], StringComparison.OrdinalIgnoreCase))) return null;
        var operand = error[(split + marker.Length)..^suffix.Length];
        try
        {
            var source = Program(instance);
            var blocks = source.Events.Where(block => block.Event.Equals(error[..split], StringComparison.OrdinalIgnoreCase)).ToArray();
            if (blocks.Length != 1 || blocks[0].Filter is not null) return null;
            var safe = false;
            _ = Steps(instance.Reference, source.Bindings, blocks[0].Program, null, 0,
                inspectFunctions: functions => safe = blocks[0].Program.CanRetryMissingRead(operand, functions));
            if (!safe) return null;
            instance.ScriptError = null;
            return error;
        }
        catch (Exception failure) when (failure is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException)
        {
            // A failed recovery inspection leaves the original failure intact.
            return null;
        }
    }
}
