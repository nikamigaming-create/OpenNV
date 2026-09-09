using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicItemOrigin(string Map, string Sha256, int Serial, int Pid);
internal sealed record ClassicItemLocation(string Kind, string? Map = null, int? Host = null, int? Tile = null, int? Elevation = null)
{
    internal static ClassicItemLocation Player { get; } = new("player");
    internal static ClassicItemLocation Container(string map, int serial) => new("container", map, serial);
    internal static ClassicItemLocation Ground(string map, int tile, int elevation) => new("ground", map, Tile: tile, Elevation: elevation);
}
internal sealed record ClassicItemStack(string Id, ClassicItemOrigin Origin, ClassicItemLocation Location, int Amount, int[] InstanceValues, int? AmmoPid = null);
internal sealed record ClassicItemChange(ClassicItemOrigin Origin, ClassicItemStack[] Stacks);
internal sealed record ClassicInventoryState(ClassicItemChange[] Changes, string? LeftHand, string? RightHand, string? Armor, int ActiveHand, long NextId);
internal sealed record ClassicInventoryEntry(ClassicItemOrigin Origin, Fallout1NativeMapObject Object, ClassicItemDefinition Definition,
    string Id, ClassicItemLocation Location, int Amount);

/// <summary>Source items, quantities and ownership shared by both campaigns and all presentations.</summary>
internal sealed partial class ClassicInventory(ClassicPlayerSession player)
{
    private readonly ClassicItemDefinitions _definitions = new(player.Catalog);
    private readonly Dictionary<string, (Fallout1NativeMapObject Object, ClassicItemLocation Location)> _originals = [];
    private readonly Dictionary<string, ClassicItemChange> _changes = [];
    private readonly List<ClassicItemOrigin> _createdOrigins = [];
    private long _nextId;
    internal event Action? Changed;
    internal event Action? EquipmentChanged;
    internal string? LeftHand { get; private set; }
    internal string? RightHand { get; private set; }
    internal string? Armor { get; private set; }
    internal int ActiveHand { get; private set; }
    private static string Key(ClassicItemOrigin origin) => origin.Map + ":" + origin.Serial;
    internal ClassicItemOrigin[] Snapshot => Carried.Select(row => row.Origin).Distinct().ToArray();
    internal IReadOnlyList<ClassicInventoryEntry> Carried => At(ClassicItemLocation.Player);
    internal int Weight => WeightOf(Carried, []);
    internal int Capacity => 25 + 25 * player.Stats.Special[0];
    internal ClassicItemDefinition Definition(Fallout1NativeMapObject item) => _definitions.Read(item.Pid);
    internal ClassicItemDefinition Definition(int pid) => _definitions.Read(pid);
    internal ClassicInventoryEntry? Held => Equipped(ActiveHand == 0 ? RightHand : LeftHand);
    internal ClassicInventoryEntry? Worn => Equipped(Armor);
    private ClassicInventoryEntry? Equipped(string? id) => id is null ? null : Carried.SingleOrDefault(row => row.Id == id);

    internal void InitializeCreatedItems(IEnumerable<ClassicInitialItem> items)
    {
        if (_changes.Count != 0 || _createdOrigins.Count != 0) throw new InvalidOperationException("Source-created items initialize before inventory restoration.");
        foreach (var item in items)
        {
            if (item.Origin.Serial >= 0 || !_originals.TryAdd(Key(item.Origin), (item.Object, item.Location)))
                throw new InvalidDataException("Created source item identity collided.");
            _createdOrigins.Add(item.Origin);
        }
    }

    private Fallout1NativeMapObject FindHost(string map, int serial) => serial < 0
        ? _originals.TryGetValue(map + ":" + serial, out var created) ? created.Object : throw new InvalidDataException("Created source owner is absent.")
        : FindObject(player.Catalog.Load(map).Objects.TopLevelObjects, serial);

