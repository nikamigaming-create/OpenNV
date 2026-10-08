using System.Security.Cryptography;

/// <summary>Neutral byte evidence only. Eviction affects reuse, never the audit denominator.</summary>
internal sealed class CorpusByteEvidenceCache : IDisposable
{
    private const int MaximumFileLeases = 64;
    private const int MaximumMemberDigests = 262144;
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Dictionary<string, FileEvidence> _baseline = new(Paths);
    private readonly Dictionary<string, LinkedListNode<Lease>> _leases = new(Paths);
    private readonly LinkedList<Lease> _leaseOrder = new();
    private readonly Dictionary<MemberKey, LinkedListNode<MemberEntry>> _members = new();
    private readonly LinkedList<MemberEntry> _memberOrder = new();
    private bool _disposed;
    private long _fileReads, _leaseHits, _memberReads, _memberHits, _fileEvictions, _memberEvictions;

    internal sealed record FileEvidence(string Source, long Bytes, long MtimeUtcTicks, long MtimeUnixMilliseconds,
        string Sha256, string ReadKind);
    internal sealed record MemberEvidence(long Bytes, string Sha256, string FileSha256, string ReadKind, string? ArchiveEncoding);
    private sealed record Lease(string Path, FileStream Stream, FileEvidence Evidence);
    private readonly record struct MemberKey(string FileSha256, string LogicalPath, long Offset, int StoredBytes, bool Compressed);
    private sealed record MemberEntry(MemberKey Key, MemberEvidence Evidence);

    internal object State => new
    {
        fileHashReads = _fileReads, fileLeaseHits = _leaseHits, memberByteReads = _memberReads, memberDigestHits = _memberHits,
        fileEvictions = _fileEvictions, memberEvictions = _memberEvictions, liveFileLeases = _leases.Count,
        retainedMemberDigests = _members.Count, baselineFiles = _baseline.Count,
        maximumFileLeases = MaximumFileLeases, maximumMemberDigests = MaximumMemberDigests,
        fileEvictionPolicy = "smallest-complete-source-byte-cost-oldest-on-ties",
        sourceBuffersRetained = 0,
        boundary = "Bounded read leases and digest reuse; one finite baseline per encountered original source file. An evicted lease rehashes before reuse. Each selection independently resolves its winner. No selection, byte-read failure or unknown decode/runtime lane is removed by eviction."
    };

    internal FileEvidence File(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        path = System.IO.Path.GetFullPath(path);
        if (_leases.TryGetValue(path, out var cached))
        {
            ValidateMetadata(cached.Value.Evidence);
            _leaseOrder.Remove(cached); _leaseOrder.AddLast(cached);
            // Windows FileShare.Read denies both writes and deletion for this lease.
            // On other platforms verify bytes again rather than relying on share flags.
            if (!OperatingSystem.IsWindows()) VerifyBaseline(HashFile(path, cached.Value.Stream));
            ++_leaseHits;
            return cached.Value.Evidence with { ReadKind = OperatingSystem.IsWindows() ? "unchanged-read-lease" : "rehashed-read-lease" };
        }
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        try
        {
            var evidence = HashFile(path, stream);
            VerifyBaseline(evidence);
            if (!_baseline.ContainsKey(path)) _baseline.Add(path, evidence);
            var node = _leaseOrder.AddLast(new Lease(path, stream, evidence));
            _leases.Add(path, node);
            while (_leases.Count > MaximumFileLeases)
            {
                // Evict by the actual cost of rereading the original, with LRU ties.
                // Interleaved small loose paths must not repeatedly rehash a multi-GB source.
                var cheapest = _leaseOrder.First ?? throw new InvalidOperationException("Corpus file lease order is empty.");
                for (var candidate = cheapest.Next; candidate is not null; candidate = candidate.Next)
                    if (candidate.Value.Evidence.Bytes < cheapest.Value.Evidence.Bytes) cheapest = candidate;
                _leaseOrder.Remove(cheapest); _leases.Remove(cheapest.Value.Path); cheapest.Value.Stream.Dispose(); ++_fileEvictions;
            }
            return evidence;
        }
        catch { stream.Dispose(); throw; }
    }

    internal MemberEvidence Archive(string file, string logicalPath, long offset, int storedBytes, bool compressed, Func<byte[]> read)
        => Archive(file, logicalPath, offset, storedBytes, compressed, () => (read(), (string?)null));

    internal MemberEvidence Archive(string file, string logicalPath, long offset, int storedBytes, bool compressed,
        Func<(byte[] Data, string? ArchiveEncoding)> read)
    {
        var original = File(file);
        var key = new MemberKey(original.Sha256, logicalPath, offset, storedBytes, compressed);
        if (_members.TryGetValue(key, out var cached))
        {
            _memberOrder.Remove(cached); _memberOrder.AddLast(cached); ++_memberHits;
            return cached.Value.Evidence with { ReadKind = "reused-exact-original-member-evidence" };
        }
        var payload = read();
        var bytes = payload.Data;
        ValidateMetadata(original);
        var evidence = new MemberEvidence(bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            original.Sha256, "ordinary-resource-read", payload.ArchiveEncoding);
        ++_memberReads;
        var node = _memberOrder.AddLast(new MemberEntry(key, evidence));
        _members.Add(key, node);
        while (_members.Count > MaximumMemberDigests)
        {
            var oldest = _memberOrder.First ?? throw new InvalidOperationException("Corpus member digest order is empty.");
            _memberOrder.RemoveFirst(); _members.Remove(oldest.Value.Key); ++_memberEvictions;
        }
        return evidence;
    }

    private FileEvidence HashFile(string path, FileStream stream)
    {
        var before = new FileInfo(path);
        var bytes = stream.Length;
        if (before.Length != bytes) throw new InvalidDataException("Owned source length differs from its pinned read handle: " + path);
        stream.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (stream.Position != bytes || stream.Length != bytes)
            throw new InvalidDataException("Owned source did not retain its complete byte extent during the audit: " + path);
        var evidence = new FileEvidence(path, bytes, before.LastWriteTimeUtc.Ticks,
            new DateTimeOffset(before.LastWriteTimeUtc).ToUnixTimeMilliseconds(), hash, "complete-original-file-read");
        ValidateMetadata(evidence); ++_fileReads;
        return evidence;
    }

    private void VerifyBaseline(FileEvidence evidence)
    {
        if (_baseline.TryGetValue(evidence.Source, out var before) &&
            (before.Bytes != evidence.Bytes || before.MtimeUtcTicks != evidence.MtimeUtcTicks || before.Sha256 != evidence.Sha256))
            throw new InvalidDataException("Owned source changed between corpus selections: " + evidence.Source);
    }

    private static void ValidateMetadata(FileEvidence evidence)
    {
        var current = new FileInfo(evidence.Source);
        if (!current.Exists || current.Length != evidence.Bytes || current.LastWriteTimeUtc.Ticks != evidence.MtimeUtcTicks)
            throw new InvalidDataException("Owned source changed while its corpus read lease was retained: " + evidence.Source);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var lease in _leaseOrder) lease.Stream.Dispose();
        _leases.Clear(); _leaseOrder.Clear(); _members.Clear(); _memberOrder.Clear(); _baseline.Clear();
    }
}
