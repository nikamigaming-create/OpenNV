using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.AnimationCompare;

internal static partial class Catalog
{
    private sealed partial class Builder
    {
        private string? RigSet(IReadOnlyList<string>? rigs, string role)
        {
            if (rigs is null || rigs.Count == 0) return null;
            var ids = rigs.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var id = "rig-set:" + Hash(role + "\n" + string.Join('\n', ids));
            _rigSets.TryAdd(id, new(id, role, ids));
            return id;
        }

        private string OwnerSet(IEnumerable<FalloutPluginRecord> recordsForSet)
        {
            var owners = recordsForSet.DistinctBy(record => record.FormKey).OrderBy(record => records.RuntimeFormId(record.FormKey))
                .Select(record => new CatalogSourceOwner(record.FormKey.ToString(), records.RuntimeFormId(record.FormKey), record.Signature,
                    _recordHashes.GetValueOrDefault(record.FormKey.ToString()), ReferenceSet(References(record.FormKey.ToString())))).ToArray();
            var id = "owners:" + Hash(JsonSerializer.Serialize(owners, Json));
            _sourceOwnerSets.TryAdd(id, new(id, owners));
            return id;
        }
    }
}