    private (Fallout1NativeMapObject Object, ClassicItemLocation Location) Original(ClassicItemOrigin origin)
    {
        var level = player.Catalog.Load(origin.Map);
        if (origin.Map != level.Path || origin.Sha256 != level.Sha256) throw new InvalidDataException("Item source map changed or is not canonical.");
        if (_originals.TryGetValue(Key(origin), out var cached))
        { if (cached.Object.Pid != origin.Pid) throw new InvalidDataException("Item source PID changed."); return cached; }
        (Fallout1NativeMapObject, ClassicItemLocation)? Find(IEnumerable<Fallout1NativeMapObject> rows, int? owner)
        {
            foreach (var row in rows)
            {
                if (row.Serial == origin.Serial && row.Pid == origin.Pid)
                    return (row, owner is { } parent ? ClassicItemLocation.Container(origin.Map, parent) : ClassicItemLocation.Ground(origin.Map, row.Tile, row.Elevation));
                if (Find(row.Inventory, row.Serial) is { } nested) return nested;
            }
            return null;
        }
        cached = Find(level.Objects.TopLevelObjects, null) ?? throw new InvalidDataException("Original source item is absent.");
        _originals.Add(Key(origin), cached); return cached;
    }

    private static int Amount(Fallout1NativeMapObject item, ClassicItemDefinition definition)
    {
        if (item.Quantity <= 0) throw new InvalidDataException("Source item quantity is not positive.");
        if (definition.Ammo is not { } ammo) return item.Quantity;
        // MAP authors can place more than one box's rounds in the final object.
        // Keep every authored round; normalize boxes only when presenting weight.
        if (ammo.PackSize <= 0 || item.InstanceValues.Count != 1 || item.InstanceValues[0] < 0)
            throw new InvalidDataException("Source ammunition pack/round count is invalid.");
        return checked((item.Quantity - 1) * ammo.PackSize + item.InstanceValues[0]);
    }
    private ClassicItemStack Initial(ClassicItemOrigin origin)
    {
        var original = Original(origin);
        return new(Key(origin) + ":source", origin, original.Location, Amount(original.Object, Definition(original.Object)), original.Object.InstanceValues.ToArray());
    }
    private ClassicInventoryEntry Entry(ClassicItemStack stack)
    {
        var original = Original(stack.Origin).Object; var definition = Definition(stack.AmmoPid ?? original.Pid);
        if (stack.AmmoPid is { } ammoPid)
        {
            var prototype = Fallout1NativeObjectGraphReader.ResolvePrototype(player.Catalog, ammoPid);
            original = original with { Pid = ammoPid, Fid = prototype.Fid!.Value, Prototype = prototype, InventoryLength = 0, Inventory = [] };
        }
        var values = (int[])stack.InstanceValues.Clone(); var quantity = stack.Amount;
        if (definition.Ammo is { } ammo)
        { quantity = checked((stack.Amount + ammo.PackSize - 1) / ammo.PackSize); values[0] = (stack.Amount - 1) % ammo.PackSize + 1; }
        return new(stack.Origin, original with { Quantity = quantity, InstanceValues = values }, definition, stack.Id, stack.Location, stack.Amount);
    }

