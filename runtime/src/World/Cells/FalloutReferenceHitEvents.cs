using System.Collections.Frozen;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutReferenceHitKind { Projectile, Melee }
internal readonly record struct FalloutReferenceHit(FalloutFormKey Attacker, FalloutFormKey? Weapon, FalloutReferenceHitKind Kind);

// Contact/damage owners mark source object events. Script admission remains on
// the reference's ordinary frame, after damage and in authored block order.
internal sealed class FalloutReferenceHitEvents(FalloutPluginStack records)
{
    private readonly Dictionary<FalloutFormKey, Dictionary<FalloutReferenceHit, long>> _pending = [];
    private long _revision, _epoch;
    internal int PendingCount => _pending.Values.Sum(marks => marks.Count);
    internal IReadOnlyList<FalloutFormKey> PendingReferences => _pending.Keys.ToArray();
    internal bool HasPending(FalloutFormKey reference) => _pending.ContainsKey(reference);

    internal void Mark(FalloutFormKey reference, FalloutFormKey attacker, FalloutFormKey? weapon, FalloutReferenceHitKind kind)
    {
        RequireReference(records, reference);
        RequireActor(records, attacker);
        if (weapon is { } form && records.GetEffective(form).Signature != "WEAP")
            throw new InvalidDataException("A hit event weapon must be a typed source WEAP form.");
        if (kind is not (FalloutReferenceHitKind.Projectile or FalloutReferenceHitKind.Melee) ||
            kind == FalloutReferenceHitKind.Projectile && weapon is null)
            throw new InvalidDataException("A projectile hit requires its source weapon and a supported contact kind.");
        var revision = checked(_revision + 1);
        if (!_pending.TryGetValue(reference, out var marks)) _pending.Add(reference, marks = []);
        marks[new(attacker, weapon, kind)] = revision;
        _revision = revision;
    }

    internal FalloutReferenceHitEventBatch SnapshotPending(FalloutFormKey reference)
    {
        RequireReference(records, reference);
        var marks = _pending.TryGetValue(reference, out var pending) ? pending.ToFrozenDictionary() :
            FrozenDictionary<FalloutReferenceHit, long>.Empty;
        var events = new List<FalloutReferenceScriptEvent>();
        if (marks.Count != 0)
        {
            events.Add(new("OnHit", HitAttackers: marks.Keys.Select(mark => mark.Attacker).ToFrozenSet()));
            var source = records.GetEffective(reference);
            var activator = source.Signature == "REFR" &&
                records.GetEffective(FalloutDialogueTopic.RequiredForm(source, "NAME")).Signature == "ACTI";
            var weapons = marks.Keys.Where(mark => mark.Weapon is not null &&
                (!activator || mark.Kind == FalloutReferenceHitKind.Projectile))
                .Select(mark => mark.Weapon!.Value).ToFrozenSet();
            if (weapons.Count != 0) events.Add(new("OnHitWith", HitWeapons: weapons));
        }
        return new(this, _epoch, reference, marks, events.AsReadOnly());
    }

    internal void Consume(FalloutReferenceHitEventBatch batch)
    {
        var observed = batch.Consume(this, _epoch);
        if (!_pending.TryGetValue(batch.Reference, out var marks)) return;
        foreach (var (key, revision) in observed)
            if (marks.GetValueOrDefault(key) == revision) marks.Remove(key);
        if (marks.Count == 0) _pending.Remove(batch.Reference);
    }

    internal void Clear() { var epoch = checked(_epoch + 1); _pending.Clear(); _epoch = epoch; }

    internal static void RequireReference(FalloutPluginStack records, FalloutFormKey reference)
    {
        if (records.GetEffective(reference).Signature is not ("ACHR" or "ACRE" or "REFR"))
            throw new InvalidDataException("Hit events require an actual placed source reference.");
    }

    internal static bool IsActor(FalloutPluginStack records, FalloutFormKey reference) =>
        reference == records.RuntimeFormKey(0x14) || records.GetEffective(reference).Signature is "ACHR" or "ACRE";

    internal static void RequireActor(FalloutPluginStack records, FalloutFormKey actor)
    {
        if (!IsActor(records, actor)) throw new InvalidDataException("A hit attacker requires an actual actor reference.");
    }
}

internal sealed class FalloutReferenceHitEventBatch(FalloutReferenceHitEvents owner, long epoch,
    FalloutFormKey reference, IReadOnlyDictionary<FalloutReferenceHit, long> marks,
    IReadOnlyList<FalloutReferenceScriptEvent> events)
{
    private bool _consumed;
    internal FalloutFormKey Reference { get; } = reference;
    internal IReadOnlyList<FalloutReferenceScriptEvent> Events { get; } = events;
    internal int Count => marks.Count;
    internal IReadOnlyDictionary<FalloutReferenceHit, long> Consume(FalloutReferenceHitEvents expectedOwner, long expectedEpoch)
    {
        if (!ReferenceEquals(owner, expectedOwner) || epoch != expectedEpoch || _consumed)
            throw new InvalidOperationException("Hit event receipt belongs to another owner, was retired or was already consumed.");
        _consumed = true;
        return marks;
    }
}
