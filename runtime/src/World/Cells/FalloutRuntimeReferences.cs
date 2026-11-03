using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutRuntimeReferenceKind { Explosion, Projectile }
internal sealed record FalloutRuntimeReference(uint Id, FalloutRuntimeReferenceKind Kind, FalloutFormKey Base, bool Retired = false);
internal sealed record FalloutRuntimeReferencesSnapshot(uint LastId, IReadOnlyList<FalloutRuntimeReference> References);

// One reserved FF namespace for this world. Retired identities are retained;
// neither an explosion nor a persistent projectile may reuse another's ID.
internal sealed class FalloutRuntimeReferences(FalloutPluginStack records)
{
    private const uint Prefix = 0xff000000;
    private readonly Dictionary<uint, FalloutRuntimeReference> _references = [];
    private uint _lastId;

    internal FalloutRuntimeReference Require(uint id) => _references.GetValueOrDefault(id) ??
        throw new InvalidDataException($"Runtime reference {id:x8} has no authoritative owner.");

    internal (FalloutSpatialReferenceId Identity, FalloutInventoryChangeLease Publication) Prepare(FalloutRuntimeReferenceKind kind, FalloutFormKey basis)
    {
        if (records.Plugins.Count == 256) throw new NotSupportedException("Runtime reference namespace FF is occupied by a loaded plugin.");
        if (_lastId == FalloutFormKey.ObjectIdMask) throw new InvalidOperationException("Runtime reference namespace is exhausted.");
        var entry = new FalloutRuntimeReference(Prefix | (_lastId + 1), kind, basis);
        ValidateEntry(entry);
        var previous = _lastId;
        var committed = false;
        var lease = new FalloutInventoryChangeLease(() =>
        {
            if (_lastId != previous) throw new InvalidOperationException("Runtime reference allocation changed during preparation.");
            _references.Add(entry.Id, entry); _lastId++; committed = true;
        }, () =>
        {
            if (!committed) return;
            if (_lastId != previous + 1 || Require(entry.Id) != entry)
                throw new InvalidOperationException("Published runtime reference acquired another owner before rollback.");
            _references.Remove(entry.Id); _lastId = previous;
        });
        return (FalloutSpatialReferenceId.Runtime(entry.Id), lease);
    }

    internal void Retire(uint id)
    {
        var reference = Require(id);
        _references[id] = reference with { Retired = true };
    }

    internal FalloutRuntimeReferencesSnapshot Capture() => new(_lastId, _references.Values.OrderBy(reference => reference.Id).ToArray());

    internal void Restore(FalloutRuntimeReferencesSnapshot? snapshot, FalloutTransientExplosionsSnapshot? legacyExplosions = null)
    {
        if (_lastId != 0 || _references.Count != 0) throw new InvalidOperationException("Runtime reference restore requires a fresh owner.");
        if (snapshot is null)
        {
            if (legacyExplosions is null) return;
            FalloutTransientExplosions.ValidateSnapshot(legacyExplosions);
            snapshot = new(legacyExplosions.LastId, legacyExplosions.Retired.Select(reference =>
                new FalloutRuntimeReference(reference.Id, FalloutRuntimeReferenceKind.Explosion, reference.Explosion, true)).ToArray());
        }
        if (snapshot.LastId > FalloutFormKey.ObjectIdMask || snapshot.References is null ||
            snapshot.References.Count != snapshot.LastId || records.Plugins.Count == 256 && snapshot.LastId != 0)
            throw new InvalidDataException("Saved runtime reference allocation is invalid.");
        var restored = new Dictionary<uint, FalloutRuntimeReference>();
        foreach (var reference in snapshot.References)
        {
            if (reference is null || (reference.Id & FalloutFormKey.ObjectIdMask) > snapshot.LastId || !restored.TryAdd(reference.Id, reference))
                throw new InvalidDataException("Saved runtime reference is duplicated or exceeds its allocation.");
            ValidateEntry(reference);
        }
        foreach (var (id, reference) in restored) _references.Add(id, reference);
        _lastId = snapshot.LastId;
    }

    private void ValidateEntry(FalloutRuntimeReference reference)
    {
        FalloutSpatialReferenceId.Runtime(reference.Id).Validate();
        var source = records.GetEffective(reference.Base);
        if (!Enum.IsDefined(reference.Kind) || source.IsDeleted || source.Signature !=
            (reference.Kind == FalloutRuntimeReferenceKind.Explosion ? "EXPL" : "PROJ"))
            throw new InvalidDataException("Runtime reference has no matching winning source base.");
    }
}
