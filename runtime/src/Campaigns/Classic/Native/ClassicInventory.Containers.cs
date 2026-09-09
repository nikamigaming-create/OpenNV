namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed partial class ClassicInventory
{
    private ClassicInventoryEntry Locate(string id)
    {
        var changed = _changes.Values.SelectMany(change => change.Stacks).SingleOrDefault(row => row.Id == id);
        if (changed is not null) return Entry(changed);
        var fields = id.Split(':');
        if (fields.Length != 3 || fields[2] != "source" || !int.TryParse(fields[1], out var serial))
            throw new InvalidOperationException("That item is no longer present.");
        var level = player.Catalog.Load(fields[0]);
        var original = FindHost(level.Path, serial);
        var origin = new ClassicItemOrigin(level.Path, level.Sha256, original.Serial, original.Pid);
        if (_changes.ContainsKey(Key(origin))) throw new InvalidOperationException("That source stack has moved.");
        return Entry(Initial(origin));
    }

    internal ClassicInventoryEntry CheckContainerAccess(string id)
    {
        if (player.Moving) throw new InvalidOperationException("Stop walking before opening a container.");
        var entry = Locate(id);
        if (entry.Definition.Subtype != 1) throw new InvalidOperationException("That item is not a container.");
        var ancestors = new HashSet<string>();
        void Check(ClassicInventoryEntry container)
        {
            if (!ancestors.Add(Key(container.Origin))) throw new InvalidDataException("Container ownership has a cycle.");
            if (container.Object.ScriptId != uint.MaxValue || container.Definition.Script != -1)
                throw new NotSupportedException("This container requires its source script.");
            var flags = container.Object.InstanceFlags;
            if ((flags & 0x02000000) != 0) throw new InvalidOperationException("That container is locked.");
            if ((flags & 0x04000000) != 0) throw new InvalidOperationException("That lock is jammed.");
            if ((flags & ~0x06000001u) != 0 || (container.Definition.ContainerFlags & ~9u) != 0)
                throw new NotSupportedException("This container has unsupported source flags.");
            var location = container.Location;
            if (location.Kind == "player") return;
            if (location.Kind == "ground")
            {
                if (location.Map != player.MapPath || location.Elevation != player.Elevation || ClassicHexGrid.Distance(player.Tile, location.Tile!.Value) > 1)
                    throw new InvalidOperationException("Walk next to the container first.");
                return;
            }
            var level = player.Catalog.Load(location.Map!);
            var host = FindHost(level.Path, location.Host!.Value);
            if (host.Prototype.ObjectType == 1)
            {
                if (location.Map != player.MapPath) throw new InvalidOperationException("The containing character is on another map.");
                CheckAccess(host.Serial); return;
            }
            var key = location.Map + ":" + location.Host;
            var parent = _changes.TryGetValue(key, out var change)
                ? Entry(change.Stacks.Single(row => row.AmmoPid is null)) : Locate(key + ":source");
            Check(parent);
        }
        Check(entry); return entry;
    }

    internal IReadOnlyList<ClassicInventoryEntry> ContainerContents(string id)
    {
        var container = CheckContainerAccess(id);
        return At(ClassicItemLocation.Container(container.Origin.Map, container.Origin.Serial));
    }
    internal void TakeFromContainer(string containerId, string id, int quantity)
    {
        var entry = ContainerContents(containerId).Single(row => row.Id == id);
        Transfer(entry, ClassicItemLocation.Player, quantity);
    }
    internal void DepositIntoContainer(string containerId, string id, int quantity)
    {
        var container = CheckContainerAccess(containerId);
        var entry = Carried.Single(row => row.Id == id);
        Transfer(entry, ClassicItemLocation.Container(container.Origin.Map, container.Origin.Serial), quantity);
    }
}
