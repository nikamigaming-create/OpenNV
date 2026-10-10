using System.Text.Json;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginPrivateIo
{
    internal string PublishSourceCallSiteFailure(NativeSourceCallSiteFailureReceipt receipt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_generation is not { } generation || receipt.Generation != generation || receipt.ProcessId <= 0 ||
            receipt.Module == 0 || receipt.Thread == 0 || receipt.Events.Count == 0 ||
            receipt.Events[^1].Failure is null ||
            receipt.Events.Any(row => row.Generation != generation || row.Module != receipt.Module ||
                row.Thread != receipt.Thread || row.Callback == 0) ||
            !StringComparer.OrdinalIgnoreCase.Equals(Canonical(receipt.ModulePath), Canonical(Selection.ModulePath)) ||
            !StringComparer.OrdinalIgnoreCase.Equals(receipt.ModuleSha256, Selection.ModuleSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(receipt.StackSha256, Selection.StackSha256))
            throw new InvalidDataException("Private source CALL failure has no exact selected module/callback owner.");
        var directory = Path.Combine(ModuleRoot, NativePluginIoRole.Diagnostic.ToString());
        var target = Path.Combine(directory, "native-source-calls-latest.private.json");
        var pending = Path.Combine(directory, $".native-source-calls-{generation}-{receipt.Events[^1].Callback}.new");
        if (!Within(ModuleRoot, target) || !Within(ModuleRoot, pending) ||
            Selection.OriginalRoots.Any(root => Within(root, target) || Within(root, pending)))
            throw new InvalidDataException("Source CALL diagnostic destination escaped its private namespace.");
        NoReparse(directory); NoReparse(target); NoReparse(pending);
        NativePluginIoSecurity.RequirePrivateTreeOwned(directory);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = 1,
            PrivateOnly = true,
            receipt.Generation,
            receipt.ProcessId,
            receipt.Module,
            receipt.Thread,
            receipt.ModulePath,
            receipt.ModuleSha256,
            receipt.StackSha256,
            Events = receipt.Events.Select(row => new
            {
                row.Generation,
                row.Module,
                row.Thread,
                row.Callback,
                row.Parent,
                Event = row.Event.ToString(),
                row.Site,
                row.Address,
                row.RuntimeSha256,
                Phase = row.Phase.ToString(),
                row.Target,
                row.Action,
                row.SdkResult,
                row.LastError,
                row.PreviousProtection,
                row.Receiver,
                row.PointerResult,
                row.Failure
            }).ToArray()
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
                        "Private source CALL diagnostic and pending-file retirement failed.", failure, cleanup);
                }
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return target;
    }
}
