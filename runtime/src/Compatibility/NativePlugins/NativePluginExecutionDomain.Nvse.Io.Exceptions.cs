using System.Text.Json;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginPrivateIo
{
    internal string PublishNativeException(NativePluginExceptionReceipt receipt, object? callableCollision = null,
        string? callableCollisionFailure = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_generation is not { } generation || receipt.Generation != generation || receipt.Call == 0 || receipt.ProcessId <= 0 ||
            !StringComparer.OrdinalIgnoreCase.Equals(Canonical(receipt.ModulePath ?? ""), Canonical(Selection.ModulePath)) ||
            !StringComparer.OrdinalIgnoreCase.Equals(receipt.ModuleSha256, Selection.ModuleSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(receipt.StackSha256, Selection.StackSha256))
            throw new InvalidDataException("Private native exception has no exact selected generation/module/source owner.");
        var directory = Path.Combine(ModuleRoot, NativePluginIoRole.Diagnostic.ToString());
        var target = Path.Combine(directory, "native-exception-latest.private.json");
        var pending = Path.Combine(directory, $".native-exception-{generation}-{receipt.Call}.new");
        if (!Within(ModuleRoot, target) || !Within(ModuleRoot, pending) || Selection.OriginalRoots.Any(root => Within(root, target) || Within(root, pending)))
            throw new InvalidDataException("Native exception destination escaped its private source namespace.");
        NoReparse(directory); NoReparse(target); NoReparse(pending);
        NativePluginIoSecurity.RequirePrivateTreeOwned(directory);
        var observed = receipt.Observation;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = 1,
            PrivateOnly = true,
            receipt.Generation,
            receipt.ProcessId,
            receipt.Call,
            receipt.ParentCall,
            Operation = receipt.Operation.ToString(),
            receipt.Module,
            receipt.Image,
            receipt.ModulePath,
            receipt.ModuleSha256,
            receipt.StackSha256,
            Native = new
            {
                observed.Thread,
                observed.Stage,
                observed.InvocationEntry,
                observed.Code,
                observed.RecordPresent,
                observed.RecordPointer,
                observed.RecordCode,
                observed.RecordFlags,
                observed.NestedRecordPointer,
                observed.ExceptionAddress,
                observed.ContextPresent,
                observed.ContextFlags,
                observed.ControlPresent,
                observed.ProgramCounter,
                observed.DeclaredParameterCount,
                CopiedParameterCount = observed.Parameters.Length,
                Parameters = observed.Parameters.ToArray(),
                observed.ParametersTruncated,
                observed.AccessOperation,
                observed.AccessKind,
                observed.AccessOperand,
                observed.InPageStatus,
                observed.PriorCallbackCode,
                observed.PriorCallbackReason,
                observed.PriorCallbackTruncated
            },
            CallableCollision = callableCollision,
            CallableCollisionFailure = callableCollisionFailure,
            Uninspected = new[] { "nested exception record contents", "unwind/call stack", "instruction/object/hook attribution", "missing engine behavior" }
        });
        var pendingCreated = false;
        Exception? failure = null;
        try
        {
            using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                pendingCreated = true; output.Write(payload); output.Flush(flushToDisk: true);
            }
            NoReparse(target); NoReparse(pending);
            File.Move(pending, target, overwrite: true); pendingCreated = false;
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (pendingCreated)
            {
                try { NoReparse(pending); File.Delete(pending); }
                catch (Exception cleanup)
                {
                    failure = failure is null ? cleanup : new AggregateException(
                        "Private native exception publication and pending-file retirement failed.", failure, cleanup);
                }
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return target;
    }
}
