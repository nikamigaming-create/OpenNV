using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutReferencePackageEventKind { Start, Done, Change }

internal sealed record FalloutReferencePackageEventSnapshot(FalloutFormKey Package,
    FalloutReferencePackageEventKind Kind, long Revision, string SourceSha256)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Package.OwnerPlugin) || Package.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            Revision <= 0 || Revision == long.MaxValue || SourceSha256 is not { Length: 64 } || !SourceSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Saved actor package event has an invalid identity, revision or source hash.");
        _ = FalloutReferencePackageEvents.Name(Kind);
    }
}

// Actor package marks are source-script event-list state, independent of an
// actor's presentation binding. Admission happens on its ordinary script frame.
internal sealed class FalloutReferencePackageEvents(FalloutPluginStack records)
{
    private readonly Dictionary<FalloutFormKey, Dictionary<(FalloutFormKey Package, FalloutReferencePackageEventKind Kind), long>> _pending = [];
    private long _revision;
    private long _epoch;
    private readonly Dictionary<FalloutFormKey, string> _sourceHashes = [];

    internal int PendingCount => _pending.Values.Sum(marks => marks.Count);
    internal IReadOnlyList<FalloutFormKey> PendingActors => _pending.Keys.ToArray();
    internal bool HasPending(FalloutFormKey actor) => _pending.ContainsKey(actor);

    internal IReadOnlyList<FalloutReferencePackageEventSnapshot> Capture(FalloutFormKey actor) =>
        _pending.TryGetValue(actor, out var marks) ? Array.AsReadOnly(marks.OrderBy(mark => mark.Value)
            .Select(mark => new FalloutReferencePackageEventSnapshot(mark.Key.Package, mark.Key.Kind,
                mark.Value, SourceHash(mark.Key.Package))).ToArray()) : [];

    internal void Restore(IReadOnlyList<FalloutReferenceSnapshot> snapshots)
    {
        if (_pending.Count != 0 || _revision != 0) throw new InvalidOperationException("Package event restoration requires a fresh owner.");
        var pending = new Dictionary<FalloutFormKey, Dictionary<(FalloutFormKey, FalloutReferencePackageEventKind), long>>();
        var revisions = new HashSet<long>();
        long revision = 0;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.PackageEvents is not { Count: > 0 } events) continue;
            RequireActor(records, snapshot.Reference);
            var marks = new Dictionary<(FalloutFormKey, FalloutReferencePackageEventKind), long>();
            foreach (var mark in events)
            {
                (mark ?? throw new InvalidDataException("Saved actor package event is absent.")).Validate();
                if (!marks.TryAdd((mark.Package, mark.Kind), mark.Revision) || !revisions.Add(mark.Revision) ||
                    !SourceHash(mark.Package).Equals(mark.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Saved actor package event is duplicated or differs from its winning source.");
                revision = Math.Max(revision, mark.Revision);
            }
            if (!pending.TryAdd(snapshot.Reference, marks)) throw new InvalidDataException("Saved package-event actor is duplicated.");
        }
        foreach (var (actor, marks) in pending) _pending.Add(actor, marks);
        _revision = revision;
    }

    private string SourceHash(FalloutFormKey package)
    {
        if (_sourceHashes.TryGetValue(package, out var existing)) return existing;
        RequirePackage(records, package);
        var source = records.GetEffective(package);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(source.ReadData());
        hash.AppendData(Encoding.UTF8.GetBytes(source.FormKey + "\0"));
        foreach (var name in source.Plugin.Masters.Append(source.Plugin.Name))
            hash.AppendData(Encoding.UTF8.GetBytes(name.ToUpperInvariant() + "\0"));
        var result = Convert.ToHexString(hash.GetHashAndReset());
        _sourceHashes.Add(package, result);
        return result;
    }

    internal void Mark(FalloutFormKey actor, FalloutFormKey package, FalloutReferencePackageEventKind kind)
    {
        RequireActor(records, actor);
        RequirePackage(records, package);
        _ = Name(kind);
        var revision = checked(_revision + 1);
        if (!_pending.TryGetValue(actor, out var marks)) _pending.Add(actor, marks = []);
        marks[(package, kind)] = revision;
        _revision = revision;
    }

