using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint SteamSourceHashEvent = 0x601;
    private readonly List<NativePluginSteamSourceHashDelivery> _steamSourceHashDeliveries = [];
    private IReadOnlyList<NativePluginSteamSourceHashPrefix> _steamSourceHashNative = [];
    private bool _steamSourceHashEntered;
    private uint _steamOperationSourceHashes;
    private ulong _steamSourceHashSession;
    internal object SteamSourceHashState => new
    {
        entered = _steamSourceHashEntered,
        deliveries = _steamSourceHashDeliveries.AsReadOnly(),
        native = _steamSourceHashNative,
        authority = "retained-CSharp-original-file-SHA256-and-actual-kernel-identity",
        originalDllCrypto = "independent"
    };
    private byte[] DispatchSteamSourceHash(Frame frame, ulong waitingCall)
    {
        VerifyOwner();
        if (_steamSourceHashEntered || ++_steamOperationSourceHashes > 1 || _steamSourceHashDeliveries.Count >= 65 || _steamSource is null || _steamEnteredOperation is null ||
            _currentCall != waitingCall || _operation != SteamProviderOperation.ToString() || _callDepth != 1 || _exchangeDepth != 1)
            throw new InvalidDataException("Source hashing has no genuine entered platform operation/file owner.");
        _steamSource.Check(Generation);
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var schema = reader.ReadUInt32(); var session = reader.ReadUInt64();
        var ordinal = reader.ReadUInt64(); var operation = (NativePluginSteamOperation)reader.ReadUInt32();
        var kind = (NativePluginSteamSourceFileKind)reader.ReadUInt32(); var path = ReadText(reader);
        var native = NativePluginSteamFileIdentity.ReadTransport(reader); Finish(reader);
        if (thread != NativeThread || schema != 1 || session == 0 || ordinal != checked((ulong)_steamSourceHashDeliveries.Count + 1) ||
            operation != _steamEnteredOperation || !Enum.IsDefined(kind) || !Path.IsPathFullyQualified(path) ||
            _steamSession != 0 && session != _steamSession || _steamSourceHashSession != 0 && session != _steamSourceHashSession ||
            kind == NativePluginSteamSourceFileKind.Provider && operation != NativePluginSteamOperation.Load ||
            kind == NativePluginSteamSourceFileKind.Callable && operation is not (NativePluginSteamOperation.LoggedOn or
                NativePluginSteamOperation.SetAchievement or NativePluginSteamOperation.StoreStatistics or
                NativePluginSteamOperation.ApplicationId or NativePluginSteamOperation.RequestStatistics))
            throw new InvalidDataException("Actual original-file hash changed its source/thread/session/ordered caller scope.");
        var physical = Path.GetFullPath(path);
        var index = _steamSourceHashDeliveries.Count;
        _steamSourceHashDeliveries.Add(new(Generation, session, ordinal, waitingCall, frame.Id, operation, kind, physical, native));
        _steamSourceHashEntered = true; _steamSourceHashSession = session;
        try
        {
            string sha; FileStream input;
            if (kind == NativePluginSteamSourceFileKind.Provider)
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(physical, _steamSource.ProviderPath) || _steamPhase != NativePluginSteamPhase.LoadEntered)
                    throw new InvalidDataException("Initial native loader hashing changed its selected original provider.");
                input = _steamSource.RetainedProviderInput(Generation);
                var actual = NativePluginSteamFileIdentity.Read(input);
                _steamSourceHashDeliveries[index] = _steamSourceHashDeliveries[index] with { Managed = actual };
                sha = _steamSource.HashRetainedProvider(Generation, native);
            }
            else
            {
                if (!_steamCallableSources.TryGetValue(physical, out var retained))
                {
                    // Hashing occurs in the existing retained original-image
                    // owner. Publish its real lease before identity comparison
                    // can refuse; child-exit closure owns this committed prefix.
                    input = NativeNvseHostSource.LeaseHashed(physical, true, out sha, checked((long)NativePluginSteamFileIdentity.MaximumImageBytes));
                    try { _steamCallableSources.Add(physical, (sha, input)); }
                    catch { input.Dispose(); throw; }
                }
                else { input = retained.Lease; sha = retained.Sha256; }
                var actual = NativePluginSteamFileIdentity.Read(input);
                _steamSourceHashDeliveries[index] = _steamSourceHashDeliveries[index] with { Managed = actual, Sha256 = sha };
                if (actual != native) throw new InvalidDataException("Mapped source method's native input and actual retained C# file identity disagree.");
            }
            if (NativePluginSteamFileIdentity.Read(input) != native || !FalloutAdvancementRuntimeReceipt.Digest(sha))
                throw new InvalidDataException("Actual retained original hash lost its immutable physical input.");
            _steamSourceHashDeliveries[index] = _steamSourceHashDeliveries[index] with { Sha256 = sha, Returned = true };
            return Payload(writer =>
            {
                writer.Write(1U); writer.Write(Generation); writer.Write(session); writer.Write(ordinal);
                writer.Write(waitingCall); writer.Write(frame.Id); writer.Write((uint)operation); writer.Write((uint)kind);
                native.WriteTransport(writer); WriteText(writer, sha);
            });
        }
        catch (Exception failure)
        {
            _steamSourceHashDeliveries[index] = _steamSourceHashDeliveries[index] with
            {
                Returned = false,
                Sha256 = _steamSourceHashDeliveries[index].Sha256 ??
                (kind == NativePluginSteamSourceFileKind.Provider ? _steamSource.ProviderHashCandidate : null),
                FailureType = failure.GetType().FullName,
                Error = failure.Message
            };
            _steamFailure ??= failure; throw;
        }
        finally { _steamSourceHashEntered = false; }
    }
    private void ReadSteamSourceHashPrefixes(BinaryReader reader, bool successful)
    {
        var count = reader.ReadUInt32();
        if (count > 65 || successful && count != (uint)_steamSourceHashDeliveries.Count)
            throw new InvalidDataException("Platform source-hash prefix exceeds or omits its actual provider/callable denominator.");
        var prefixes = new List<NativePluginSteamSourceHashPrefix>();
        for (var index = 0U; index < count; index++)
        {
            var ordinal = reader.ReadUInt64(); var request = reader.ReadUInt64(); var callback = reader.ReadUInt64();
            var operation = (NativePluginSteamOperation)reader.ReadUInt32(); var kind = (NativePluginSteamSourceFileKind)reader.ReadUInt32();
            var phase = reader.ReadUInt32(); var beforeCopied = SteamBoolean(reader);
            var before = beforeCopied ? NativePluginSteamFileIdentity.ReadTransport(reader) : null;
            var afterCopied = SteamBoolean(reader); var after = afterCopied ? NativePluginSteamFileIdentity.ReadTransport(reader) : null;
            var sha = ReadText(reader); var error = reader.ReadUInt32();
            if (ordinal != index + 1 || request == 0 || !Enum.IsDefined(operation) || !Enum.IsDefined(kind) || phase > 6 ||
                phase == 5 && (!beforeCopied || !afterCopied || before != after || error != 0) ||
                successful && phase != 5 || phase == 6 && error == 0 || sha.Length != 0 && !FalloutAdvancementRuntimeReceipt.Digest(sha))
                throw new InvalidDataException("Native source hash invented success or omitted its retained original-file prefix.");
            var actual = _steamSourceHashDeliveries.SingleOrDefault(value => value.Ordinal == ordinal);
            if (actual is not null && (!beforeCopied || actual.WaitingRequest != request || actual.Operation != operation ||
                actual.Kind != kind || actual.Native != before || callback != 0 && actual.Callback != callback ||
                sha.Length != 0 && actual.Sha256 != sha))
                throw new InvalidDataException("Failed native source hash changed its actual managed caller/file/digest prefix.");
            if (phase == 5 && (actual is null || !actual.Returned || actual.Callback != callback || actual.Managed != after || actual.Sha256 != sha))
                throw new InvalidDataException("Native source-hash success has no actual correlated C# read-only hashing receipt.");
            prefixes.Add(new(ordinal, request, callback, operation, kind, phase, actual?.Path, beforeCopied, before, afterCopied, after, sha, error));
        }
        _steamSourceHashNative = prefixes.AsReadOnly();
    }
    private void RequireSteamSourceHashSession(ulong actualSession)
    {
        if (_steamSource is null || _steamSourceHashSession != actualSession || actualSession == 0 || _steamSourceHashNative.Count == 0 ||
            _steamSourceHashNative[0] is not { Phase: 5, Kind: NativePluginSteamSourceFileKind.Provider } first ||
            first.Sha256 != _steamSource.Declaration.ProviderSha256)
            throw new InvalidDataException("Platform receipt omitted its actual C# source hash/native kernel-file identity admission.");
    }
    private void RequireSteamSourceHashOwner(string physical, string sha)
    {
        if (!_steamSourceHashDeliveries.Any(receipt => receipt.Generation == Generation && receipt.Returned &&
            receipt.Session == _steamSourceHashSession && string.Equals(receipt.Sha256, sha, StringComparison.OrdinalIgnoreCase) &&
            StringComparer.OrdinalIgnoreCase.Equals(receipt.Path, physical)))
            throw new InvalidDataException("Actual platform callable has no real retained C# hash/native opened-file identity receipt.");
    }
}
