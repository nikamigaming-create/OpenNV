using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginCngSystemService
{
    private sealed record SharedInvocation(ulong Call, NativePluginCryptoOperation Operation, ulong Target,
        uint Flags, uint Error, uint ObjectBytes, bool Destination, bool CopiedPresent,
        NativePluginCngBufferExtent[] Buffers, NativePluginCngSharedPointer?[] Pointers,
        NativePluginCngSharedPointer? Object, NativePluginCngSharedPointer? Copied)
    {
        internal uint[] Written { get; } = Buffers.Select((buffer, at) =>
            !buffer.Present || Pointers[at] is not null ? buffer.Length : 0U).ToArray();
        internal uint Read { get; set; }
        internal NativePluginCngServiceResult? Result { get; set; }
    }
    private NativePluginExecutionDomain? _sharedOriginal;
    private readonly Dictionary<ulong, NativePluginCngSharedSection> _sharedSections = [];
    private readonly Dictionary<(uint Kind, ulong Object), ulong> _sharedObjects = [];
    private readonly Dictionary<(uint Kind, ulong View), ulong> _sharedViews = [];
    private readonly Dictionary<ulong, SharedInvocation> _sharedInvocations = [];
    private readonly Dictionary<ulong, NativePluginCngSharedPointer> _sharedHashObjects = [];
    private readonly List<NativePluginCngSharedReceipt> _sharedReceipts = [];
    private readonly List<NativePluginCngSharedCallReceipt> _sharedCalls = [];
    private readonly List<NativePluginCngDetachReceipt> _detachReceipts = [];
    private readonly List<NativePluginCngSectionHandle> _failedSharedReferences = [];
    private ulong _nextSharedSection, _detachCall;
    private uint _detachImage;
    private bool _detachReturned, _sharedFailed;
    private bool SharedOwnersRetired => _sharedSections.Values.All(value => value.ParentHandleRetired &&
        value.References.Values.All(reference => reference.IsClosed)) && _failedSharedReferences.All(reference => reference.IsClosed);
    internal IReadOnlyList<NativePluginCngSharedReceipt> SharedReceipts => _sharedReceipts.AsReadOnly();
    internal IReadOnlyList<NativePluginCngSharedCallReceipt> SharedCalls => _sharedCalls.AsReadOnly();
    internal IReadOnlyList<NativePluginCngDetachReceipt> DetachReceipts => _detachReceipts.AsReadOnly();

    internal void BindOriginalSharedOwner(NativePluginExecutionDomain original)
    {
        if (_sharedOriginal is not null || original.Generation != _originalGeneration || original.NativeThread != _originalThread ||
            original.ChildExited || _sharedSections.Count != 0)
            throw new InvalidOperationException("Shared CNG memory must bind the one genuine original process generation/thread.");
        _sharedOriginal = original;
    }
    private static byte[] SharedPayload(Action<BinaryWriter> action)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        action(writer); return stream.ToArray();
    }
    private void RequireDetachStep(ulong call, byte[] request, bool actualUnload)
    {
        var step = (NativePluginCngServiceStep)BinaryPrimitives.ReadUInt32LittleEndian(request);
        if (step == NativePluginCngServiceStep.DetachScope) return;
        if (!actualUnload)
        {
            if (_detachCall != 0 && !_detachReturned) throw new InvalidDataException("CNG callback escaped its entered original unload scope.");
            return;
        }
        if (_detachCall == call && _detachReturned && (step is NativePluginCngServiceStep.SharedRelease or NativePluginCngServiceStep.Retire)) return;
        if (_detachCall != call || _detachReturned)
            throw new InvalidDataException("Original CNG destruction callback lacks its actual entered FreeLibrary prefix.");
        if (step == NativePluginCngServiceStep.Begin)
        {
            using var reader = Reader(request); _ = reader.ReadUInt32(); var operation = reader.ReadUInt32();
            if (operation is not (6 or 7)) throw new NotSupportedException("Original unload may only destroy/close already acquired CNG handles.");
        }
        else if (step is not (NativePluginCngServiceStep.Execute or NativePluginCngServiceStep.Release or NativePluginCngServiceStep.SharedRelease))
            throw new NotSupportedException("Original unload cannot create/query/upload new CNG objects or providers.");
    }
    private bool TryForwardShared(ulong call, byte[] request, bool actualUnload, out byte[] reply)
    {
        reply = [];
        using var input = Reader(request); var step = (NativePluginCngServiceStep)input.ReadUInt32();
        if (step == NativePluginCngServiceStep.DetachScope)
        {
            var action = input.ReadUInt32(); var image = input.ReadUInt32(); var succeeded = Presence(input); var error = input.ReadUInt32(); Finish(input);
            var original = _sharedOriginal ?? throw new InvalidOperationException("Original unload has no retained process authority.");
            original.RequireCngDetachSource(_originalModule, image, call);
            if (!actualUnload || action is not (1 or 2) || action == 1 && (succeeded || error != 0 || _detachCall != 0) ||
                action == 2 && (_detachCall != call || _detachImage != image || _detachReturned || succeeded && error != 0))
                throw new InvalidDataException("CNG detach enter/return changed or replayed the actual source unload call.");
            if (action == 1) { _detachCall = call; _detachImage = image; }
            else _detachReturned = true;
            _detachReceipts.Add(new(_originalGeneration, _originalModule, call, image, true, _detachReturned, succeeded, error));
            reply = BitConverter.GetBytes(1U); return true;
        }
        if (step == NativePluginCngServiceStep.SharedBind)
        {
            var operation = (NativePluginCryptoOperation)input.ReadUInt32();
            var source = new NativePluginCngSharedSource(input.ReadUInt32(), input.ReadUInt64(), input.ReadUInt64(),
                input.ReadUInt32(), input.ReadUInt32(), input.ReadUInt32(), input.ReadUInt32(), input.ReadUInt32()); Finish(input);
            if (!Enum.IsDefined(operation) || actualUnload || _sharedFailed || _sharedViews.ContainsKey((source.Kind, source.View)))
                throw new InvalidDataException("Shared CNG source bind is repeated, failed or inside original detach.");
            var original = _sharedOriginal ?? throw new InvalidOperationException("Shared CNG source has no genuine original process owner.");
            // Cross-check the actual source view even when its Windows section
            // already has another live original view. Every declaration survives.
            var verified = original.RetainCngSharedSource(source); _failedSharedReferences.Add(verified);
            NativePluginCngSharedSection section;
            uint remote = 0;
            if (_sharedObjects.TryGetValue((source.Kind, source.Object), out var existing))
            {
                section = _sharedSections[existing];
                if (section.Maximum != source.Maximum || section.ReleaseEntered || section.ServiceViewRetired || !section.Entered)
                    throw new InvalidDataException("CNG shared object extent or entered lifetime changed between source aliases.");
                NativePluginDomainChild.RequireSameCngSection(section.Parent, verified);
            }
            else
            {
                // The retained reference is transferred, never disposed on a
                // failed entered bind. The parent keeps it until service closure.
                var parent = verified;
                var id = checked(++_nextSharedSection); section = new(id, source, parent);
                _sharedSections.Add(id, section); _sharedObjects.Add((source.Kind, source.Object), id);
                section.RemoteHandle = remote = (_domain ?? throw new InvalidOperationException("CNG service process is absent.")).ReceiveCngSharedSource(parent);
            }
            try
            {
                section.Entered = true;
                var bind = SharedPayload(writer =>
                {
                    writer.Write((uint)NativePluginCngServiceStep.SharedBind); writer.Write(section.Id); writer.Write(remote); writer.Write(source.Maximum);
                    writer.Write(source.View); writer.Write(source.Address); writer.Write(source.Length); writer.Write(source.Offset);
                });
                using var output = Reader((_domain ?? throw new InvalidOperationException("CNG service process is absent.")).CngSystemExchange(bind));
                if (output.ReadUInt64() != section.Id || output.ReadUInt64() != source.View || output.ReadUInt32() != source.Address ||
                    output.ReadUInt32() != source.Length || output.ReadUInt32() != source.Offset)
                    throw new InvalidDataException("Actual shared service view changed original pointer/offset/extent identity.");
                Finish(output); section.ServiceAddresses.Add(source.View, source.Address);
                section.ServiceAddress = source.Address;
            }
            catch (Exception failure)
            {
                _sharedFailed = true;
                throw (_domain?.FailCngSharedLifetime(failure) ?? failure);
            }
            section.References.Add(source.View, verified); _failedSharedReferences.Remove(verified);
            section.Views.Add(source.View, source); _sharedViews.Add((source.Kind, source.View), section.Id);
            _sharedReceipts.Add(SharedReceipt(call, section, source));
            reply = SharedPayload(writer => { writer.Write(section.Id); writer.Write(source.View); }); return true;
        }
        if (step == NativePluginCngServiceStep.SharedRelease)
        {
            var id = input.ReadUInt64(); var kind = input.ReadUInt32(); var view = input.ReadUInt64(); Finish(input);
            if (!_sharedViews.TryGetValue((kind, view), out var existing) || id != existing || !_sharedSections.TryGetValue(id, out var section) ||
                !section.Views.TryGetValue(view, out var source) || section.ReleaseEntered ||
                _sharedHashObjects.Values.Any(pointer => pointer.Section == id && pointer.View == view) ||
                _sharedInvocations.Values.Any(invocation => SharedPointers(invocation).Any(pointer => pointer.Section == id && pointer.View == view)))
                throw new InvalidDataException("Shared source free/unmap would retire an absent view or live CNG object/invocation.");
            var last = section.Views.Count == 1;
            if (last) section.ReleaseEntered = true;
            var release = SharedPayload(writer => { writer.Write((uint)NativePluginCngServiceStep.SharedRelease); writer.Write(id); writer.Write(view); });
            using var output = Reader((_domain ?? throw new InvalidOperationException("CNG service process is absent.")).CngSystemExchange(release));
            if (output.ReadUInt64() != id || output.ReadUInt64() != view || output.ReadUInt32() != 1)
                throw new InvalidDataException("Actual shared service view/handle did not retire.");
            Finish(output);
            if (last)
            {
                section.ServiceViewRetired = true;
                section.Parent.CloseChecked(); section.ParentHandleRetired = true;
                _sharedObjects.Remove((kind, section.Object));
            }
            if (section.References.TryGetValue(view, out var reference) && !ReferenceEquals(reference, section.Parent)) reference.CloseChecked();
            section.Views.Remove(view); _sharedViews.Remove((kind, view)); _sharedReceipts.Add(SharedReceipt(call, section, source, true));
            reply = SharedPayload(writer => { writer.Write(id); writer.Write(view); writer.Write(1U); }); return true;
        }
        if (step == NativePluginCngServiceStep.SharedBegin)
        {
            if (actualUnload || _sharedFailed) throw new InvalidDataException("Original detach/failed shared owner cannot begin a new CNG operation.");
            var operation = (NativePluginCryptoOperation)input.ReadUInt32(); var target = input.ReadUInt64();
            var flags = input.ReadUInt32(); var error = input.ReadUInt32(); var objectBytes = input.ReadUInt32(); var destination = Presence(input);
            var copied = Presence(input); _ = input.ReadUInt32(); var buffers = new NativePluginCngBufferExtent[4];
            var pointers = new NativePluginCngSharedPointer?[4];
            for (var at = 0; at < buffers.Length; ++at)
            {
                var present = Presence(input); var length = input.ReadUInt32(); var mode = input.ReadUInt32();
                if (mode > 2 || (mode != 0) != present) throw new InvalidDataException("CNG shared buffer mode changed its source pointer presence.");
                buffers[at] = new(present, length); if (mode == 2) pointers[at] = ReadSharedPointer(input, length);
            }
            var storage = Presence(input) ? ReadSharedPointer(input, objectBytes) : null;
            var size = Presence(input) ? ReadSharedPointer(input, sizeof(uint)) : null; Finish(input);
            if (!Enum.IsDefined(operation) || operation == NativePluginCryptoOperation.OpenAlgorithm && target != 0 ||
                operation != NativePluginCryptoOperation.OpenAlgorithm && !_handles.ContainsKey(target) ||
                storage is not null && operation != NativePluginCryptoOperation.CreateHash || size is not null && !copied)
                throw new InvalidDataException("Shared CNG SDK target/object storage is foreign, retired or not source-correlated.");
            var invocation = new SharedInvocation(call, operation, target, flags, error, objectBytes, destination, copied, buffers, pointers, storage, size);
            using var output = Reader((_domain ?? throw new InvalidOperationException("CNG service process is absent.")).CngSystemExchange(request));
            var id = output.ReadUInt64(); Finish(output);
            if (id == 0 || !_sharedInvocations.TryAdd(id, invocation) || _invocations.ContainsKey(id))
                throw new InvalidDataException("CNG shared SDK invocation identity repeated.");
            reply = BitConverter.GetBytes(id); return true;
        }
        if (step is not (NativePluginCngServiceStep.Write or NativePluginCngServiceStep.Execute or NativePluginCngServiceStep.Read or NativePluginCngServiceStep.Release)) return false;
        var invocationId = input.ReadUInt64();
        if (!_sharedInvocations.TryGetValue(invocationId, out var active)) return false;
        if (active.Call != call || active.Result is not null && (step is NativePluginCngServiceStep.Write or NativePluginCngServiceStep.Execute) ||
            active.Result is null && (step is NativePluginCngServiceStep.Read or NativePluginCngServiceStep.Release))
            throw new InvalidDataException("Shared CNG call/result prefix changed or attempted SDK replay.");
        uint role = 0, count = 0;
        if (step == NativePluginCngServiceStep.Write)
        {
            role = input.ReadUInt32(); var offset = input.ReadUInt32(); count = input.ReadUInt32();
            if (role >= 4 || active.Pointers[role] is not null || !active.Buffers[role].Present || offset != active.Written[role] ||
                count > active.Buffers[role].Length - offset || count > input.BaseStream.Length - input.BaseStream.Position)
                throw new InvalidDataException("Shared CNG upload targets direct memory or skips its actual copied input extent.");
            _ = input.ReadBytes(checked((int)count)); Finish(input);
        }
        else if (step == NativePluginCngServiceStep.Read)
        {
            var offset = input.ReadUInt32(); count = input.ReadUInt32(); Finish(input);
            if (active.Pointers[3] is not null || !active.Buffers[3].Present || offset != active.Read || count > active.Buffers[3].Length - offset)
                throw new InvalidDataException("Shared CNG result requested a copied mirror of actual direct caller storage.");
        }
        else
        {
            Finish(input);
            if (step == NativePluginCngServiceStep.Execute && active.Buffers.Where((buffer, at) => buffer.Length != active.Written[at]).Any() ||
                step == NativePluginCngServiceStep.Release && active.Pointers[3] is null && active.Buffers[3].Present && active.Read != active.Buffers[3].Length)
                throw new InvalidDataException("Shared CNG SDK entry/result release has incomplete actual copied buffers.");
        }
        var response = (_domain ?? throw new InvalidOperationException("CNG service process is absent.")).CngSystemExchange(request);
        using var result = Reader(response);
        if (step == NativePluginCngServiceStep.Execute)
        {
            var value = new NativePluginCngServiceResult(result.ReadInt32(), result.ReadUInt32(), result.ReadUInt64(), Presence(result), result.ReadUInt32(), result.ReadUInt32()); Finish(result);
            if (value.CopiedPresent != active.CopiedPresent || value.OutputLength != active.Buffers[3].Length ||
                value.Created != 0 && (value.Status < 0 || active.Operation is not (NativePluginCryptoOperation.OpenAlgorithm or NativePluginCryptoOperation.CreateHash)))
                throw new InvalidDataException("Shared CNG SDK receipt changed the actual original signature.");
            active.Result = value;
            if (value.Status >= 0)
            {
                if (active.Operation is NativePluginCryptoOperation.OpenAlgorithm or NativePluginCryptoOperation.CreateHash)
                {
                    if (value.Created == 0 || !_handles.TryAdd(value.Created, active.Operation == NativePluginCryptoOperation.CreateHash))
                        throw new InvalidDataException("Actual shared CNG constructor did not publish one new SDK capability.");
                    _pendingPublication.Add(value.Created, call);
                    if (active.Object is { } storage) _sharedHashObjects.Add(value.Created, storage);
                }
                else if (active.Operation is NativePluginCryptoOperation.DestroyHash or NativePluginCryptoOperation.CloseAlgorithm)
                { _handles.Remove(active.Target); _sharedHashObjects.Remove(active.Target); }
            }
            _sharedCalls.Add(new(call, invocationId, active.Operation, value, Array.AsReadOnly(active.Pointers), active.Object, active.Copied));
            _receipts.Add(new(checked((ulong)_receipts.Count + 1), _originalGeneration, _originalModule, call, _originalThread,
                Generation, ProcessId, invocationId, active.Operation, active.Target, value.Created, value.Status, value.LastError,
                value.CopiedPresent, value.Copied, value.OutputLength, _sources.Provider.SourceOwner, _sources.Primitives.SourceOwner,
                active.Flags, active.Error, active.ObjectBytes, active.Destination, Array.AsReadOnly(active.Buffers)));
        }
        else if (step == NativePluginCngServiceStep.Write)
        { if (result.ReadUInt32() != active.Written[role] + count) throw new InvalidDataException("CNG copied source upload changed its exact extent."); Finish(result); active.Written[role] += count; }
        else if (step == NativePluginCngServiceStep.Read)
        { if (result.ReadUInt32() != count || result.ReadBytes(checked((int)count)).Length != count) throw new InvalidDataException("CNG copied output extent changed."); Finish(result); active.Read += count; }
        else
        { if (result.ReadUInt32() != 1) throw new InvalidDataException("Shared CNG invocation did not release."); Finish(result); _sharedInvocations.Remove(invocationId); }
        reply = response; return true;
    }
    private bool ForwardSharedChecked(ulong call, byte[] request, bool actualUnload, out byte[] reply)
    {
        try { return TryForwardShared(call, request, actualUnload, out reply); }
        catch { _sharedFailed = true; throw; }
    }
    private NativePluginCngSharedPointer ReadSharedPointer(BinaryReader reader, uint length)
    {
        var pointer = new NativePluginCngSharedPointer(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt32(), length);
        if (!_sharedSections.TryGetValue(pointer.Section, out var section) || section.ReleaseEntered || section.ServiceViewRetired ||
            !section.Views.TryGetValue(pointer.View, out var view) || pointer.Offset < view.Offset ||
            pointer.Offset - view.Offset >= view.Length || length > view.Length - (pointer.Offset - view.Offset))
            throw new InvalidDataException("Shared CNG pointer lost its exact original view/range capability.");
        return pointer;
    }
    private static IEnumerable<NativePluginCngSharedPointer> SharedPointers(SharedInvocation call) =>
        call.Pointers.Concat([call.Object, call.Copied]).OfType<NativePluginCngSharedPointer>();
    private NativePluginCngSharedReceipt SharedReceipt(ulong call, NativePluginCngSharedSection section, NativePluginCngSharedSource source, bool viewRetired = false) =>
        new(_originalGeneration, call, Generation, ProcessId, section.Id, source, section.ServiceAddresses.GetValueOrDefault(source.View), section.RemoteHandle,
            viewRetired, section.ParentHandleRetired);
    private void RequireSharedRetired()
    {
        if (_sharedFailed || _sharedViews.Count != 0 || _sharedInvocations.Count != 0 || _sharedHashObjects.Count != 0 || !SharedOwnersRetired)
            throw new InvalidDataException("CNG provider retirement retains failed/shared original buffers or invocation lifetimes.");
    }
    private void SharedHandleRetired(ulong id) => _sharedHashObjects.Remove(id);
    private void ReleaseSharedAfterServiceExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Shared CNG parent handles must survive until exact service closure.");
        var failures = new List<Exception>();
        foreach (var reference in _sharedSections.Values.SelectMany(section => section.References.Values.Append(section.Parent))
            .Concat(_failedSharedReferences).Distinct())
        {
            try { reference.CloseChecked(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        foreach (var section in _sharedSections.Values) section.ParentHandleRetired = section.Parent.IsClosed;
        if (failures.Count != 0) throw new AggregateException("Shared CNG independent parent references failed retirement.", failures);
        // Keep source declarations and failed SDK/unmap prefixes observable.
    }
}