    internal FalloutReferencePackageEventBatch SnapshotPending(FalloutFormKey actor)
    {
        RequireActor(records, actor);
        var marks = _pending.TryGetValue(actor, out var pending)
            ? pending.ToFrozenDictionary() : FrozenDictionary<(FalloutFormKey Package, FalloutReferencePackageEventKind Kind), long>.Empty;
        var events = marks.Keys.GroupBy(mark => mark.Kind).OrderBy(group => group.Key)
            .Select(group => new FalloutReferenceScriptEvent(Name(group.Key), Packages: group.Select(mark => mark.Package).ToFrozenSet()))
            .ToArray();
        return new(this, _epoch, actor, marks, Array.AsReadOnly(events));
    }

    // Consume the admitted receipt after source execution or a retained fault.
    // Effects may mark another transition during dispatch; preserve that newer
    // mark for the next source frame, even when its PACK and kind are identical.
    internal void Consume(FalloutReferencePackageEventBatch batch)
    {
        var observed = batch.Consume(this, _epoch);
        if (!_pending.TryGetValue(batch.Actor, out var marks)) return;
        foreach (var (key, revision) in observed)
            if (marks.GetValueOrDefault(key) == revision) marks.Remove(key);
        if (marks.Count == 0) _pending.Remove(batch.Actor);
    }

    internal void Clear()
    {
        var epoch = checked(_epoch + 1);
        _pending.Clear();
        _epoch = epoch;
    }

    internal static string Name(FalloutReferencePackageEventKind kind) => kind switch
    {
        FalloutReferencePackageEventKind.Start => "OnPackageStart",
        FalloutReferencePackageEventKind.Done => "OnPackageDone",
        FalloutReferencePackageEventKind.Change => "OnPackageChange",
        _ => throw new InvalidDataException("Unknown actor package event kind.")
    };

    internal static bool TryKind(string name, out FalloutReferencePackageEventKind kind)
    {
        if (name.Equals("OnPackageStart", StringComparison.OrdinalIgnoreCase)) kind = FalloutReferencePackageEventKind.Start;
        else if (name.Equals("OnPackageDone", StringComparison.OrdinalIgnoreCase) || name.Equals("OnPackageEnd", StringComparison.OrdinalIgnoreCase))
            kind = FalloutReferencePackageEventKind.Done;
        else if (name.Equals("OnPackageChange", StringComparison.OrdinalIgnoreCase)) kind = FalloutReferencePackageEventKind.Change;
        else { kind = default; return false; }
        return true;
    }

    internal static string CanonicalName(string name) => TryKind(name, out var kind) ? Name(kind) : name;

    internal static void RequireActor(FalloutPluginStack records, FalloutFormKey actor)
    {
        if (records.GetEffective(actor).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("Actor package events require an actual placed ACHR or ACRE calling reference.");
    }

    internal static void RequirePackage(FalloutPluginStack records, FalloutFormKey package)
    {
        if (records.GetEffective(package).Signature != "PACK")
            throw new InvalidDataException("Actor package events require a typed source PACK identity.");
    }
}

internal sealed class FalloutReferencePackageEventBatch(FalloutReferencePackageEvents owner, long epoch,
    FalloutFormKey actor, IReadOnlyDictionary<(FalloutFormKey Package, FalloutReferencePackageEventKind Kind), long> marks,
    IReadOnlyList<FalloutReferenceScriptEvent> events)
{
    private bool _consumed;
    internal FalloutFormKey Actor { get; } = actor;
    internal IReadOnlyList<FalloutReferenceScriptEvent> Events { get; } = events;
    internal int Count => marks.Count;

    internal IReadOnlyDictionary<(FalloutFormKey Package, FalloutReferencePackageEventKind Kind), long> Consume(
        FalloutReferencePackageEvents expectedOwner, long expectedEpoch)
    {
        if (!ReferenceEquals(owner, expectedOwner) || epoch != expectedEpoch || _consumed)
            throw new InvalidOperationException("Package event receipt belongs to another owner, was retired or was already consumed.");
        _consumed = true;
        return marks;
    }
}
