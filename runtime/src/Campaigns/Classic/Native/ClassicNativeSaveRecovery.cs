using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Recover pre-fix exploration saves without accepting changed owned files or resetting player/items.</summary>
internal static class ClassicNativeSaveRecovery
{
    internal static ClassicPlayerSave Rebase(ClassicMapCatalog catalog, ClassicPlayerSave save)
    {
        if (save.Campaign != "fallout-1" || save.Schema is not ("opennv-classic-player/v1" or "opennv-classic-player/v2" or "opennv-classic-player/v3")) return save;
        var verified = new Dictionary<string, (string Hash, Fallout1NativeObjectGraph Prior)>();
        bool changed = false;
        string Hash(string map, string hash)
        {
            var current = catalog.Load(map);
            if (current.Path != map) throw new InvalidDataException("Saved map identity is not canonical.");
            if (current.Sha256 == hash) return hash;
            if (verified.TryGetValue(map, out var checkedMap))
            {
                if (checkedMap.Hash != hash) throw new InvalidDataException("Saved item origins disagree on their source hash.");
                return current.Sha256;
            }
            var prior = catalog.ReadForNativeSaveRecovery(map);
            if (ClassicPremadeReader.Hash(prior) != hash) throw new InvalidDataException("Saved source does not match either verified DAT1 decoder version.");
            verified.Add(map, (hash, Fallout1NativeObjectGraphReader.Read(prior, Fallout1NativeMapReader.Read(prior), catalog)));
            changed = true; return current.Sha256;
        }
        ClassicItemOrigin Origin(ClassicItemOrigin origin)
        {
            var hash = Hash(origin.Map, origin.Sha256);
            if (hash == origin.Sha256) return origin;
            Fallout1NativeMapObject Find(IEnumerable<Fallout1NativeMapObject> rows)
            {
                IEnumerable<Fallout1NativeMapObject> Flatten(IEnumerable<Fallout1NativeMapObject> objects) =>
                    objects.SelectMany(row => new[] { row }.Concat(Flatten(row.Inventory)));
                return Flatten(rows).SingleOrDefault(row => row.Serial == origin.Serial && row.Pid == origin.Pid)
                    ?? throw new InvalidDataException("A recovered item is missing from its original source.");
            }
            var prior = Find(verified[origin.Map].Prior.TopLevelObjects);
            var current = Find(catalog.Load(origin.Map).Objects.TopLevelObjects);
            if (JsonSerializer.Serialize(prior) != JsonSerializer.Serialize(current))
                throw new InvalidDataException("A source item changed across decoder versions; automatic recovery cannot discard that difference.");
            return origin with { Sha256 = hash };
        }
        var mapHash = Hash(save.MapPath, save.MapSha256);
        var inventory = save.Inventory?.Select(Origin).ToArray();
        var items = save.Items is null ? null : save.Items with
        {
            Changes = save.Items.Changes.Select(row => row with
            {
                Origin = Origin(row.Origin),
                Stacks = row.Stacks.Select(stack => stack with { Origin = Origin(stack.Origin) }).ToArray()
            }).ToArray()
        };
        return changed ? save with { MapSha256 = mapHash, Inventory = inventory, Items = items } : save;
    }
}