    internal static Fallout1NativeMapObject FindObject(IEnumerable<Fallout1NativeMapObject> rows, int serial) =>
        TryFind(rows, serial) ?? throw new InvalidDataException("Source inventory host is absent.");
    private static Fallout1NativeMapObject? TryFind(IEnumerable<Fallout1NativeMapObject> rows, int serial)
    { foreach (var row in rows) { if (row.Serial == serial) return row; if (TryFind(row.Inventory, serial) is { } found) return found; } return null; }
    private IEnumerable<Fallout1NativeMapObject> SourceRows(ClassicItemLocation location)
    {
        if (location.Kind == "player") return [];
        if (location.Map is null) throw new InvalidDataException("World item owner has no map.");
        var level = player.Catalog.Load(location.Map);
        if (location.Kind == "ground") return level.Objects.TopLevelObjects.Where(row => row.Prototype.ObjectType == 0 &&
            row.Prototype.LogicalPath is not null && row.Tile == location.Tile && row.Elevation == location.Elevation && (row.Flags & 1) == 0);
        if (location.Kind != "container" || location.Host is null) throw new InvalidDataException("Item owner is invalid.");
        return FindHost(level.Path, location.Host.Value).Inventory;
    }
    internal IReadOnlyList<ClassicInventoryEntry> At(ClassicItemLocation location)
    {
        var result = new List<ClassicInventoryEntry>();
        foreach (var origin in _createdOrigins)
            if (!_changes.ContainsKey(Key(origin)) && Initial(origin) is { Amount: > 0 } initial && initial.Location == location) result.Add(Entry(initial));
        if (location.Kind != "player")
        {
            var level = player.Catalog.Load(location.Map!);
            foreach (var row in SourceRows(location))
            {
                var origin = new ClassicItemOrigin(level.Path, level.Sha256, row.Serial, row.Pid);
                if (!_changes.ContainsKey(Key(origin)) && Initial(origin) is { Amount: > 0 } stack) result.Add(Entry(stack));
            }
        }
        result.AddRange(_changes.Values.SelectMany(row => row.Stacks).Where(stack => stack.Location == location && stack.Amount > 0).Select(Entry));
        return result.OrderBy(row => row.Origin.Map).ThenBy(row => row.Object.Serial).ThenBy(row => row.Id).ToArray();
    }
    private int WeightOf(IEnumerable<ClassicInventoryEntry> entries, HashSet<string> ancestors)
    {
        var weight = 0;
        foreach (var entry in entries)
        {
            weight = checked(weight + entry.Definition.Weight * entry.Object.Quantity);
            if (entry.Definition.Subtype != 1) continue;
            if (!ancestors.Add(Key(entry.Origin))) throw new InvalidDataException("Container ownership has a cycle.");
            weight = checked(weight + WeightOf(At(ClassicItemLocation.Container(entry.Origin.Map, entry.Origin.Serial)), ancestors));
            ancestors.Remove(Key(entry.Origin));
        }
        return weight;
    }

