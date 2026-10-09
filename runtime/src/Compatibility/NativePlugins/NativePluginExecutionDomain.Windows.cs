using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint WindowProcessCallback = 29;
    private NativePluginProductWindow? _productWindow;
    private IDisposable? _productWindowLease;
    private string? _windowProcessSource;
    private bool _windowBindEntered, _windowNativeRetired, _windowEnumeration;
    private readonly List<NativePluginWindowReceipt> _windowReceipts = [];
    internal IReadOnlyList<NativePluginWindowReceipt> NvseWindowReceipts => _windowReceipts.AsReadOnly();
    internal bool WindowProcessOwnersRetired => _windowProcessSource is null && _productWindow is null && _productWindowLease is null &&
        _windowProcessHandles.Count == 0 && _windowProcessPending is null && _process.WindowProcessReferencesRetired;
    internal void BindChildProcessImports()
    {
        VerifyOwner(); var io = _privateIo ?? throw new InvalidOperationException("Process imports require their actual selected restricted source domain.");
        if (_windowBindEntered || _callDepth != 0 || NativeModuleCount != 0)
            throw new InvalidOperationException("Process import producer repeated or entered after module/call publication.");
        _windowBindEntered = true; _windowProcessSource = io.Selection.StackSha256;
        using var reader = Exchange(NativePluginDomainOperation.WindowProcess, Payload(writer =>
        { writer.Write(1U); writer.Write(0U); writer.Write(0U); writer.Write(0U); WriteText(writer, _windowProcessSource); }));
        if ((reader.ReadUInt32() | reader.ReadUInt32() | reader.ReadUInt32()) != 0)
            throw new InvalidDataException("Child process scope fabricated an unbound product window.");
        Finish(reader);
    }
    internal void BindProductWindow(FalloutDirectInputState input)
    {
        VerifyOwner(); var io = _privateIo ?? throw new InvalidOperationException("Window producer requires its selected restricted source domain.");
        if (_windowBindEntered || _callDepth != 0 || NativeModuleCount != 0 ||
            !input.Source.Equals(io.Selection.StackSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Window producer repeated or differs from the exact selected source/call lifetime.");
        _windowBindEntered = true; _windowProcessSource = input.Source; _productWindow = new(input); _productWindowLease = _productWindow.Retain();
        using var reader = Exchange(NativePluginDomainOperation.WindowProcess, Payload(writer =>
        { writer.Write(1U); writer.Write(_productWindow.Window); writer.Write(_productWindow.Thread); writer.Write(_productWindow.Process); WriteText(writer, input.Source); }));
        if (reader.ReadUInt32() != _productWindow.Window || reader.ReadUInt32() != _productWindow.Thread || reader.ReadUInt32() != _productWindow.Process)
            throw new InvalidDataException("Actual source window binding did not return its exact HWND/thread/process identity.");
        Finish(reader);
    }
    private byte[] DispatchPrivateWindowProcess(ulong parent, uint thread, ulong module, BinaryReader reader)
    {
        var source = _windowProcessSource ?? throw new NotSupportedException("Original import has no actual selected process/source producer.");
        if (parent != _currentCall || parent == 0 || thread != NativeThread || _nvsePlugin is not { } plugin ||
            plugin.Generation != Generation || plugin.Module != module)
            throw new InvalidDataException("Window/process callback lost its actual source module/generation/native caller.");
        _privateIo!.CheckModule(plugin.Path, plugin.Sha256, source);
        var category = reader.ReadUInt32();
        if (category == 2) return DispatchWindowProcessSdk(parent, thread, module, reader);
        var window = _productWindow ?? throw new NotSupportedException("Original USER32 import has no actual product-window/message-thread producer.");
        window.Require();
        if (category != 1 || _windowNativeRetired) throw new InvalidDataException("Window callback category or native lifetime is invalid.");
        var operation = (NativePluginWindowOperation)reader.ReadUInt32(); var target = reader.ReadUInt32(); var incoming = reader.ReadUInt32();
        var count = reader.ReadUInt32();
        if (!Enum.IsDefined(operation) || count > MaximumPayload || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Window callback lacks its exact public operation/caller extent.");
        var bytes = reader.ReadBytes(checked((int)count)); Finish(reader);
        (int Result, uint LastError) Invoke(uint observedWindow)
        {
            if (_windowEnumeration) throw new InvalidOperationException("An entered window callback cannot replay its enumeration.");
            _windowEnumeration = true; ++_callDepth;
            try
            {
                using var receipt = Exchange(NativePluginDomainOperation.WindowProcess, Payload(writer =>
                { writer.Write(2U); writer.Write(module); writer.Write(observedWindow); writer.Write(incoming); }));
                var result = receipt.ReadInt32(); var error = receipt.ReadUInt32(); Finish(receipt); return (result, error);
            }
            finally { --_callDepth; _windowEnumeration = false; }
        }
        var outcome = window.Execute(operation, target, incoming, NativeThread, bytes,
            operation == NativePluginWindowOperation.Enumerate ? Invoke : null);
        _windowReceipts.Add(new(Generation, module, thread, parent, operation, target, outcome.Result, outcome.LastError, outcome.Bytes.Length, window.SourceIdentity));
        return Payload(writer => { writer.Write(outcome.Result); writer.Write(outcome.LastError); writer.Write(checked((uint)outcome.Bytes.Length)); writer.Write(outcome.Bytes); });
    }
    internal void RetireProductWindowCalls()
    {
        VerifyOwner(); if (_windowProcessSource is null || _windowNativeRetired) return;
        if (_callDepth != 0 || NativeModuleCount != 0) throw new InvalidOperationException("Original image/call remains before window SDK retirement.");
        _productWindow?.RetireBeforeChildExit(NativeThread);
        if (_windowProcessHandles.Count != 0 || _windowProcessPending is not null)
            throw new InvalidDataException("Original process query resources remain at actual native retirement.");
        _windowNativeRetired = true;
    }
    internal void RequireWindowProcessColdCapture()
    {
        VerifyOwner(); _productWindow?.RequireColdCapture();
        if (_windowEnumeration || _windowProcessPending is not null || _windowProcessHandles.Count != 0)
            throw new NotSupportedException("Original process/query/Toolhelp pointers lack an admitted current-source cold continuation.");
    }
    private void ClearWindowProcessAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Window/process/source release requires the exact observed child closure.");
        List<Exception> failures = [];
        foreach (var row in _windowProcessHandles.ToArray())
        {
            try { _process.CloseWindowProcessReference(row.Value.Guard); _windowProcessHandles.Remove(row.Key); }
            catch (Exception error) { failures.Add(error); }
        }
        if (_windowProcessHandles.Count == 0) _windowProcessPending = null;
        try { _productWindowLease?.Dispose(); _productWindowLease = null; } catch (Exception error) { failures.Add(error); }
        try { _productWindow?.RetireAfterChildExit(); _productWindow = null; } catch (Exception error) { failures.Add(error); }
        try { _process.CloseFailedWindowProcessReferencesAfterChildExit(); _process.RequireWindowProcessReferencesRetired(); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Independent original window/process owners failed retirement.", failures);
        _windowProcessSource = null;
    }
}
