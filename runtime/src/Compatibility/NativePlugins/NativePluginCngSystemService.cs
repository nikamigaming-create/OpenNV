using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed class NativePluginCngServiceFaultException(string message, NativePluginCngSystemService owner, Exception inner)
    : InvalidOperationException(message, inner)
{
    internal NativePluginCngSystemService Owner { get; } = owner;
}

// Only first-party code runs in this separate, ordinary-authority process. Its
// compiled dispatch admits Hello/CNG49/Retire; it cannot enter an original DLL.
internal sealed partial class NativePluginCngSystemService
{
    private sealed record Invocation(ulong OriginalCall, NativePluginCryptoOperation Operation, ulong Target,
        bool CopiedPresent, uint OutputLength, uint Flags, uint CallerLastError, uint ObjectBytes,
        bool DestinationPresent, NativePluginCngBufferExtent[] Buffers)
    {
        internal NativePluginCngServiceResult? Result { get; set; }
        internal uint[] Written { get; } = Buffers.Select(buffer => buffer.Present ? 0U : buffer.Length).ToArray();
        internal uint Read { get; set; }
    }
    private readonly NativePluginCngSystemSources _sources;
    private NativePluginExecutionDomain? _domain;
    private readonly Dictionary<ulong, Invocation> _invocations = [];
    private readonly Dictionary<ulong, bool> _handles = [];
    private readonly Dictionary<ulong, ulong> _pendingPublication = [];
    private readonly Dictionary<uint, ulong> _nativeAddresses = [];
    private readonly List<NativePluginCngLocalPublication> _publications = [];
    private readonly List<NativePluginCngServiceReceipt> _receipts = [];
    private readonly List<NativePluginCngEmergencyReceipt> _emergency = [];
    private readonly ulong _originalGeneration, _originalModule;
    private readonly uint _originalThread;
    private bool _providerRetired, _sourcesRetired, _emergencyEntered;
    private Exception? _retirementError;
    internal ulong Generation => _domain?.Generation ?? 0;
    internal int ProcessId => _domain?.ProcessId ?? 0;
    internal bool ChildExited => _domain?.ChildExited ?? true;
    internal bool ResourcesRetired => _sourcesRetired && SharedOwnersRetired && (_domain?.CngServiceProcessResourcesRetired ?? true);
    internal bool OrderlyRetired => !_emergencyEntered && (_providerRetired || _memoryOnlyRetired) && (_domain?.NaturallyRetired ?? false) && ChildExited && _sourcesRetired && SharedOwnersRetired && !_sharedFailed;
    internal IReadOnlyList<NativePluginCngServiceReceipt> Receipts => _receipts.AsReadOnly();
    internal IReadOnlyList<NativePluginCngEmergencyReceipt> EmergencyReceipts => _emergency.AsReadOnly();
    internal IReadOnlyList<NativePluginCngLocalPublication> LocalPublications => _publications.AsReadOnly();
    internal NativePluginCngSystemService(NativePluginCngServiceImage image, NativePluginCryptoProviderSource provider,
        ulong originalGeneration, ulong originalModule, uint originalThread)
    {
        if (originalGeneration == 0 || originalModule == 0 || originalThread == 0)
            throw new ArgumentException("CNG service requires one actual original caller generation/module/thread.");
        _originalGeneration = originalGeneration; _originalModule = originalModule; _originalThread = originalThread;
        _sources = new(image, provider);
        try
        {
            _domain = new NativePluginExecutionDomain(_sources.Image.Path);
            _sources.RequireCurrent();
            // Windows memory-only lifetime. No SDK provider is admitted
            // until PrepareSdkForActualConsumer enters a genuine CNG call.
        }
        catch (Exception error)
        {
            if (error is NativePluginDomainFaultException failure) _domain = failure.Owner;
            var failures = new List<Exception> { error };
            try { _domain?.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (ChildExited) { _sources.Dispose(); _sourcesRetired = true; }
            throw new NativePluginCngServiceFaultException("CNG system-provider construction failed with retained exact owners.", this,
                failures.Count == 1 ? error : new AggregateException(failures));
        }
    }
    internal byte[] Forward(ulong originalCall, byte[] request, bool actualUnload = false)
    {
        if (originalCall == 0 || request.Length < 4 || _providerRetired || _sourcesRetired)
            throw new InvalidDataException("CNG forwarding lost its current original invocation/provider owner.");
        _sources.RequireCurrent(); RequireDetachStep(originalCall, request, actualUnload);
        PrepareSdkForActualConsumer(request);
        if (ForwardSharedChecked(originalCall, request, actualUnload, out var sharedReply))
        { if (!_sourcesRetired) _sources.RequireCurrent(); return sharedReply; }
        using var input = Reader(request); var step = (NativePluginCngServiceStep)input.ReadUInt32();
        if (step is NativePluginCngServiceStep.Prepare or NativePluginCngServiceStep.AbandonAfterClientExit || !Enum.IsDefined(step))
            throw new NotSupportedException("Original CNG caller cannot construct arbitrary providers or claim child closure.");
        if (step == NativePluginCngServiceStep.PublishLocal)
        {
            var handle = input.ReadUInt64(); var address = input.ReadUInt32(); var destination = input.ReadUInt32();
            var hash = Presence(input); var readback = input.ReadUInt32(); Finish(input);
            if (!_pendingPublication.TryGetValue(handle, out var call) || call != originalCall ||
                !_handles.TryGetValue(handle, out var kind) || kind != hash || address == 0 || destination == 0 || readback != address ||
                !_nativeAddresses.TryAdd(address, handle))
                throw new InvalidDataException("CNG native capability/output readback is foreign, repeated or not source-correlated.");
            _pendingPublication.Remove(handle); _publications.Add(new(originalCall, handle, address, destination, hash));
            return BitConverter.GetBytes(1U);
        }
        Invocation? invocation = null; ulong id = 0; uint bufferRole = 0, transferCount = 0;
        if (step == NativePluginCngServiceStep.Begin)
        {
            var operation = (NativePluginCryptoOperation)input.ReadUInt32(); var target = input.ReadUInt64();
            var flags = input.ReadUInt32(); var callerError = input.ReadUInt32(); var objectBytes = input.ReadUInt32(); var destination = Presence(input);
            var copied = Presence(input); _ = input.ReadUInt32(); uint outputLength = 0;
            var buffers = new NativePluginCngBufferExtent[4];
            for (var at = 0; at < 4; ++at)
            { var present = Presence(input); var length = input.ReadUInt32(); buffers[at] = new(present, length); if (at == 3) outputLength = length; }
            Finish(input);
            if (!Enum.IsDefined(operation) || operation == NativePluginCryptoOperation.OpenAlgorithm && target != 0 ||
                operation != NativePluginCryptoOperation.OpenAlgorithm && !_handles.ContainsKey(target))
                throw new InvalidDataException("CNG target is foreign, retired or unowned by this original module.");
            invocation = new(originalCall, operation, target, copied, outputLength, flags, callerError, objectBytes, destination, buffers);
        }
        else if (step is NativePluginCngServiceStep.Write or NativePluginCngServiceStep.Execute or NativePluginCngServiceStep.Read or NativePluginCngServiceStep.Release)
        {
            id = input.ReadUInt64();
            if (!_invocations.TryGetValue(id, out invocation) || invocation.OriginalCall != originalCall)
                throw new InvalidDataException("CNG upload/call/result/release belongs to another original call prefix.");
            if (step is NativePluginCngServiceStep.Execute && invocation.Result is not null ||
                step is NativePluginCngServiceStep.Write && invocation.Result is not null ||
                step is NativePluginCngServiceStep.Read or NativePluginCngServiceStep.Release && invocation.Result is null)
                throw new InvalidDataException("CNG result publication or SDK entry cannot replay or precede execution.");
            if (step == NativePluginCngServiceStep.Write)
            {
                bufferRole = input.ReadUInt32(); var offset = input.ReadUInt32(); transferCount = input.ReadUInt32();
                if (bufferRole >= 4 || !invocation.Buffers[bufferRole].Present || offset != invocation.Written[bufferRole] ||
                    transferCount > invocation.Buffers[bufferRole].Length - offset || transferCount > input.BaseStream.Length - input.BaseStream.Position)
                    throw new InvalidDataException("CNG parent buffer lease saw skipped/repeated or oversized original input.");
                _ = input.ReadBytes(checked((int)transferCount)); Finish(input);
            }
            else if (step == NativePluginCngServiceStep.Read)
            {
                var offset = input.ReadUInt32(); transferCount = input.ReadUInt32(); Finish(input);
                if (!invocation.Buffers[3].Present || offset != invocation.Read || transferCount > invocation.OutputLength - offset)
                    throw new InvalidDataException("CNG parent result lease saw skipped/repeated or oversized output.");
            }
            else
            {
                Finish(input);
                if (step == NativePluginCngServiceStep.Execute && invocation.Buffers.Where((buffer, at) => buffer.Length != invocation.Written[at]).Any() ||
                    step == NativePluginCngServiceStep.Release && invocation.Buffers[3].Present && invocation.Read != invocation.OutputLength)
                    throw new InvalidDataException("CNG SDK entry/result retirement has incomplete actual byte transfer.");
            }
        }
        else if (step == NativePluginCngServiceStep.Retire)
        {
            Finish(input);
            RequireSharedRetired();
            if (_handles.Count != 0 || _invocations.Count != 0 || _pendingPublication.Count != 0)
                throw new InvalidDataException("Original CNG retirement retains live handle/result capabilities.");
        }
        var reply = (_domain ?? throw new InvalidOperationException("CNG child owner is absent.")).CngSystemExchange(request);
        using var output = Reader(reply);
        if (step == NativePluginCngServiceStep.Begin)
        {
            id = output.ReadUInt64(); Finish(output);
            if (id == 0 || !_invocations.TryAdd(id, invocation!)) throw new InvalidDataException("CNG invocation ID is absent or repeated.");
        }
        else if (step == NativePluginCngServiceStep.Execute)
        {
            var result = new NativePluginCngServiceResult(output.ReadInt32(), output.ReadUInt32(), output.ReadUInt64(),
                Presence(output), output.ReadUInt32(), output.ReadUInt32()); Finish(output);
            if (result.CopiedPresent != invocation!.CopiedPresent || result.OutputLength != invocation.OutputLength ||
                result.Created != 0 && (result.Status < 0 || invocation.Operation is not (NativePluginCryptoOperation.OpenAlgorithm or NativePluginCryptoOperation.CreateHash)))
                throw new InvalidDataException("Actual Windows CNG result changed the caller signature/capability kind.");
            invocation.Result = result;
            if (result.Status >= 0)
            {
                if (invocation.Operation is NativePluginCryptoOperation.OpenAlgorithm or NativePluginCryptoOperation.CreateHash)
                {
                    if (result.Created == 0 || !_handles.TryAdd(result.Created, invocation.Operation == NativePluginCryptoOperation.CreateHash))
                        throw new InvalidDataException("Windows CNG construction lacks one new owned capability.");
                    _pendingPublication.Add(result.Created, originalCall);
                }
                else if (invocation.Operation is NativePluginCryptoOperation.DestroyHash or NativePluginCryptoOperation.CloseAlgorithm)
                { _handles.Remove(invocation.Target); SharedHandleRetired(invocation.Target); }
            }
            _receipts.Add(new(checked((ulong)_receipts.Count + 1), _originalGeneration, _originalModule, originalCall, _originalThread,
                Generation, ProcessId, id, invocation.Operation, invocation.Target, result.Created, result.Status, result.LastError,
                result.CopiedPresent, result.Copied, result.OutputLength, _sources.Provider.SourceOwner, _sources.Primitives.SourceOwner,
                invocation.Flags, invocation.CallerLastError, invocation.ObjectBytes, invocation.DestinationPresent, Array.AsReadOnly(invocation.Buffers)));
        }
        else if (step == NativePluginCngServiceStep.Write)
        {
            if (output.ReadUInt32() != invocation!.Written[bufferRole] + transferCount)
                throw new InvalidDataException("CNG service input acknowledgement disagrees with the parent's original byte extent.");
            Finish(output); invocation.Written[bufferRole] += transferCount;
        }
        else if (step == NativePluginCngServiceStep.Read)
        {
            if (output.ReadUInt32() != transferCount) throw new InvalidDataException("CNG service output acknowledgement changed its real provider extent.");
            if (output.ReadBytes(checked((int)transferCount)).Length != transferCount)
                throw new InvalidDataException("CNG service truncated actual Windows result bytes.");
            Finish(output); invocation!.Read += transferCount;
        }
        else if (step == NativePluginCngServiceStep.Release)
        {
            if (output.ReadUInt32() != 1) throw new InvalidDataException("CNG result buffers lack actual release acknowledgement.");
            Finish(output); _invocations.Remove(id);
        }
        else if (step == NativePluginCngServiceStep.Retire)
        {
            ReadRetirement(output, false); _domain.Dispose();
            if (!ChildExited || !_domain.NaturallyRetired) throw new InvalidDataException("CNG service did not retire its exact process normally.");
            _sources.RequireCurrent(); _sources.Dispose(); _sourcesRetired = true;
        }
        // Write/read extents are independently checked in the actual service and
        // in the native caller. No whole-buffer managed mirror is manufactured.
        if (!_sourcesRetired) _sources.RequireCurrent();
        return reply;
    }
    private bool ReadRetirement(BinaryReader reader, bool emergency)
    {
        var count = reader.ReadUInt32();
        if (!emergency && count != 0) throw new InvalidDataException("Orderly CNG retirement contains substituted emergency calls.");
        for (uint at = 0; at < count; ++at)
        {
            var operation = reader.ReadUInt32(); var handle = reader.ReadUInt64(); var status = reader.ReadInt32();
            if (operation is not (6 or 7) || !_handles.TryGetValue(handle, out var hash) || hash != (operation == 6))
                throw new InvalidDataException("Emergency CNG status has no real retained handle/kind.");
            _emergency.Add(new(operation, handle, status)); if (status >= 0) { _handles.Remove(handle); SharedHandleRetired(handle); }
        }
        var more = Presence(reader); var retired = Presence(reader); var hashes = reader.ReadUInt32(); var algorithms = reader.ReadUInt32();
        var released = Presence(reader); var error = reader.ReadUInt32(); Finish(reader);
        if (more)
        {
            if (!emergency || retired || released || error != 0 || count == 0 || hashes + (ulong)algorithms != (ulong)_handles.Count)
                throw new InvalidDataException("CNG cleanup batch lost actual progress/remaining handle ownership.");
            return true;
        }
        if (!retired || !released || error != 0 || hashes != 0 || algorithms != 0 || _handles.Count != 0)
            throw new InvalidDataException("Actual CNG handle/provider retirement is incomplete; source/process owners remain retained.");
        _providerRetired = true;
        return false;
    }
    internal void ReleaseAfterOriginalChildExit(bool originalChildExited)
    {
        if (!originalChildExited) throw new InvalidOperationException("CNG system resources must survive until exact original child closure.");
        if (_sourcesRetired) { ReleaseSharedAfterServiceExit(); if (_retirementError is not null) throw _retirementError; return; }
        var failures = new List<Exception>();
        if (_sdkPrepareEntered && !_providerRetired && !_emergencyEntered && _domain is { Fault: null, ChildExited: false })
        {
            _emergencyEntered = true;
            try
            {
                var request = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(request, (uint)NativePluginCngServiceStep.AbandonAfterClientExit);
                bool more;
                do { using var reader = Reader(_domain.CngSystemExchange(request)); more = ReadRetirement(reader, true); } while (more);
                _invocations.Clear(); _sharedInvocations.Clear();
            }
            catch (Exception error) { failures.Add(error); }
        }
        try { _domain?.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (!_sdkPrepareEntered && _domain is { NaturallyRetired: true, ChildExited: true }) _memoryOnlyRetired = true;
        if (ChildExited)
        {
            try { ReleaseSharedAfterServiceExit(); } catch (Exception error) { failures.Add(error); }
            _sources.RequireCurrent(); _sources.Dispose(); _sourcesRetired = true;
        }
        else failures.Add(new InvalidOperationException("Exact CNG service child is still alive; all source/provider owners are retained."));
        if (failures.Count != 0)
        {
            _retirementError ??= new NativePluginCngServiceFaultException("CNG service emergency/child retirement failed.", this, new AggregateException(failures));
            throw _retirementError;
        }
    }
    private static bool Presence(BinaryReader reader)
    {
        var value = reader.ReadUInt32(); if (value > 1) throw new InvalidDataException("CNG pointer presence is not Boolean."); return value != 0;
    }
    private static BinaryReader Reader(byte[] payload) => new(new MemoryStream(payload, false), Encoding.UTF8, false);
    private static void Finish(BinaryReader reader)
    { if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("CNG reply has unconsumed bytes."); }
    private static void Text(BinaryWriter writer, string value)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(value); writer.Write(checked((uint)bytes.Length)); writer.Write(bytes);
    }
    private static string ReadText(BinaryReader reader)
    {
        var length = reader.ReadUInt32();
        if (length > NativePluginExecutionDomain.MaximumPayload || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("CNG source text exceeds its real reply extent.");
        return new UTF8Encoding(false, true).GetString(reader.ReadBytes(checked((int)length)));
    }
}