    internal bool Taken(string map, int serial) => _changes.TryGetValue(ClassicMapCatalog.Canonical(map) + ":" + serial, out var change) &&
        !change.Stacks.Any(stack => stack.AmmoPid is null && stack.Location == Original(change.Origin).Location && stack.Amount > 0);
    internal Fallout1NativeMapObject Host(int serial)
    {
        var host = player.Level.Objects.TopLevelObjects.Single(row => row.Serial == serial);
        if (host.Elevation != player.Elevation || (host.Flags & 1) != 0 || Taken(player.MapPath, host.Serial)) throw new InvalidOperationException("That object is not present here.");
        return host;
    }
    internal static bool IsLootHost(Fallout1NativeMapObject row) => row.Prototype.ObjectType == 0 && row.Prototype.LogicalPath is not null ||
        row.Prototype.ObjectType == 1 && row.InstanceValues.Count == 11 && (row.InstanceValues[3] & 0x80) != 0;
    internal string HostName(int serial)
    {
        var host = Host(serial);
        if (host.Prototype.ObjectType == 0) return Definition(host).Name;
        return ClassicItemDefinitions.Messages(player.Catalog.Read("text/english/game/pro_crit.msg", out _))
            .GetValueOrDefault(host.Prototype.MessageNumber ?? -1, "Remains");
    }
    internal void CheckAccess(int serial)
    {
        var host = Host(serial);
        if (player.Moving || ClassicHexGrid.Distance(player.Tile, host.Tile) > 1) throw new InvalidOperationException("Walk next to the object first.");
        if (!IsLootHost(host)) throw new InvalidOperationException("That character is alive.");
        if (host.ScriptId != uint.MaxValue) throw new NotSupportedException("This interaction requires its source script.");
        if (host.Prototype.ObjectType == 1) return;
        var definition = Definition(host);
        if (definition.Script != -1) throw new NotSupportedException("This interaction requires its source script.");
        if (definition.Subtype == 1)
        {
            if ((host.InstanceFlags & 0x02000000) != 0) throw new InvalidOperationException("That container is locked.");
            if ((host.InstanceFlags & 0x04000000) != 0) throw new InvalidOperationException("That lock is jammed.");
            if ((host.InstanceFlags & ~0x06000001u) != 0 || (definition.ContainerFlags & ~9u) != 0)
                throw new NotSupportedException("This container has unsupported source flags.");
        }
    }
    private ClassicItemLocation HostLocation(int serial)
    {
        var host = Host(serial);
        return host.Prototype.ObjectType == 1 || Definition(host).Subtype == 1 ? ClassicItemLocation.Container(player.MapPath, serial) :
            ClassicItemLocation.Ground(player.MapPath, host.Tile, host.Elevation);
    }
    internal IReadOnlyList<ClassicInventoryEntry> Contents(int serial) => At(HostLocation(serial));
    internal void Take(int hostSerial, int itemSerial) => Take(hostSerial, Contents(hostSerial).Single(row => row.Object.Serial == itemSerial).Id);
    internal void Take(int hostSerial, string id, int? quantity = null)
    { CheckAccess(hostSerial); var entry = Contents(hostSerial).Single(row => row.Id == id); Transfer(entry, ClassicItemLocation.Player, quantity ?? entry.Amount); }
    internal void Return(int hostSerial, ClassicItemOrigin origin) => Deposit(hostSerial, Carried.Single(row => row.Origin == origin).Id);
    internal void Deposit(int hostSerial, string id, int? quantity = null)
    {
        CheckAccess(hostSerial); var destination = HostLocation(hostSerial);
        if (destination.Kind != "container") throw new InvalidOperationException("That object cannot contain items.");
        var entry = Carried.Single(row => row.Id == id); Transfer(entry, destination, quantity ?? entry.Amount);
    }
    internal void Drop(string id, int? quantity = null)
    {
        if (player.Moving) throw new InvalidOperationException("Stop walking before dropping an item.");
        var entry = Carried.Single(row => row.Id == id); Transfer(entry, ClassicItemLocation.Ground(player.MapPath, player.Tile, player.Elevation), quantity ?? entry.Amount);
    }
    internal void TakeGround(string id, int? quantity = null)
    {
        var entry = GroundItems.Single(row => row.Id == id);
        if (player.Moving || ClassicHexGrid.Distance(player.Tile, entry.Location.Tile!.Value) > 1) throw new InvalidOperationException("Walk next to the item first.");
        Transfer(entry, ClassicItemLocation.Player, quantity ?? entry.Amount);
    }
    internal IReadOnlyList<ClassicInventoryEntry> GroundItems => player.Level.Objects.TopLevelObjects
        .Where(row => row.Prototype.ObjectType == 0 && row.Prototype.LogicalPath is not null && row.Elevation == player.Elevation && (row.Flags & 1) == 0)
        .Select(row => ClassicItemLocation.Ground(player.MapPath, row.Tile, row.Elevation))
        .Concat(_changes.Values.SelectMany(row => row.Stacks).Where(row => row.Location.Kind == "ground" && row.Location.Map == player.MapPath && row.Location.Elevation == player.Elevation)
            .Select(row => row.Location)).Distinct().SelectMany(At).ToArray();

    private void CheckItem(ClassicInventoryEntry entry)
    {
        if (entry.Object.ScriptId != uint.MaxValue || entry.Definition.Script != -1) throw new NotSupportedException("This item requires its source script.");
        if (entry.Definition.Subtype == 1 && (entry.Definition.ContainerFlags & 1) != 0) throw new InvalidOperationException("That container cannot be picked up.");
    }
    private string NewId(ClassicItemOrigin origin) => Key(origin) + ":" + (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private List<ClassicItemStack> Touch(ClassicItemOrigin origin) => _changes.TryGetValue(Key(origin), out var change) ? change.Stacks.ToList() : [Initial(origin)];
    private void Set(ClassicItemOrigin origin, List<ClassicItemStack> stacks) => _changes[Key(origin)] = new(origin, stacks.ToArray());
}
