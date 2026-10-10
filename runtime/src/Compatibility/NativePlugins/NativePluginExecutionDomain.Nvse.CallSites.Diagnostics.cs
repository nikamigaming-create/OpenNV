namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeSourceCallSiteFailureReceipt(ulong Generation, int ProcessId, ulong Module,
    uint Thread, string ModulePath, string ModuleSha256, string StackSha256,
    IReadOnlyList<NativeSourceCallSiteEvent> Events);

internal sealed partial class NativePluginExecutionDomain
{
    private NativeSourceCallSiteFailureReceipt? _sourceCallSiteFailure;
    private string? _sourceCallSiteFailureFile, _sourceCallSiteDeliveryFailure;
    internal NativeSourceCallSiteFailureReceipt? SourceCallSiteFailure
    { get { lock (_faultGate) return _sourceCallSiteFailure; } }
    internal string? PrivateSourceCallSiteFailureFile
    { get { lock (_faultGate) return _sourceCallSiteFailureFile; } }
    internal string? PrivateSourceCallSiteDeliveryFailure
    { get { lock (_faultGate) return _sourceCallSiteDeliveryFailure; } }

    private void RetainSourceCallSiteFailure()
    {
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Source CALL failure lost its original module.");
        var events = _sourceCallSiteEvents.ToArray();
        if (events.Length == 0 || events[^1].Failure is null ||
            events.Any(row => row.Generation != Generation || row.Module != plugin.Module ||
                row.Thread != NativeThread || row.Callback == 0))
            throw new InvalidDataException("Source CALL failure lost its correlated callback prefix.");
        var receipt = new NativeSourceCallSiteFailureReceipt(Generation, ProcessId, plugin.Module,
            NativeThread, plugin.Path, plugin.Sha256, _privateIo?.Selection.StackSha256 ?? "", events);
        lock (_faultGate)
        {
            if (_sourceCallSiteFailure is not null) return;
            _sourceCallSiteFailure = receipt;
        }
        if (_privateIo is null)
        {
            AppendDiagnostic(" Source CALL failure retained in memory; this domain has no private diagnostic owner.");
            return;
        }
        try
        {
            var path = _privateIo.PublishSourceCallSiteFailure(receipt);
            lock (_faultGate) _sourceCallSiteFailureFile = path;
            AppendDiagnostic(" Source CALL failure saved to the selected module's private latest receipt.");
        }
        catch (Exception delivery)
        {
            lock (_faultGate) _sourceCallSiteDeliveryFailure = delivery.ToString();
            AppendDiagnostic($" Private source CALL failure delivery failed ({delivery.GetType().Name}); correlated prefix retained in memory.");
            throw;
        }
    }
}
