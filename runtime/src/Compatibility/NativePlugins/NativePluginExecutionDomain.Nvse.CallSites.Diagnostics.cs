namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeSourceCallSiteFailureScope { SemanticPrefix, SdkFailureTail }
internal sealed record NativeSourceCallSiteFailureReceipt(ulong Generation, int ProcessId, ulong Module,
    uint Thread, string ModulePath, string ModuleSha256, string StackSha256,
    IReadOnlyList<NativeSourceCallSiteEvent> Events, NativeSourceCallSiteFailureScope Scope, int StartEvent);

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

    private void RetainSourceCallSiteFailure(int? sdkFailureStart = null)
    {
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Source CALL failure lost its original module.");
        var start = sdkFailureStart ?? 0;
        if (start < 0 || start >= _sourceCallSiteEvents.Count)
            throw new InvalidDataException("Source CALL diagnostic lost its exact event range.");
        var events = _sourceCallSiteEvents.GetRange(start, _sourceCallSiteEvents.Count - start).ToArray();
        if ((sdkFailureStart is null ? events[^1].Failure is null : events[0].SdkResult != 0 || events.Any(row => row.Site != events[0].Site)) ||
            events.Any(row => row.Generation != Generation || row.Module != plugin.Module ||
                row.Thread != NativeThread || row.Callback == 0))
            throw new InvalidDataException("Source CALL failure lost its correlated callback prefix.");
        var receipt = new NativeSourceCallSiteFailureReceipt(Generation, ProcessId, plugin.Module,
            NativeThread, plugin.Path, plugin.Sha256, _privateIo?.Selection.StackSha256 ?? "", events,
            sdkFailureStart is null ? NativeSourceCallSiteFailureScope.SemanticPrefix : NativeSourceCallSiteFailureScope.SdkFailureTail, start);
        lock (_faultGate)
        {
            _sourceCallSiteFailure = receipt;
        }
        if (_privateIo is null)
        {
            if (receipt.Scope == NativeSourceCallSiteFailureScope.SemanticPrefix)
                AppendDiagnostic(" Source CALL failure retained in memory; this domain has no private diagnostic owner.");
            return;
        }
        try
        {
            var path = _privateIo.PublishSourceCallSiteFailure(receipt);
            lock (_faultGate) _sourceCallSiteFailureFile = path;
            if (receipt.Scope == NativeSourceCallSiteFailureScope.SemanticPrefix)
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
