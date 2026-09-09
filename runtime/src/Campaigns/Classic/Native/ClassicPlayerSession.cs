using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicPlayerSave(string Schema, string Campaign, string ProfileId, string MapPath, string MapSha256,
    int Elevation, ClassicCharacterDraft Choice, int Tile, int Rotation, int HitPoints, long CompletedSteps,
    ClassicItemOrigin[]? Inventory = null, ClassicInventoryState? Items = null, string? InitializationHash = null,
    ClassicDoorWorldSave? Doors = null);

/// <summary>The classic player owns movement and persistence; cameras, meshes and HUDs consume it.</summary>
internal sealed class ClassicPlayerSession
{
    internal const string SaveSchema = "opennv-classic-player/v4";
    private const string InitializationSaveSchema = "opennv-classic-player/v3";
    private const string ItemsSaveSchema = "opennv-classic-player/v2";
    private const string LegacySaveSchema = "opennv-classic-player/v1";
    internal static string SavePathFor(string requested)
    {
        if (!File.Exists(requested)) return requested;
        using var document = JsonDocument.Parse(File.ReadAllBytes(requested));
        if (document.RootElement.TryGetProperty("Schema", out var schema) && schema.GetString() is SaveSchema or InitializationSaveSchema or ItemsSaveSchema or LegacySaveSchema) return requested;
        // The previous runtime's richer save cannot be converted by discarding
        // inventory, combat or scripts. Retain it and use an adjacent new slot.
        return requested + ".native-player.json";
    }
    private ClassicMapNavigation _navigation;
    private string _mapSha256;
    private readonly ClassicMapCatalog _catalog;
    private ClassicMapLevel _level;
    private readonly IReadOnlyList<ClassicPremade> _premades;
    private readonly Queue<int> _path = new();
    private int? _redirect;
    private byte[]? _sourceRecoveryBackup;
    internal ClassicCharacterDraft Choice { get; }
    internal ClassicStartingStats Stats { get; }
    internal string MapPath { get; private set; }
    internal int Elevation { get; private set; }
    internal int Tile { get; private set; }
    internal int Rotation { get; private set; }
    internal int HitPoints { get; private set; }
    internal long CompletedSteps { get; private set; }
    internal double StepFraction { get; private set; }
    internal bool Moving => _path.Count != 0;
    internal int NextTile => _path.TryPeek(out var next) ? next : Tile;
    internal IReadOnlyList<int> RemainingPath => _path.ToArray();
    internal IReadOnlySet<int> Walkable => _navigation.Walkable;
    internal string Status { get; private set; } = "Click a floor hex to walk.";
    internal event Action? Changed;
    internal event Action? Arrived;
    internal event Action? LevelChanged;
    internal ClassicMapCatalog Catalog => _catalog;
    internal ClassicMapLevel Level => _level;
    internal ClassicInventory Inventory { get; }
    internal ClassicDoorWorld Doors { get; }
    internal ClassicCampaignInitialization? Initialization { get; private set; }
    internal int ArmorClass => Stats.ArmorClass + (Inventory.Worn?.Definition.Armor?.ArmorClass ?? 0);

    internal ClassicPlayerSession(IFalloutClassicOwnedSource source, string mapPath, ClassicCharacterDraft choice,
        int? elevation = null, int? tile = null, int? rotation = null, ClassicDoorWorldSave? savedDoors = null,
        ClassicCampaignInitialization? initialization = null)
    {
        _catalog = source as ClassicMapCatalog ?? new ClassicMapCatalog(source, choice.Campaign);
        _premades = ClassicPremadeReader.Load(_catalog.Campaign, path => _catalog.Read(path, out _));
        choice.Validate(_catalog.Campaign, source.ProfileId, _premades);
        // Freeze all arrays in the accepted character; an open editor cannot
        // mutate the identity of a game which has already started.
        Choice = JsonSerializer.Deserialize<ClassicCharacterDraft>(JsonSerializer.Serialize(choice))!;
        Stats = ClassicStartingStats.From(Choice.Character); HitPoints = Stats.HitPoints;
        Initialization = initialization;
        _level = _catalog.Load(mapPath); MapPath = _level.Path; _mapSha256 = _level.Sha256;
        var map = _level.Map;
        Elevation = elevation ?? map.EnteringElevation; Tile = tile ?? map.EnteringTile; Rotation = rotation ?? map.EnteringRotation;
        _navigation = new(map, _level.Objects, Elevation);
        Inventory = new ClassicInventory(this);
        Doors = new ClassicDoorWorld(this);
        if (savedDoors is null) Doors.Enter(); else Doors.Restore(savedDoors);
        Doors.PassageChanged += RefreshNavigation; RefreshNavigation();
        if (!Walkable.Contains(Tile)) throw new InvalidDataException("Source MAP entry is blocked; map initialization must resolve it before player admission.");
    }

