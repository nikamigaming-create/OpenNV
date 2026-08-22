using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal partial class GameplaySession : Node
{
    private const string SaveSchema = "opennv-sandbox-save/v1";
    private const string RevolverFormId = "0008f216";
    private const string EntryDoorFormId = "0010618e";

    private readonly Dictionary<string, InventoryEntry> _inventory = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _doorStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _emptiedContainers = new(StringComparer.OrdinalIgnoreCase);
    private Label? _objectiveLabel;
    private Label? _statusLabel;
    private Label? _inventoryLabel;
    private Label3D? _xrHudLabel;
    private string _savePath = "";
    private string _cellFormId = "";
    private bool _useXrHud;
    private string? _equippedWeaponFormId;
    private string? _weaponAmmoFormId;
    private int _weaponDamage;
    private int _weaponClipSize;
    private int _ammoInMagazine;
    private int _shotsFired;

    internal bool ObjectiveComplete => ObjectiveStage == 4;
    internal string SavePath => _savePath;
    internal int ShotsFired => _shotsFired;
    internal int AmmoInMagazine => _ammoInMagazine;
    internal bool HasItem(string itemFormId) => _inventory.ContainsKey(itemFormId);
    internal bool IsContainerEmptied(string referenceFormId) => _emptiedContainers.Contains(referenceFormId);
    internal int ObjectiveStage =>
        !_inventory.ContainsKey(RevolverFormId) ? 0 :
        _shotsFired == 0 ? 1 :
        !_inventory.Values.Any(entry => entry.RecordType == "ALCH") ? 2 :
        !_doorStates.GetValueOrDefault(EntryDoorFormId) ? 3 : 4;

    internal void Configure(string cellFormId, string? configuredSavePath, bool useXrHud = false)
    {
        Name = "GameplaySession";
        _cellFormId = cellFormId;
        _useXrHud = useXrHud;
        _savePath = ResolvePath(configuredSavePath ?? "user://saves/goodsprings-sandbox-v1.json");
        Load(cellFormId);
    }

    public override void _Ready()
    {
        if (_useXrHud)
            return;
        var layer = new CanvasLayer { Name = "GameplayHud" };
        AddChild(layer);
        var panel = new ColorRect
        {
            Position = new Vector2(18.0f, 18.0f),
            Size = new Vector2(520.0f, 132.0f),
            Color = new Color(0.015f, 0.025f, 0.02f, 0.82f),
        };
        layer.AddChild(panel);
        var labels = new VBoxContainer
        {
            Position = new Vector2(32.0f, 28.0f),
            Size = new Vector2(490.0f, 112.0f),
        };
        layer.AddChild(labels);
        _objectiveLabel = new Label();
        _statusLabel = new Label();
        _inventoryLabel = new Label();
        foreach (var label in new[] { _objectiveLabel, _statusLabel, _inventoryLabel })
        {
            label.AddThemeColorOverride("font_color", new Color(0.70f, 0.95f, 0.50f));
            label.AddThemeFontSizeOverride("font_size", 17);
            labels.AddChild(label);
        }
        var crosshair = new Label
        {
            Text = "+",
            Position = new Vector2(635.0f, 346.0f),
        };
        crosshair.AddThemeColorOverride("font_color", Colors.White);
        crosshair.AddThemeFontSizeOverride("font_size", 24);
        layer.AddChild(crosshair);
        RefreshHud("WASD move • E activate • Left click fire • F5 save");
    }

    internal void AttachXrHud(Node3D leftHand)
    {
        if (!_useXrHud)
            throw new InvalidOperationException("Cannot attach an XR HUD to a desktop gameplay session.");
        var mount = new Node3D
        {
            Name = "XrWristHud",
            Position = new Vector3(0.0f, 0.08f, -0.06f),
            RotationDegrees = new Vector3(-62.0f, 0.0f, 0.0f),
        };
        leftHand.AddChild(mount);
        _xrHudLabel = new Label3D
        {
            Name = "XrObjectiveInventory",
            FontSize = 34,
            PixelSize = 0.00125f,
            Modulate = new Color(0.70f, 0.95f, 0.50f),
            OutlineSize = 8,
            Text = "OPENNV XR HUD",
        };
        mount.AddChild(_xrHudLabel);
        RefreshHud("Left stick move • Right stick snap-turn • Grip activate • Trigger fire • X save");
    }

    internal bool IsReferenceRemoved(string referenceFormId) => _removedReferences.Contains(referenceFormId);

    internal bool IsDoorOpen(string referenceFormId) => _doorStates.GetValueOrDefault(referenceFormId);

    internal void Collect(PickupInstance pickup)
    {
        if (!_removedReferences.Add(pickup.ReferenceFormId))
            return;
        AddInventory(pickup.ItemFormId, pickup.EditorId, pickup.RecordType, pickup.Count);
        if (pickup.Weapon is { } weapon)
        {
            _equippedWeaponFormId = pickup.ItemFormId;
            _weaponAmmoFormId = weapon.AmmoFormId;
            _weaponDamage = weapon.Damage;
            _weaponClipSize = weapon.ClipSize;
            _ammoInMagazine = weapon.ClipSize;
        }
        pickup.QueueFree();
        Save();
        RefreshHud($"Picked up {pickup.EditorId} x{pickup.Count}");
    }

    internal void OpenContainer(ContainerInstance container)
    {
        if (!_emptiedContainers.Add(container.ReferenceFormId))
        {
            RefreshHud($"{container.EditorId}: empty");
            return;
        }
        if (container.Items.Any(item => !item.Resolved))
        {
            _emptiedContainers.Remove(container.ReferenceFormId);
            RefreshHud($"{container.EditorId}: unresolved leveled contents remain locked out");
            return;
        }
        var transferred = 0;
        foreach (var item in container.Items.Where(item => item.Resolved && item.Count > 0))
        {
            AddInventory(item.ItemFormId, item.EditorId, item.RecordType, item.Count);
            transferred += item.Count;
        }
        Save();
        RefreshHud($"{container.EditorId}: transferred {transferred} resolved item(s)");
    }

    internal void SaveAndNotify()
    {
        Save();
        RefreshHud("Game saved");
    }

    internal void DoorChanged(DoorInstance door)
    {
        _doorStates[door.ReferenceFormId] = door.IsOpen;
        Save();
        RefreshHud($"Door {door.ReferenceFormId}: {(door.IsOpen ? "open" : "closed")}");
    }

    internal bool Fire(Node3D aimSource)
    {
        if (_equippedWeaponFormId is null)
        {
            RefreshHud("No weapon equipped");
            return false;
        }
        if (_ammoInMagazine <= 0)
        {
            RefreshHud("Empty cylinder");
            return false;
        }
        _ammoInMagazine--;
        _shotsFired++;
        var from = aimSource.GlobalPosition;
        var to = from - aimSource.GlobalBasis.Z * 100.0f;
        var hit = aimSource.GetWorld3D().DirectSpaceState.IntersectRay(
            PhysicsRayQueryParameters3D.Create(from, to, 1));
        Save();
        RefreshHud(
            hit.Count == 0
                ? $".357 fired ({_weaponDamage} damage profile) • miss"
                : $".357 fired ({_weaponDamage} damage profile) • hit {hit["collider"].AsGodotObject()}");
        return true;
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        var document = new
        {
            schema = SaveSchema,
            cellFormId = _cellFormId,
            inventory = _inventory.Values.OrderBy(entry => entry.ItemFormId, StringComparer.OrdinalIgnoreCase),
            removedReferences = _removedReferences.Order(StringComparer.OrdinalIgnoreCase),
            doorStates = _doorStates
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            emptiedContainers = _emptiedContainers.Order(StringComparer.OrdinalIgnoreCase),
            equippedWeaponFormId = _equippedWeaponFormId,
            weaponAmmoFormId = _weaponAmmoFormId,
            weaponDamage = _weaponDamage,
            weaponClipSize = _weaponClipSize,
            ammoInMagazine = _ammoInMagazine,
            shotsFired = _shotsFired,
            objectiveStage = ObjectiveStage,
        };
        var temporary = _savePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true,
        }) + System.Environment.NewLine);
        File.Move(temporary, _savePath, true);
    }

    internal object Report() => new
    {
        schema = SaveSchema,
        savePath = _savePath,
        objectiveStage = ObjectiveStage,
        objectiveComplete = ObjectiveComplete,
        inventoryEntries = _inventory.Count,
        inventoryCount = _inventory.Values.Sum(entry => entry.Count),
        equippedWeaponFormId = _equippedWeaponFormId,
        weaponAmmoFormId = _weaponAmmoFormId,
        weaponDamage = _weaponDamage,
        weaponClipSize = _weaponClipSize,
        ammoInMagazine = _ammoInMagazine,
        shotsFired = _shotsFired,
        removedReferences = _removedReferences.Count,
        emptiedContainers = _emptiedContainers.Count,
        openDoors = _doorStates.Count(entry => entry.Value),
    };

    private void Load(string cellFormId)
    {
        if (!File.Exists(_savePath))
            return;
        using var document = JsonDocument.Parse(File.ReadAllText(_savePath));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != SaveSchema)
            throw new InvalidOperationException($"Unexpected sandbox save schema: {_savePath}");
        if (root.GetProperty("cellFormId").GetString() != cellFormId)
            throw new InvalidOperationException($"Sandbox save belongs to another cell: {_savePath}");
        foreach (var item in root.GetProperty("inventory").EnumerateArray())
        {
            var entry = new InventoryEntry(
                item.GetProperty("ItemFormId").GetString()!,
                item.GetProperty("EditorId").GetString()!,
                item.GetProperty("RecordType").GetString()!,
                item.GetProperty("Count").GetInt32());
            _inventory.Add(entry.ItemFormId, entry);
        }
        foreach (var value in root.GetProperty("removedReferences").EnumerateArray())
            _removedReferences.Add(value.GetString()!);
        foreach (var property in root.GetProperty("doorStates").EnumerateObject())
            _doorStates.Add(property.Name, property.Value.GetBoolean());
        foreach (var value in root.GetProperty("emptiedContainers").EnumerateArray())
            _emptiedContainers.Add(value.GetString()!);
        _equippedWeaponFormId = root.GetProperty("equippedWeaponFormId").ValueKind == JsonValueKind.String
            ? root.GetProperty("equippedWeaponFormId").GetString()
            : null;
        _weaponAmmoFormId = root.GetProperty("weaponAmmoFormId").ValueKind == JsonValueKind.String
            ? root.GetProperty("weaponAmmoFormId").GetString()
            : null;
        _weaponDamage = root.GetProperty("weaponDamage").GetInt32();
        _weaponClipSize = root.GetProperty("weaponClipSize").GetInt32();
        _ammoInMagazine = root.GetProperty("ammoInMagazine").GetInt32();
        _shotsFired = root.GetProperty("shotsFired").GetInt32();
    }

    private void AddInventory(string itemFormId, string editorId, string recordType, int count)
    {
        if (_inventory.TryGetValue(itemFormId, out var current))
            _inventory[itemFormId] = current with { Count = current.Count + count };
        else
            _inventory.Add(itemFormId, new InventoryEntry(itemFormId, editorId, recordType, count));
    }

    private void RefreshHud(string status)
    {
        var objective = ObjectiveStage switch
        {
            0 => "OBJECTIVE  Find and take the authored .357 revolver",
            1 => "OBJECTIVE  Fire the .357 once",
            2 => "OBJECTIVE  Take any authored aid item",
            3 => "OBJECTIVE  Open the saloon entry door",
            _ => "OBJECTIVE COMPLETE  Goodsprings sandbox route passed",
        };
        var ammunition = _equippedWeaponFormId is null
            ? "--/--"
            : $"{_ammoInMagazine}/{_weaponClipSize}";
        var statusLine = $".357 {ammunition}   {status}";
        var inventory = "INVENTORY  " +
            (_inventory.Count == 0
                ? "empty"
                : string.Join(
                    " • ",
                    _inventory.Values
                        .OrderBy(item => item.EditorId, StringComparer.OrdinalIgnoreCase)
                        .Select(item => $"{item.EditorId} x{item.Count}")));
        if (_objectiveLabel is not null)
        {
            _objectiveLabel.Text = objective;
            _statusLabel!.Text = statusLine;
            _inventoryLabel!.Text = inventory;
        }
        if (_xrHudLabel is not null)
            _xrHudLabel.Text = $"{objective}\n{statusLine}\n{inventory}";
    }

    private static string ResolvePath(string path) =>
        path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    private readonly record struct InventoryEntry(
        string ItemFormId,
        string EditorId,
        string RecordType,
        int Count);
}
