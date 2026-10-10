using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private FalloutEngineFieldMethodSource? _sourceFieldMethodSource;
    private readonly Dictionary<uint, NativeSourceFieldMethod> _sourceFieldMethods = [];
    private readonly List<NativeSourceFieldMethodEvent> _sourceFieldMethodEvents = [];
    private uint _nextSourceFieldMethod;
    private NativeSourceFieldMethodInvocation? _sourceFieldMethodInvocation;
    internal IReadOnlyList<NativeSourceFieldMethodEvent> SourceFieldMethodEvents => _sourceFieldMethodEvents.AsReadOnly();

    private uint DispatchNvseFieldMethod(NativeNvseHostCall operation, Frame frame, ulong parent, BinaryReader reader)
    {
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Source field method lost its actual original-module owner.");
        var host = _nvseHostSource ?? throw new InvalidDataException("Source field method lost its exact selected runtime.");
        host.Check(Generation); RequireSourceAddressSpace(host);
        NativeSourceFieldMethod? method = null; uint entry = 0; NativeSourceFieldValue? result = null; NativeNvseSourceObject? receiver = null;
        try
        {
            if (operation == NativeNvseHostCall.SourceFieldMethodDeclare)
            {
                entry = reader.ReadUInt32(); var abi = (NativePluginAbi)reader.ReadUInt32(); var receiverClass = reader.ReadUInt32(); Finish(reader);
                if (_operation != nameof(NativePluginDomainOperation.SourceFieldMethods) || plugin.Phase != NativeNvsePhase.LoadedTrue ||
                    abi is not (NativePluginAbi.Cdecl or NativePluginAbi.Thiscall) ||
                    (abi == NativePluginAbi.Cdecl ? receiverClass != 0 : receiverClass is < 1 or > 3))
                    throw new NotSupportedException("Source field declaration requires its genuine typed loaded caller and explicit zero-argument ABI.");
                if (_sourceFieldMethods.TryGetValue(entry, out method))
                {
                    RequireSourceFieldMethod(method, plugin);
                    if (method.Phase != NativeSourceFieldMethodPhase.Published || method.Failure is not null ||
                        method.Abi != abi || (uint?)method.ReceiverClass != (receiverClass == 0 ? (uint?)null : receiverClass))
                        throw new InvalidDataException("Source field method replayed a retired/failed or differently typed declaration.");
                    Record(); return method.Id;
                }
                if (_nextSourceFieldMethod >= 256) throw new NotSupportedException("Source field native callable lifetime quota is exhausted.");
                _sourceFieldMethodSource ??= FalloutExecutableStringTable.ReadEngineFieldMethodSource(host.RuntimePath, host.RuntimeSha256);
                var source = _sourceFieldMethodSource.Read(entry);
                if ((source.Result.UsesReceiver || source.Reads.Any(value => value.UsesReceiver)) && abi != NativePluginAbi.Thiscall ||
                    entry < host.SourceAddressSpace.ImageBase || (ulong)entry + 7 > (ulong)host.SourceAddressSpace.ImageBase + host.SourceAddressSpace.ImageBytes)
                    throw new InvalidDataException("Source field method has no complete selected code/receiver ABI extent.");
                method = new(checked(++_nextSourceFieldMethod), Generation, plugin.Module, NativeThread, source, abi,
                    receiverClass == 0 ? null : (NativeNvseSourceClass)receiverClass);
                _sourceFieldMethods.Add(entry, method); Record(); return method.Id;
            }
            var id = reader.ReadUInt32(); entry = reader.ReadUInt32();
            if (!_sourceFieldMethods.TryGetValue(entry, out method) || method.Id != id)
                throw new InvalidDataException("Source field callback has no exact retained declaration.");
            RequireSourceFieldMethod(method, plugin);
            if (method.Failure is not null) throw new InvalidOperationException("Source field method retains its failed entered prefix: " + method.Failure);
            if (operation == NativeNvseHostCall.SourceFieldMethodPublish)
            {
                var lease = reader.ReadUInt64(); var target = reader.ReadUInt32(); Finish(reader);
                if (_operation != nameof(NativePluginDomainOperation.SourceFieldMethods) ||
                    method.Phase != NativeSourceFieldMethodPhase.Declared || lease == 0 || target == 0)
                    throw new InvalidDataException("Source field publication repeated or omitted its real native callable.");
                _process.RequireEngineCommandCallable(entry, target, retired: false);
                method.Lease = lease; method.Target = target; method.Phase = NativeSourceFieldMethodPhase.Published;
                Record(); return 1;
            }
            if (operation == NativeNvseHostCall.SourceFieldMethodExecute)
            {
                var objectId = reader.ReadUInt64(); var pointer = reader.ReadUInt32(); Finish(reader);
                if (method.Phase != NativeSourceFieldMethodPhase.Published ||
                    _operation is not (nameof(NativePluginDomainOperation.SourceFieldMethods) or nameof(NativePluginDomainOperation.NvseCommand) or
                        nameof(NativePluginDomainOperation.NvseLoad) or nameof(NativePluginDomainOperation.NvseMessage)))
                    throw new NotSupportedException("Source method escaped its actual typed/command/loader/message invocation.");
                if (method.Abi == NativePluginAbi.Thiscall)
                {
                    if (!_nvseSourceObjects.TryGetValue(objectId, out receiver) || receiver.Address != pointer || receiver.Class != method.ReceiverClass)
                        throw new InvalidDataException("Source method receiver is not its real complete native class.");
                    VerifyNvseSourceObject(plugin, receiver);
                }
                else if (objectId != 0 || pointer != 0) throw new InvalidDataException("Source cdecl method supplied a fabricated implicit receiver.");
                _process.RequireEngineCommandCallable(entry, method.Target, retired: false);
                var invocation = _sourceFieldMethodInvocation;
                if (invocation is not null && (!ReferenceEquals(invocation.Method, method) ||
                    !ReferenceEquals(invocation.Receiver, receiver) || invocation.Returned || method.Calls != 1))
                    throw new InvalidDataException("Source method callback lost its exact entered object/call receipt.");
                if (invocation is null) ++method.Calls;
                if (receiver is not null) ++receiver.Calls;
                try
                {
                    result = EvaluateSourceFieldMethod(plugin, method, receiver);
                    if (invocation is not null && !SameSourceFieldResult(invocation.Expected, result))
                        throw new InvalidDataException("Actual source state changed inside its correlated native field invocation.");
                    RetainSourceFieldResult(plugin, method, result);
                    if (invocation is not null) { invocation.Returned = true; invocation.Callback = frame.Id; }
                    Record(); return result.Bits;
                }
                finally { if (receiver is not null) --receiver.Calls; if (invocation is null) --method.Calls; }
            }
            if (operation == NativeNvseHostCall.SourceFieldMethodRetire)
            {
                var lease = reader.ReadUInt64(); Finish(reader);
                if (_operation != nameof(NativePluginDomainOperation.UnloadNvse) || method.Calls != 0 ||
                    method.Phase != NativeSourceFieldMethodPhase.Published || lease != method.Lease)
                    throw new InvalidDataException("Source field retirement lacks its returned real original-module unload prefix.");
                _process.RequireEngineCommandCallable(entry, method.Target, retired: true);
                foreach (var group in method.Borrows.GroupBy(value => value.Owner))
                    if (group.Key.Dependents < group.Count()) throw new InvalidDataException("Source field borrow lost its independent retained dependencies.");
                foreach (var borrow in method.Borrows) --borrow.Owner.Dependents;
                method.Borrows.Clear(); method.Phase = NativeSourceFieldMethodPhase.Retired;
                Record(); return 1;
            }
            throw new InvalidDataException("Unknown source field method callback.");
        }
        catch (Exception failure) { if (method is not null) method.Failure ??= failure.Message; Record(failure.Message); throw; }
        void Record(string? failure = null) => _sourceFieldMethodEvents.Add(new(Generation, plugin.Module, NativeThread, frame.Id, parent,
            operation, method?.Id ?? 0, entry, host.RuntimeSha256, method?.Source.BodySha256 ?? "", method?.Phase ?? NativeSourceFieldMethodPhase.Declared,
            receiver?.Id, result?.Bits, result?.Role, result?.Buffer?.Handle ?? result?.Object?.Image.Handle, failure));
    }

    private void RequireSourceFieldMethod(NativeSourceFieldMethod method, NativeNvsePlugin plugin)
    {
        if (method.Generation != Generation || method.Module != plugin.Module || method.Thread != NativeThread ||
            !_sourceFieldMethods.TryGetValue(method.Source.Entry, out var actual) || !ReferenceEquals(method, actual) ||
            method.Phase is NativeSourceFieldMethodPhase.Retired or NativeSourceFieldMethodPhase.Closed)
            throw new InvalidDataException("Source field callable belongs to a foreign/retired generation or class lease.");
    }
    private void RequireSourceFieldMethodsRetired()
    {
        if (_sourceFieldMethods.Values.Any(value => value.Phase != NativeSourceFieldMethodPhase.Retired || value.Borrows.Count != 0 || value.Failure is not null))
            throw new InvalidDataException("Original module retirement retains a live/failed source field callable or returned pointer.");
    }
    internal void RequireSourceFieldMethodsColdCapture()
    {
        if (_sourceFieldMethods.Values.Any(value => value.Failure is not null ||
            value.Phase is NativeSourceFieldMethodPhase.Declared or NativeSourceFieldMethodPhase.Published))
            throw new NotSupportedException("Published source methods/returned pointer leases require their complete native cold reconstruction owner.");
    }
    private void ClearSourceFieldMethodsAfterChildExit()
    {
        if (!ChildExited || _sourceFieldMethodInvocation is not null)
            throw new InvalidOperationException("Source field owners remain leased until their genuine child closure and returned call.");
        foreach (var group in _sourceFieldMethods.Values.SelectMany(value => value.Borrows).GroupBy(value => value.Owner))
            if (group.Key.Dependents < group.Count()) throw new InvalidDataException("Child-closed source borrows lost their actual dependency counters.");
        foreach (var method in _sourceFieldMethods.Values)
        {
            foreach (var borrow in method.Borrows)
            {
                --borrow.Owner.Dependents;
            }
            method.Borrows.Clear(); method.Phase = NativeSourceFieldMethodPhase.Closed;
        }
        _sourceFieldMethodSource = null;
    }
}
