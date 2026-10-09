using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeNvseSourcePublicationReceipt(ulong Callback, ulong Caller, ulong Object,
    ulong Publication, uint Extent, string ExpectedSha256, string? ObservedSha256, bool? Matched);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint SourceFilePublished = 0x424;
    private sealed class NativeSourcePublication(ulong caller, NativeNvseSourceObject value,
        INativeNvseSourceFileCurrentAuthority authority, byte[] expected, int receipt)
    {
        internal readonly ulong Caller = caller;
        internal readonly NativeNvseSourceObject Object = value;
        internal readonly INativeNvseSourceFileCurrentAuthority Authority = authority;
        internal readonly byte[] Expected = expected;
        internal readonly int Receipt = receipt;
    }
    private readonly Dictionary<ulong, NativeSourcePublication> _nvseSourcePublications = [];
    private readonly List<NativeNvseSourcePublicationReceipt> _nvseSourcePublicationHistory = [];
    internal IReadOnlyList<NativeNvseSourcePublicationReceipt> NvseSourcePublicationHistory
        => _nvseSourcePublicationHistory.AsReadOnly();

    private void RetainSourceFilePublication(Frame begin, ulong caller, NativeNvseSourceObject value,
        INativeNvseSourceFileAuthority file, byte[] after, int receipt)
    {
        if (file is not INativeNvseSourceFileCurrentAuthority authority)
            throw new NotSupportedException("Source parsing requires its actual shared failed-publication owner before native delivery.");
        if (begin.Id == 0 || after.Length != value.Image.Length || !_nvseSourcePublications.TryAdd(begin.Id,
                new(caller, value, authority, after.ToArray(), receipt)))
            throw new InvalidDataException("Native source publication lacks its unique actual call/image lifetime.");
        _nvseSourcePublicationHistory.Add(new(begin.Id, caller, value.Id, begin.Id, checked((uint)after.Length),
            Convert.ToHexString(SHA256.HashData(after)), null, null));
    }

    private byte[] DispatchSourceFilePublication(Frame frame, BinaryReader reader, ulong caller,
        NativeNvseSourceObject value, INativeNvseSourceFileAuthority file)
    {
        var id = reader.ReadUInt64(); var count = reader.ReadUInt32();
        if (!_nvseSourcePublications.TryGetValue(id, out var pending) || pending.Caller != caller ||
            !ReferenceEquals(pending.Object, value) || !ReferenceEquals(pending.Authority, file) ||
            count != pending.Expected.Length || count > MaximumPayload - 64)
            throw new InvalidDataException("Native source publication acknowledgement belongs to another contributor/call/extent.");
        var observed = reader.ReadBytes(checked((int)count)); Finish(reader);
        var matches = observed.AsSpan().SequenceEqual(pending.Expected);
        _nvseSourcePublicationHistory.Add(new(frame.Id, caller, value.Id, id, count,
            Convert.ToHexString(SHA256.HashData(pending.Expected)), Convert.ToHexString(SHA256.HashData(observed)), matches));
        if (!matches)
            throw new InvalidDataException("Actual native contributor publication differs from its complete source-derived image.");
        _nvseFileCalls[pending.Receipt] = _nvseFileCalls[pending.Receipt] with { PublicationCompleted = true };
        _nvseSourcePublications.Remove(id);
        return Payload(writer => writer.Write(count));
    }

    private static byte[] GuardSourceFilePublication(INativeNvseSourceFileAuthority file, Func<byte[]> action)
    {
        if (file is not INativeNvseSourceFileCurrentAuthority actual)
            throw new NotSupportedException("Source advance lacks the genuine shared parser failure authority.");
        try { return action(); }
        catch (Exception error)
        {
            try { actual.RetainSourceFileOutputFailure(error); }
            catch (Exception retained) { throw new AggregateException("Source advance/publication and shared-prefix retention failed.", error, retained); }
            throw;
        }
    }

    // A native failure can occur after a produced reply but before any next
    // callback. Its shared cursor prefix still belongs to this failed call.
    // Failure does not clear/replay that prefix or retire any callable lease.
    private Exception RetainIncompleteSourceFilePrefixes(Exception original)
    {
        var owners = new HashSet<INativeNvseSourceFileCurrentAuthority>(ReferenceEqualityComparer.Instance);
        foreach (var publication in _nvseSourcePublications.Values) owners.Add(publication.Authority);
        foreach (var transfer in _nvseFileTransfers.Values)
            if (transfer.Object.Authority is INativeNvseSourceFileCurrentAuthority authority) owners.Add(authority);
            else return new AggregateException("Failed native output lacks its retained shared contributor authority.", original,
                new NotSupportedException("Unfinished source output owner is unbound."));
        if (owners.Count == 0) return original;
        var errors = new List<Exception> { original };
        foreach (var owner in owners)
            try { owner.RetainSourceFileOutputFailure(original); }
            catch (Exception failure) { errors.Add(failure); }
        return errors.Count == 1 ? original : new AggregateException("Native source publication failed with retained shared-prefix errors.", errors);
    }

    private void RequireSourcePublicationComplete(NativeSourceFileTransfer pending)
    {
        if (!_nvseFileCalls[pending.Receipt].PublicationCompleted)
            throw new InvalidDataException("Native source output precedes acknowledgement of its actual contributor image.");
    }
}