    private void RefreshNavigation()
    {
        _navigation = new(_level.Map, _level.Objects, Elevation, Doors.Blocks);
        if (Moving && StepFraction == 0 && !Walkable.Contains(NextTile))
        { _path.Clear(); _redirect = null; Status = "The passage is blocked."; }
        Changed?.Invoke();
    }

    internal static ClassicPlayerSession Begin(ClassicMapCatalog catalog, ClassicCharacterDraft choice, ClassicRetailRandomContract? initializationContract = null)
    {
        var initialization = initializationContract is null ? null : ClassicCampaignInitialization.Execute(catalog, choice, initializationContract);
        var arrival = initialization?.Arrival;
        var player = new ClassicPlayerSession(catalog, catalog.StartingMap, choice, arrival?.Elevation,
            arrival is null ? null : checked(arrival.TileY * 200 + arrival.TileX), arrival?.Rotation, initialization: initialization);
        if (initialization is not null) player.Inventory.InitializeCreatedItems(initialization.Items);
        return player;
    }

    // The renderer builds the destination before calling this commit. Failure
    // leaves the active map, player and save intact.
    internal void ChangeLevel(string mapPath, int elevation, int tile, int rotation)
    {
        if (Moving) throw new InvalidOperationException("A map transition must occur at a completed hex.");
        var next = _catalog.Load(mapPath);
        if (!next.Map.Elevations.ContainsKey(elevation) || rotation is < 0 or >= 6)
            throw new InvalidDataException("Source exit destination elevation or rotation is invalid.");
        var navigation = new ClassicMapNavigation(next.Map, next.Objects, elevation, row => Doors.BlocksAt(next.Path, row));
        if (!navigation.Walkable.Contains(tile)) throw new InvalidDataException("Source exit arrival is blocked; its map initialization is still required.");
        _level = next; _navigation = navigation; _mapSha256 = next.Sha256;
        MapPath = next.Path; Elevation = elevation; Tile = tile; Rotation = rotation; StepFraction = 0; _redirect = null;
        Doors.Enter(); RefreshNavigation();
        Status = "Click a floor hex to walk."; Changed?.Invoke(); LevelChanged?.Invoke();
    }

    internal ClassicMapExit? ExitAtPlayer()
    {
        var exits = _level.Objects.TopLevelObjects.Where(row => row.Elevation == Elevation && row.Tile == Tile &&
            Fallout1NativeObjectGraphReader.IsExitGrid(row.Prototype) && (row.Flags & 1) == 0).ToArray();
        if (exits.Length == 0) return null;
        var destinations = exits.Select(row => row.InstanceValues.ToArray()).ToArray();
        if (destinations.Any(values => values.Length != 4) || destinations.Skip(1).Any(values => !values.SequenceEqual(destinations[0])))
            throw new InvalidDataException("Source exit grids disagree on this hex.");
        var target = destinations[0];
        return new(_catalog.ResolveMap(target[0]), target[2], target[1], target[3]);
    }

    internal bool RequestMove(int destination)
    {
        var from = Moving && StepFraction > 0 ? NextTile : Tile;
        if (destination != from && ClassicHexGrid.Path(from, destination, Walkable).Length == 0)
        { Status = "That hex cannot be reached."; Changed?.Invoke(); return false; }
        if (Moving && StepFraction > 0) _redirect = destination;
        else Plan(destination);
        Changed?.Invoke(); return true;
    }

    internal void Stop()
    {
        if (Moving && StepFraction > 0) _redirect = NextTile;
        else { _path.Clear(); _redirect = null; }
        Status = "Stopping at the next hex."; Changed?.Invoke();
    }

    private void Plan(int destination)
    {
        _path.Clear(); _redirect = null;
        foreach (var tile in ClassicHexGrid.Path(Tile, destination, Walkable)) _path.Enqueue(tile);
        if (Moving) Rotation = ClassicHexGrid.Rotation(Tile, NextTile);
        Status = Moving ? $"Walking · {_path.Count} hexes" : "Click a floor hex to walk.";
    }

