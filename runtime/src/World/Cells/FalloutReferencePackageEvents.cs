using System.Collections.Frozen;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutReferencePackageEventKind { Start, Done, Change }

// Actor package marks are source-script event-list state, independent of an
// actor's presentation binding. Admission happens on its ordinary script frame.
internal sealed class FalloutReferencePackageEvents(FalloutPluginStack records)
{
    private readonly Dictionary<FalloutFormKey, Dictionary<(FalloutFormKey Package, FalloutReferencePackageEventKind Kind), long>> _pending = [];
    private long _revision;
    private long _epoch;

    internal int PendingCount => _pending.Values.Sum(marks => marks.Count);
    internal IReadOnlyList<FalloutFormKey> PendingActors => _pending.Keys.ToArray();
    internal bool HasPending(FalloutFormKey actor) => _pending.ContainsKey(actor);

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
