using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutActorBaseFlagChange(FalloutFormKey Base, string Winner, string Sha256, bool Essential);
internal sealed record FalloutActorBaseFlagsSnapshot(IReadOnlyList<FalloutActorBaseFlagChange> Changes);

// Script writes target the passed actor base. A placed actor's reference flag
// remains independent; neither operation changes the immutable plugin record.
internal sealed class FalloutActorBaseFlagState(FalloutPluginStack records)
{
    private readonly Dictionary<FalloutFormKey, FalloutActorBaseFlagChange> _changes = [];

    internal bool Essential(FalloutFormKey actorBase, bool source) =>
        _changes.TryGetValue(actorBase, out var changed) ? changed.Essential : source;

    internal void SetEssential(FalloutFormKey actorBase, bool essential)
    {
        if (_changes.TryGetValue(actorBase, out var previous))
            _changes[actorBase] = previous with { Essential = essential };
        else
            _changes.Add(actorBase, Read(actorBase, essential));
    }

    private FalloutActorBaseFlagChange Read(FalloutFormKey actorBase, bool essential)
    {
        var source = records.GetEffective(actorBase);
        if (source.Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("SetEssential requires an actor base.");
        var fields = source.ReadSubrecords().Where(field => field.Signature == "ACBS").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 24)
            throw new InvalidDataException("Actor base has invalid ACBS data.");
        return new(source.FormKey, source.Plugin.Name, Convert.ToHexString(SHA256.HashData(source.ReadData())), essential);
    }

    internal FalloutActorBaseFlagsSnapshot Capture() => new(_changes.Values
        .OrderBy(change => change.Base.OwnerPlugin, StringComparer.OrdinalIgnoreCase)
        .ThenBy(change => change.Base.ObjectId).ToArray());

    internal void Restore(FalloutActorBaseFlagsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Changes);
        var prepared = new Dictionary<FalloutFormKey, FalloutActorBaseFlagChange>();
        foreach (var saved in snapshot.Changes)
        {
            var source = Read(saved.Base, saved.Essential);
            if (!string.Equals(source.Winner, saved.Winner, StringComparison.OrdinalIgnoreCase) || source.Sha256 != saved.Sha256)
                throw new InvalidDataException("Saved actor-base flag source has changed.");
            if (!prepared.TryAdd(source.Base, source))
                throw new InvalidDataException("Saved actor-base flags contain a duplicate base.");
        }
        _changes.Clear();
        foreach (var pair in prepared) _changes.Add(pair.Key, pair.Value);
    }
}