    // Source FRM deltas advance hex distance. No presentation transform is read
    // back into gameplay. A redirect finishes the current edge before replanning.
    internal void Advance(double hexDistance)
    {
        if (!double.IsFinite(hexDistance) || hexDistance < 0) throw new ArgumentOutOfRangeException(nameof(hexDistance));
        if (!Moving) return;
        StepFraction += hexDistance;
        while (Moving && StepFraction >= 1)
        {
            Tile = _path.Dequeue(); CompletedSteps++; StepFraction -= 1;
            if (_level.Objects.TopLevelObjects.Any(row => row.Tile == Tile && row.Elevation == Elevation &&
                (row.Flags & 1) == 0 && Fallout1NativeObjectGraphReader.IsExitGrid(row.Prototype)))
            { _path.Clear(); _redirect = null; break; }
            if (_redirect is { } target) Plan(target);
            // A door can finish closing after this route was planned. Check
            // the live owner again at every completed edge before taking it.
            if (Moving && !Walkable.Contains(NextTile)) { _path.Clear(); _redirect = null; }
            if (Moving) Rotation = ClassicHexGrid.Rotation(Tile, NextTile);
        }
        if (!Moving) { StepFraction = 0; Status = "Click a floor hex to walk."; }
        Changed?.Invoke();
        if (!Moving) Arrived?.Invoke();
    }

    internal void Save(string path)
    {
        if (Moving) throw new InvalidOperationException("Finish the current walk before saving.");
        var save = new ClassicPlayerSave(SaveSchema, Choice.Campaign, Choice.ClassicProfileId, MapPath, _mapSha256,
            Elevation, Choice, Tile, Rotation, HitPoints, CompletedSteps, Items: Inventory.State, InitializationHash: Initialization?.Identity, Doors: Doors.Save());
        var target = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (_sourceRecoveryBackup is { } recovered)
        {
            var backup = target + ".before-dat1-fix-" + ClassicPremadeReader.Hash(recovered)[..12];
            if (!File.Exists(backup)) File.WriteAllBytes(backup, recovered);
        }
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(save)); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static ClassicPlayerSession Restore(IFalloutClassicOwnedSource source, string path, ClassicRetailRandomContract? initializationContract = null)
    {
        var originalBytes = File.ReadAllBytes(path);
        var save = JsonSerializer.Deserialize<ClassicPlayerSave>(originalBytes) ?? throw new InvalidDataException("Classic player save is empty.");
        var campaign = source is ClassicMapCatalog catalog ? catalog.Campaign : save.Campaign;
        if (campaign is not ("fallout-1" or "fallout-2") || save.Schema is not (SaveSchema or InitializationSaveSchema or ItemsSaveSchema or LegacySaveSchema) || save.Campaign != campaign ||
            save.ProfileId != source.ProfileId || save.Choice is null || save.Choice.Campaign != campaign)
            throw new InvalidDataException("This save belongs to another campaign, installation or unsupported save format.");
        if (save.Schema is SaveSchema or InitializationSaveSchema or ItemsSaveSchema && (save.Items is null || save.Inventory is not null))
            throw new InvalidDataException("The current save requires its complete item state.");
        var sourceCatalog = source as ClassicMapCatalog ?? new ClassicMapCatalog(source, campaign);
        var recovered = ClassicNativeSaveRecovery.Rebase(sourceCatalog, save);
        var sourceChanged = !ReferenceEquals(recovered, save); save = recovered;
        ClassicCampaignInitialization? initialization = null;
        if (save.InitializationHash is not null || initializationContract is not null)
        {
            if (initializationContract is null) throw new NotSupportedException("This save requires its campaign initialization contract.");
            initialization = ClassicCampaignInitialization.Execute(sourceCatalog, save.Choice, initializationContract);
            if (save.InitializationHash is not null && save.InitializationHash != initialization.Identity)
                throw new InvalidDataException("Campaign initialization source or results changed.");
        }
        var result = new ClassicPlayerSession(sourceCatalog, save.MapPath, save.Choice, save.Elevation, save.Tile, save.Rotation, save.Doors, initialization);
        if (sourceChanged) result._sourceRecoveryBackup = originalBytes;
        if (save.Schema == SaveSchema && save.Doors is null) throw new InvalidDataException("The current save requires its complete door state.");
        if (save.MapSha256 != result._mapSha256 || save.Elevation != result.Elevation || !result.Walkable.Contains(save.Tile) ||
            save.Rotation is < 0 or >= 6 || save.HitPoints is < 0 || save.HitPoints > result.Stats.HitPoints || save.CompletedSteps < 0)
            throw new InvalidDataException("Saved player state no longer matches the owned map or character.");
        result.Tile = save.Tile; result.Rotation = save.Rotation; result.HitPoints = save.HitPoints; result.CompletedSteps = save.CompletedSteps;
        if (initialization is not null)
        {
            // Older native saves contained exploration and MAP item changes only.
            // Introduce their missing source-created baseline once, preserving
            // current position, original loot and every subsequent item change.
            result.Inventory.InitializeCreatedItems(initialization.Items);
        }
        if (save.Items is { } items) result.Inventory.Restore(items);
        else result.Inventory.Restore(save.Inventory ?? []);
        return result;
    }
}
