using System.Globalization;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint SteamCallbackEvent = 0x600;
    private readonly Dictionary<ulong, NativePluginSteamCallbackToken> _steamCallbacks = [];
    private readonly List<NativePluginSteamCallbackDelivery> _steamCallbackDeliveries = [];
    private readonly List<NativePluginSteamCallbackPrefix> _steamCallbackPrefixes = [];
    private Action<NativePluginSteamCallbackDelivery>? _steamCallbackHandler;
    private Guid _steamCallbackLease;
    private NativePluginSteamOperation? _steamEnteredOperation;
    private uint? _steamRegistering;
    private ulong _steamDeliveredSequence, _steamProvisionalCallback, _steamCallbackRequest;
    private uint _steamOperationCallbacks;
    private bool _steamHandlerEntered;
    internal object SteamSourceCallbackState => new
    {
        registrations = _steamCallbacks.Values.ToArray(),
        deliveries = _steamCallbackDeliveries.AsReadOnly(),
        nativePrefixes = _steamCallbackPrefixes.AsReadOnly(),
        entered = _steamHandlerEntered,
        registering = _steamRegistering,
        enteredOperation = _steamEnteredOperation
    };
    internal IDisposable BindSteamSourceCallbacks(Action<NativePluginSteamCallbackDelivery> handler)
    {
        RequireSteam(); ArgumentNullException.ThrowIfNull(handler);
        if (_steamCallbackLease != Guid.Empty || _steamCallbacks.Count != 0 || _steamPhase != NativePluginSteamPhase.Loaded)
            throw new InvalidOperationException("Source callback member construction requires the actual loaded, uninitialized service.");
        _steamCallbackHandler = handler; var lease = _steamCallbackLease = Guid.NewGuid(); return new SteamCallbackLease(this, lease);
    }
    private sealed class SteamCallbackLease(NativePluginExecutionDomain owner, Guid lease) : IDisposable
    {
        public void Dispose()
        {
            if (owner._steamCallbackLease != lease) return;
            owner.VerifyOwner();
            if (owner._steamCallbacks.Count != 0 || owner._steamHandlerEntered || owner._steamRegistering is not null)
                throw new InvalidOperationException("Actual callback member/handler lifetime still owns its source receiver.");
            owner._steamCallbackHandler = null; owner._steamCallbackLease = Guid.Empty;
        }
    }
    internal NativePluginSteamCallbackToken RegisterSteamSourceCallback(uint id)
    {
        RequireSteam();
        if (_steamCallbackLease == Guid.Empty || _steamRegistering is not null ||
            _steamCallbacks.Values.Any(value => value.CallbackId == id) || _steamPhase != NativePluginSteamPhase.Loaded)
            throw new InvalidOperationException("Original source callback registration was absent, entered or already committed.");
        var shape = NativePluginSteamStartupDeclaration.Read(_steamSource!.Declaration).Callbacks.SingleOrDefault(value => value.Id == id)
            ?? throw new NotSupportedException("Original platform callback family has no source receiver/extent owner.");
        _steamRegistering = shape.Id; _steamProvisionalCallback = 0;
        try
        {
            var receipt = SteamCall(NativePluginSteamOperation.RegisterCallback, argument: id.ToString(CultureInfo.InvariantCulture));
            if (receipt.Token == 0 || receipt.Result > byte.MaxValue ||
                _steamProvisionalCallback != 0 && _steamProvisionalCallback != receipt.Token)
                throw Fatal(new InvalidDataException("Actual registered callback omitted/replaced its current native object token."));
            var token = new NativePluginSteamCallbackToken(Generation, _steamSession, receipt.Token, id, checked((byte)receipt.Result));
            if (!_steamCallbacks.TryAdd(token.Token, token)) throw Fatal(new InvalidDataException("Platform callback reused a live member object."));
            return token;
        }
        finally { _steamRegistering = null; _steamProvisionalCallback = 0; }
    }
    internal void RetireSteamSourceCallback(NativePluginSteamCallbackToken token)
    {
        RequireSteam();
        if (token.Generation != Generation || token.Session != _steamSession || _steamPhase != NativePluginSteamPhase.ShutdownReturned ||
            !_steamCallbacks.TryGetValue(token.Token, out var owned) || !ReferenceEquals(owned, token))
            throw new InvalidOperationException("Original callback member retirement lost its source generation/shutdown prefix.");
        var receipt = SteamCall(NativePluginSteamOperation.UnregisterCallback, token.Token);
        if (receipt.Result != 0) throw Fatal(new InvalidDataException("Source callback member retained registration after its retirement."));
        _steamCallbacks.Remove(token.Token);
    }
    internal uint SteamApplicationId(NativePluginSteamToken token)
    { RequireSteamToken(token, NativePluginSteamInterface.Utilities); return SteamCall(NativePluginSteamOperation.ApplicationId, token.Token).Result; }
    internal void ConfirmSteamSourceApplication(uint actual)
    {
        RequireSteam();
        if (_steamReceipts.LastOrDefault(receipt => receipt.Operation == NativePluginSteamOperation.ApplicationId) is not { } receipt ||
            receipt.Result != actual || receipt.CurrentAppId != actual)
            throw new InvalidDataException("Source AppID admission lacks its actual returned scalar/store prefix.");
        if (_steamSource!.Application is not { } declared || actual != declared.AppId)
        {
            var failure = new NotSupportedException("Actual source application scalar differs from or lacks its selected installed AppState.");
            _steamFailure ??= failure; _steamPhase = NativePluginSteamPhase.Failed; throw Fatal(failure);
        }
    }
    internal bool SteamRequestCurrentStatistics(NativePluginSteamToken token)
    { RequireSteamToken(token, NativePluginSteamInterface.Statistics); return (SteamCall(NativePluginSteamOperation.RequestStatistics, token.Token).Result & 0xff) != 0; }
    private uint DispatchSteamSourceCallback(Frame frame, ulong waitingCall)
    {
        if (++_steamOperationCallbacks > 65536) throw new InvalidDataException("Actual Steam callback transaction quota exceeded.");
        if (_steamEnteredOperation is null || _steamSession == 0 || _steamCallbackLease == Guid.Empty || _steamHandlerEntered ||
            _currentCall != waitingCall || _operation != SteamProviderOperation.ToString() ||
            _steamCallbackRequest != 0 && _steamCallbackRequest != waitingCall)
            throw new InvalidDataException("Steam callback has no actual entered operation/receiver lease.");
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var session = reader.ReadUInt64(); var token = reader.ReadUInt64();
        var sequence = reader.ReadUInt64(); var id = reader.ReadUInt32(); var flags = reader.ReadUInt32();
        var alternate = SteamBoolean(reader); var ioFailure = SteamBoolean(reader); var apiCall = reader.ReadUInt64();
        var bytes = reader.ReadUInt32();
        var shape = NativePluginSteamStartupDeclaration.Read(_steamSource!.Declaration).Callbacks.SingleOrDefault(value => value.Id == id);
        if (thread != NativeThread || session != _steamSession || token == 0 || sequence != checked(_steamDeliveredSequence + 1) ||
            flags > byte.MaxValue || shape is null || bytes != shape.Bytes || !alternate && (ioFailure || apiCall != 0))
            throw new InvalidDataException("Actual Steam callback changed its thread/source/ABI/ordered payload extent.");
        var payload = reader.ReadBytes(checked((int)bytes)); Finish(reader);
        if (payload.Length != bytes) throw new EndOfStreamException("Actual callback payload was not completely delivered.");
        if (!_steamCallbacks.TryGetValue(token, out var registered))
        {
            if (_steamRegistering != id || _steamProvisionalCallback != 0 && _steamProvisionalCallback != token)
                throw new InvalidDataException("Native callback did not originate from the actual entered member constructor.");
            _steamProvisionalCallback = token;
        }
        else if (registered.CallbackId != id || registered.Generation != Generation || registered.Session != session)
            throw new InvalidDataException("Actual callback belongs to another source member lifetime.");
        // Exchange restores its ambient request before the returned receipt is
        // decoded. Retain this actual validated request for later correlation;
        // no receipt may borrow a predicted or previous transaction identity.
        _steamCallbackRequest = waitingCall; _steamDeliveredSequence = sequence;
        var delivery = new NativePluginSteamCallbackDelivery(Generation, session, token, sequence, waitingCall,
            frame.Id, id, checked((byte)flags), alternate, ioFailure, apiCall, payload);
        _steamCallbackDeliveries.Add(delivery); _steamHandlerEntered = true;
        try
        {
            (_steamCallbackHandler ?? throw new NotSupportedException("Actual source Steam callback receiver is unbound."))(delivery);
            return 1; // Sent only after the genuine C# source handler returned.
        }
        catch (Exception failure) { _steamFailure ??= failure; throw; }
        finally { _steamHandlerEntered = false; }
    }
    private IReadOnlyList<NativePluginSteamCallbackPrefix> ReadSteamCallbackPrefixes(BinaryReader reader, bool successful)
    {
        var count = reader.ReadUInt32();
        if (count > 65536) throw new InvalidDataException("Native Steam callback prefix exceeded its bounded current transaction.");
        var result = new List<NativePluginSteamCallbackPrefix>();
        for (var index = 0U; index < count; index++)
        {
            var sequence = reader.ReadUInt64(); var token = reader.ReadUInt64(); var id = reader.ReadUInt32(); var flags = reader.ReadUInt32();
            var alternate = SteamBoolean(reader); var io = SteamBoolean(reader); var apiCall = reader.ReadUInt64();
            var copied = SteamBoolean(reader); var returned = SteamBoolean(reader); var length = reader.ReadUInt32();
            var shape = NativePluginSteamStartupDeclaration.Read(_steamSource!.Declaration).Callbacks.SingleOrDefault(value => value.Id == id);
            if (sequence == 0 || token == 0 || flags > byte.MaxValue || shape is null ||
                (copied ? length != shape.Bytes : length != 0) || returned && !copied || successful && !returned ||
                !alternate && (io || apiCall != 0) || result.Count != 0 && sequence != checked(result[^1].Sequence + 1))
                throw new InvalidDataException("Native Steam callback manufactured a source return or changed its payload prefix.");
            var payload = reader.ReadBytes(checked((int)length));
            if (payload.Length != length) throw new EndOfStreamException("Native callback prefix omitted its retained bytes.");
            var value = new NativePluginSteamCallbackPrefix(sequence, token, id, checked((byte)flags), alternate, io, apiCall, copied, returned, payload);
            if (returned)
            {
                var actual = _steamCallbackDeliveries.SingleOrDefault(delivery => delivery.Sequence == sequence);
                if (actual is null || _steamCallbackRequest == 0 || actual.WaitingRequest != _steamCallbackRequest || actual.Token != token || actual.CallbackId != id ||
                    actual.Flags != flags || actual.AlternateRun != alternate || actual.IoFailure != io || actual.ApiCall != apiCall ||
                    !actual.Payload.Span.SequenceEqual(payload))
                    throw new InvalidDataException("Native callback return lacks the actual correlated C# receiver invocation.");
            }
            result.Add(value); _steamCallbackPrefixes.Add(value);
        }
        return result.AsReadOnly();
    }
    private void ClearSteamCallbacksAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Registered callback memory retires only with this exact child's exit.");
        _steamCallbacks.Clear(); _steamCallbackHandler = null; _steamCallbackLease = Guid.Empty;
        _steamRegistering = null; _steamProvisionalCallback = 0;
        // Retain all observed deliveries/native/source failure prefixes.
    }
    private IReadOnlyList<NativePluginSteamCallbackLifetime> ReadSteamCallbackLifetimes(BinaryReader reader)
    {
        var count = reader.ReadUInt32();
        if (count > 3) throw new InvalidDataException("Source callback lifetime denominator changed its reviewed three members.");
        var values = new List<NativePluginSteamCallbackLifetime>();
        for (var index = 0U; index < count; index++)
        {
            var token = reader.ReadUInt64(); var id = reader.ReadUInt32(); var flags = reader.ReadUInt32(); var live = SteamBoolean(reader);
            if (token == 0 || flags > byte.MaxValue || id is not (1101 or 1102 or 1103) ||
                values.Any(value => value.Token == token || value.CallbackId == id))
                throw new InvalidDataException("Failed native callback changed its actual source member/lifetime identity.");
            values.Add(new(token, id, checked((byte)flags), live));
        }
        return values.AsReadOnly();
    }
}
