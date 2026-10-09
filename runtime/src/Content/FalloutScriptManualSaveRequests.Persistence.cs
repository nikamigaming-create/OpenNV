using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record RuntimeSaveRequestBinding(string SourceCompatibilityId, string ContinuePath, Func<Guid, string> SlotPath);

internal sealed partial class FalloutScriptManualSaveRequests
{
    internal void BindSelection(RuntimeSaveRequestBinding binding, Func<ulong> enginePhase)
    {
        if (Order.Bound || _writeNewSlot is not null) throw new InvalidOperationException("Source save selection is already bound.");
        _enginePhase = enginePhase;
        Order.Bind(binding.SourceCompatibilityId, binding.ContinuePath, binding.SlotPath, () => CurrentPhase);
    }

    private void RequireSuspendedPrefix(FalloutFormKey caller, string scope, string program, FalloutCompiledCursorSnapshot cursor)
    {
        foreach (var row in Order.Requests.Where(row => row.Disposition == RuntimeSaveRequestDisposition.Pending &&
            row.Invocation?.Disposition == RuntimeSaveInvocationDisposition.Suspended && row.Script!.Caller == caller &&
            row.Script.ScopeSha256 == scope && row.Script.ProgramSha256 == program))
            if (JsonSerializer.Serialize(row.Invocation!.SuspendedSlice!.Cursor) != JsonSerializer.Serialize(cursor))
                throw new InvalidDataException("Save-request continuation cannot replay or skip its retained actual SCDA prefix.");
    }

    internal void RestoreOrder(RuntimeSaveRequestColdLoad loaded)
    {
        if (_entered.Count != 0 || _executing.Count != 0 || _invocation != 0 || _suspendedCompiled.Count != 0)
            throw new InvalidOperationException("Save-request cold continuation requires a fresh source execution owner.");
        loaded.RequireUnchanged();
        Order.Restore(loaded.Snapshot, loaded.Path, records, loaded.Suspended);
        foreach (var row in Order.Requests.Where(row => row.Disposition == RuntimeSaveRequestDisposition.Pending &&
            row.Invocation?.Disposition == RuntimeSaveInvocationDisposition.Suspended))
        {
            var site = row.Script!; var lease = row.Invocation!;
            var key = (site.Caller, site.ScopeSha256!, site.ProgramSha256);
            if (!_suspendedCompiled.TryGetValue(key, out var values)) _suspendedCompiled.Add(key, values = []);
            if (!values.Any(value => value.Invocation == lease.Invocation && value.Session == lease.Session))
                values.Add(new(site.Caller, site.Program, site.RecordSha256, site.ProgramSha256,
                    lease.Invocation, site.Authority, site.ScopeSha256, lease.Session, site.CompiledProgram, null));
            _unclosedRequests.Add(lease.Invocation);
            _invocation = Math.Max(_invocation, lease.Invocation);
        }
    }

    internal RuntimeSaveRequestOrderSnapshot CaptureOrder()
    {
        RequireCapture(); return Order.Capture();
    }

    internal string? PreparationBlocker(ulong order) => _entered.Count != 0
        ? "entered-source-invocation" : Order.PreparationBlocker(order);
}
