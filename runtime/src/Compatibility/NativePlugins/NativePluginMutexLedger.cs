using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// The child calls Windows. This owner correlates actual SDK results with the
// source caller and tracks only objects/refs admitted in this process generation.
internal sealed class NativePluginMutexLedger(ulong generation, ulong module, uint thread)
{
    private readonly Dictionary<ulong, NativePluginMutexLiveHandle> _handles = [];
    private readonly List<NativePluginMutexReceipt> _receipts = [];
    private readonly List<NativePluginMutexDetachReceipt> _detachReceipts = [];
    private ulong _next, _pendingId, _pendingCall, _detachCall, _detachSequence;
    private NativePluginMutexRequest? _pending;
    private bool _detachEntered, _detachReturned;
    internal IReadOnlyList<NativePluginMutexReceipt> Receipts => _receipts.AsReadOnly();
    internal IReadOnlyList<NativePluginMutexDetachReceipt> DetachReceipts => _detachReceipts.AsReadOnly();
    internal NativePluginMutexPending? Pending => _pending is { } request ? new(_pendingId, _pendingCall, request) : null;
    internal bool PendingConstruction => _pending is { } request && (IsCreate(request.Api) || request.Api is NativePluginMutexApi.OpenA or NativePluginMutexApi.OpenW);
    private static bool IsCreate(NativePluginMutexApi api) => api is NativePluginMutexApi.CreateA or NativePluginMutexApi.CreateW or
        NativePluginMutexApi.CreateExA or NativePluginMutexApi.CreateExW;
    internal IReadOnlyList<NativePluginMutexHandle> LiveHandles => _handles.Values.Select(value => new NativePluginMutexHandle(value.Capability, value.Handle)).ToArray();
    internal ulong Begin(ulong call, NativePluginMutexRequest request)
    {
        if (call == 0 || request.Thread != thread || _pending is not null || _detachEntered)
            throw new InvalidOperationException("Mutex request has a foreign/retiring caller or an unfinished SDK prefix.");
        ValidateRequest(request);
        _pending = request with { Handles = request.Handles.ToImmutableArray() };
        _pendingId = checked(++_next); _pendingCall = call; return _pendingId;
    }
    private void ValidateRequest(NativePluginMutexRequest request)
    {
        if (!Enum.IsDefined(request.Api) || request.Inherit > 1 || request.WaitAll > 1 || request.Alertable > 1)
            throw new InvalidDataException("Mutex signature has unknown ABI flags.");
        if (request.Inherit != 0 || request.SecurityDescriptor != 0 || request.Alertable != 0)
            throw new NotSupportedException("Inherited/custom-security/APC mutex lifetimes are not owned.");
        if (request.Security == 0 ? request.SecurityLength != 0 : request.SecurityLength != 12)
            throw new InvalidDataException("Mutex SECURITY_ATTRIBUTES does not carry the complete public x86 declaration.");
        var create = IsCreate(request.Api);
        var open = request.Api is NativePluginMutexApi.OpenA or NativePluginMutexApi.OpenW;
        if (create || open)
        {
            if (request.Handles.Count != 0 || request.Timeout != 0 || request.WaitAll != 0 || request.Flags > 1 ||
                open && (request.Flags != 0 || request.Security != 0 || request.Name is null))
                throw new InvalidDataException("Mutex construction changed its source signature.");
            if (request.Api is NativePluginMutexApi.CreateA or NativePluginMutexApi.CreateW && request.Access != 0)
                throw new InvalidDataException("CreateMutex has no desired-access argument.");
        }
        else
        {
            if (request.Name is not null || request.Flags != 0 || request.Access != 0 || request.Security != 0)
                throw new InvalidDataException("Mutex handle operation contains a fabricated construction declaration.");
            var many = request.Api is NativePluginMutexApi.WaitMany or NativePluginMutexApi.WaitManyEx;
            if (request.Handles.Count is < 1 or > 64 || !many && request.Handles.Count != 1 ||
                request.Handles.Distinct().Count() != request.Handles.Count || !many && request.WaitAll != 0)
                throw new InvalidDataException("Mutex wait/handle array is empty, repeated or exceeds the public SDK extent.");
            if (request.Api is NativePluginMutexApi.Release or NativePluginMutexApi.Close && request.Timeout != 0)
                throw new InvalidDataException("Mutex release/close acquired a fabricated timeout.");
            foreach (var handle in request.Handles) RequireHandle(handle);
            if (many && request.Handles.Select(handle => RequireHandle(handle).Object.Id).Distinct().Count() != request.Handles.Count)
                throw new NotSupportedException("Multiple-object mutex wait repeats aliases of the same actual kernel object.");
        }
    }
    internal void Complete(ulong call, ulong id, uint result, uint lastError, ulong? aliasCapability)
    {
        var request = _pending ?? throw new InvalidDataException("Mutex result has no entered original SDK call.");
        if (call != _pendingCall || id != _pendingId || request.Thread != thread)
            throw new InvalidDataException("Mutex result changed its actual call/sequence/thread.");
        var create = IsCreate(request.Api);
        var open = request.Api is NativePluginMutexApi.OpenA or NativePluginMutexApi.OpenW;
        if (create || open)
        {
            if (result == uint.MaxValue) throw new InvalidDataException("Mutex constructor returned a pseudo handle.");
            if (result != 0)
            {
                if (_handles.Values.Any(value => value.Handle == result))
                    throw new InvalidDataException("Mutex constructor repeats a still-live actual handle.");
                var existing = open || lastError == 183;
                NativePluginMutexObject obj;
                if (existing)
                {
                    if (aliasCapability is not { } alias || !_handles.TryGetValue(alias, out var known))
                    {
                        Record(id, call, request, id, 0, result, result, lastError, false, "foreign-existing-object-refused; actual-created-handle-retained-by-child");
                        throw new NotSupportedException("Actual named mutex already exists without this generation's retained Windows object identity.");
                    }
                    obj = known.Object;
                }
                else
                {
                    if (aliasCapability is not null) throw new InvalidDataException("A genuinely new mutex aliases a known object.");
                    obj = new(id);
                    if (request.Flags == 1) { obj.OwnerThread = thread; obj.Depth = 1; }
                }
                obj.Handles.Add(id); _handles.Add(id, new(id, result, obj));
                Record(id, call, request, id, obj.Id, result, result, lastError, false, existing ? "actual-owned-object-alias" : "actual-new-object");
            }
            else
            {
                if (aliasCapability is not null) throw new InvalidDataException("Failed mutex constructor was assigned an object.");
                Record(id, call, request, id, 0, 0, result, lastError, false, "actual-windows-construction-failure");
            }
        }
        else Apply(id, call, request, result, lastError, false);
        _pending = null; _pendingId = _pendingCall = 0;
    }
    private void Apply(ulong sequence, ulong call, NativePluginMutexRequest request, uint result, uint lastError, bool detach)
    {
        if (request.Api is NativePluginMutexApi.Release or NativePluginMutexApi.Close)
        {
            var value = RequireHandle(request.Handles[0]);
            // Win32 BOOL is any nonzero value. Do not replace SDK errors with a
            // synthetic success/failure or normalize successful last-error bytes.
            if (result != 0)
            {
                if (request.Api == NativePluginMutexApi.Release)
                {
                    if (value.Object.OwnerThread != thread || value.Object.Depth == 0)
                        throw new InvalidDataException("Successful Windows ReleaseMutex contradicts the retained acquisition history.");
                    if (--value.Object.Depth == 0) value.Object.OwnerThread = 0;
                }
                else { _handles.Remove(value.Capability); value.Object.Handles.Remove(value.Capability); }
            }
            Record(sequence, call, request, value.Capability, value.Object.Id, value.Handle, result, lastError, detach,
                result == 0 ? "actual-windows-operation-failure" : request.Api == NativePluginMutexApi.Close ? "local-handle-closed; global-object-lifetime-unobserved" : "thread-acquisition-released");
            return;
        }
        if (result is not (258 or uint.MaxValue))
        {
            var many = request.Api is NativePluginMutexApi.WaitMany or NativePluginMutexApi.WaitManyEx;
            var abandoned = result >= 0x80 && result < 0x80 + request.Handles.Count;
            var index = abandoned ? result - 0x80 : result;
            if (index >= request.Handles.Count || !many && index != 0)
                throw new InvalidDataException("Mutex wait has an impossible non-alertable Windows result.");
            IReadOnlyList<NativePluginMutexHandle> acquired = request.WaitAll != 0 ? request.Handles : [request.Handles[checked((int)index)]];
            foreach (var handle in acquired)
            {
                var obj = RequireHandle(handle).Object;
                if (obj.Depth != 0 && obj.OwnerThread != thread)
                    throw new InvalidDataException("Windows mutex acquisition contradicts this generation's thread ownership.");
                obj.OwnerThread = thread; obj.Depth = checked(obj.Depth + 1);
            }
        }
        foreach (var handle in request.Handles)
        {
            var value = RequireHandle(handle);
            Record(sequence, call, request, value.Capability, value.Object.Id, value.Handle, result, lastError, detach,
                result == 258 ? "actual-timeout" : result == uint.MaxValue ? "actual-wait-failure" : "actual-thread-acquisition");
        }
    }
    internal void EnterDetach(ulong call, uint image)
    {
        if (call == 0 || image == 0 || _pending is not null || _detachEntered)
            throw new InvalidOperationException("Mutex detach cannot repeat or abandon an entered SDK call.");
        _detachEntered = true; _detachCall = call;
        _detachReceipts.Add(new(generation, module, call, image, false, null, null));
    }
    internal void ReturnDetach(ulong call, uint image, uint result, uint lastError)
    {
        if (!_detachEntered || _detachReturned || call != _detachCall || _detachReceipts[0].Image != image)
            throw new InvalidDataException("Mutex detach return has no exact original FreeLibrary entry.");
        _detachReturned = true; _detachReceipts.Add(new(generation, module, call, image, true, result, lastError));
    }
    internal void Deferred(ulong call, ulong sequence, uint callbackThread, NativePluginMutexApi api,
        NativePluginMutexHandle handle, uint incomingError, uint result, uint lastError)
    {
        if (!_detachReturned || call != _detachCall || callbackThread != thread || sequence != _detachSequence + 1 ||
            api is not (NativePluginMutexApi.Release or NativePluginMutexApi.Close))
            throw new InvalidDataException("Deferred mutex receipt changed its original detach/thread/order.");
        var request = new NativePluginMutexRequest(api, thread, incomingError, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, [handle]);
        Apply(checked(++_next), call, request, result, lastError, true); _detachSequence = sequence;
    }
    private NativePluginMutexLiveHandle RequireHandle(NativePluginMutexHandle handle)
    {
        if (!_handles.TryGetValue(handle.Capability, out var value) || value.Handle != handle.Handle || handle.Handle is 0 or uint.MaxValue)
            throw new InvalidDataException("Mutex call has a foreign, stale or retired actual handle capability.");
        return value;
    }
    private void Record(ulong sequence, ulong call, NativePluginMutexRequest request, ulong capability, ulong obj,
        uint handle, uint result, uint lastError, bool detach, string disposition) =>
        _receipts.Add(new(generation, module, sequence, call, thread, request.Api, request.Name, capability, obj, handle, result, lastError, detach, disposition, request));
    internal void RequireRetired()
    {
        if (_handles.Count != 0 || _pending is not null || _detachEntered && !_detachReturned)
            throw new InvalidDataException("Mutex retirement retains real handles or an unfinished Windows/loader call.");
        if (_detachReturned && _detachReceipts[^1].Result == 0)
            throw new InvalidDataException("Actual original FreeLibrary failed; mutex source lifetime cannot be retired naturally.");
    }
}
