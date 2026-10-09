namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private NativePluginExceptionReceipt? _nativeException;
    private string? _nativeExceptionFile, _nativeExceptionDeliveryFailure;
    internal NativePluginExceptionReceipt? NativeException { get { lock (_faultGate) return _nativeException; } }
    internal string? PrivateNativeExceptionFile { get { lock (_faultGate) return _nativeExceptionFile; } }
    internal string? PrivateNativeExceptionDeliveryFailure { get { lock (_faultGate) return _nativeExceptionDeliveryFailure; } }

    private NativePluginExceptionObservation? ReadNativeException(BinaryReader reader, Frame frame, uint code, string reason)
    {
        var observed = NativePluginExceptionObservation.Read(reader, code, reason, ReadText);
        if (observed is null) return null;
        if (NativeThread == 0 || observed.Thread != NativeThread)
            throw new InvalidDataException("Native exception does not belong to the actual companion thread.");
        var operation = (NativePluginDomainOperation)frame.Operation;
        var nvseCall = operation is NativePluginDomainOperation.NvseQuery or NativePluginDomainOperation.NvseLoad or
            NativePluginDomainOperation.NvseMessage or NativePluginDomainOperation.NvseSerialization or NativePluginDomainOperation.NvseCommand;
        var stageOwned = operation switch
        {
            NativePluginDomainOperation.Call => observed.Stage == "authored scalar call",
            NativePluginDomainOperation.NvseQuery => observed.Stage == "NVSEPlugin_Query",
            NativePluginDomainOperation.NvseLoad => observed.Stage == "NVSEPlugin_Load",
            _ => false
        };
        // Original synchronous Dispatch may enter a registered listener inside
        // Query/Load/another command. The waiting request remains its outer call;
        // the retained stage belongs to the actual inner guarded invocation.
        stageOwned |= nvseCall && (MessageStage(observed.Stage) || IndexedStage(observed.Stage, "NVSE serialization ") ||
            IndexedStage(observed.Stage, "NVSE command "));
        if (!stageOwned) throw new InvalidDataException("Native exception stage has no entered call owner.");
        return observed;
    }

    private static bool IndexedStage(string stage, string prefix) => stage.StartsWith(prefix, StringComparison.Ordinal) &&
        uint.TryParse(stage.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _);
    private static bool MessageStage(string stage)
    {
        const string prefix = "NVSE message ", separator = ", listener ";
        if (!stage.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var split = stage.IndexOf(separator, prefix.Length, StringComparison.Ordinal);
        return split >= prefix.Length &&
            uint.TryParse(stage.AsSpan(prefix.Length, split - prefix.Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out _) &&
            uint.TryParse(stage.AsSpan(split + separator.Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var listener) && listener != 0;
    }

    private void RetainNativeException(NativePluginExceptionObservation? observed, Frame frame)
    {
        if (observed is null) return;
        var receipt = new NativePluginExceptionReceipt
        {
            Generation = Generation,
            ProcessId = ProcessId,
            Call = frame.Id,
            ParentCall = frame.Parent,
            Operation = (NativePluginDomainOperation)frame.Operation,
            Observation = observed,
            Module = _nvsePlugin?.Module ?? _module?.Handle,
            Image = _nvsePlugin?.Image ?? _module?.GuestBase,
            ModulePath = _nvsePlugin?.Path ?? _privateIo?.Selection.ModulePath ?? _moduleSource?.Name,
            ModuleSha256 = _nvsePlugin?.Sha256 ?? _privateIo?.Selection.ModuleSha256,
            StackSha256 = _privateIo?.Selection.StackSha256
        };
        lock (_faultGate)
        {
            if (_nativeException is not null)
                throw new InvalidDataException("A fatal native generation already has its retained exception receipt.");
            _nativeException = receipt;
        }
        if (_privateIo is null)
        {
            AppendDiagnostic(" Native exception retained in memory; this domain has no selected private diagnostic owner.");
            return;
        }
        try
        {
            object? collision = null;
            string? collisionFailure = null;
            try { collision = CaptureCallableCollision(observed); }
            catch (Exception error) { collisionFailure = error.ToString(); }
            var path = _privateIo.PublishNativeException(receipt, collision, collisionFailure);
            lock (_faultGate) _nativeExceptionFile = path;
            AppendDiagnostic(" Native exception saved to the selected module's private latest-fault receipt.");
        }
        catch (Exception error)
        {
            // Preserve the first original exception even when private diagnostic
            // delivery fails. Exact secondary detail remains private in memory.
            lock (_faultGate) _nativeExceptionDeliveryFailure = error.ToString();
            AppendDiagnostic($" Private native exception delivery failed ({error.GetType().Name}); raw receipt retained in memory.");
        }
    }

    private object? CaptureCallableCollision(NativePluginExceptionObservation observed)
    {
        // CAL1 belongs to our own deliberate abort and its fifteen actual
        // VirtualQuery fields. Other exceptions are never interpreted as it.
        if (observed.Code != 0xe04e5653 || observed.PriorCallbackCode != 487 ||
            observed.Parameters.Length != 15 || observed.Parameters[0] != 0x43414c31) return null;
        var allocation = observed.Parameters[9];
        var view = _mappingViews.Values.SingleOrDefault(value => value.Address == allocation);
        var mapped = view is null ? null : _mappingReceipts.LastOrDefault(value =>
            value.Operation == NativePluginMappingOperation.Map && value.Capability == view.Id && value.Success);
        var route = mapped?.BackingFile is > 0 ? _privateIo?.Receipts.SingleOrDefault(value =>
            value.Sequence == mapped.BackingFile && value.Api == 1 && value.Error == 0) : null;
        var guest = _guestAllocations.Values.Concat(_guestRetirements).SingleOrDefault(value => value.Address == allocation);
        return new
        {
            Memory = _process.CaptureCallableCollision(observed.Parameters[4]),
            AdmittedView = mapped,
            BackingFileSource = route,
            AdmittedGuestRegion = guest,
            AdmittedMappingReceiptCount = _mappingReceipts.Count
        };
    }
}
