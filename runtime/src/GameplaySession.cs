using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal partial class GameplaySession : Node
{
    private const string SaveSchemaV1 = "opennv-sandbox-save/v1";
    private const string SaveSchemaV2 = "opennv-sandbox-save/v2";
    private const int EquippedWeaponCount = 1;

    private readonly Dictionary<string, InventoryEntry> _inventory = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _doorStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _emptiedContainers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PoolTableInstance> _pools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PoolTableInstance.PoolState> _loadedPoolStates =
        new(StringComparer.OrdinalIgnoreCase);
    private Label? _objectiveLabel;
    private Label? _statusLabel;
    private Label? _inventoryLabel;
    private Label3D? _xrHudLabel;
    private string _savePath = "";
    private string _cellFormId = "";
    private string _entryDoorFormId = "";
    private RuntimeConfiguration _configuration = null!;
    private bool _useXrHud;
    private string? _equippedWeaponFormId;
    private string? _weaponAmmoFormId;
    private int _weaponDamage;
    private int _weaponClipSize;
    private int _ammoInMagazine;
    private int _shotsFired;

    internal bool ObjectiveComplete => ObjectiveStage == SandboxObjectiveStage.Complete;
    internal string SavePath => _savePath;
    internal int ShotsFired => _shotsFired;
    internal int AmmoInMagazine => _ammoInMagazine;
    internal int EmptiedContainersCount => _emptiedContainers.Count;
    internal int OpenDoorsCount => _doorStates.Count(entry => entry.Value);
    internal int ReserveAmmo =>
        _weaponAmmoFormId is null ? 0 : _inventory.GetValueOrDefault(_weaponAmmoFormId).Count;
    internal bool HasXrHud => _xrHudLabel is not null;
    internal float XrHudPixelSize => _xrHudLabel?.PixelSize ?? 0.0f;
    internal bool HasItem(string itemFormId) => _inventory.ContainsKey(itemFormId);
    internal bool IsContainerEmptied(string referenceFormId) => _emptiedContainers.Contains(referenceFormId);
    internal SandboxObjectiveStage ObjectiveStage =>
        _equippedWeaponFormId is null ? SandboxObjectiveStage.EquipWeapon :
        _shotsFired == 0 ? SandboxObjectiveStage.FireWeapon :
        !_inventory.Values.Any(entry => entry.RecordType == "ALCH") ? SandboxObjectiveStage.TakeAid :
        !_doorStates.GetValueOrDefault(_entryDoorFormId) ? SandboxObjectiveStage.OpenEntryDoor :
        SandboxObjectiveStage.Complete;

    internal void Configure(
        string cellFormId,
        string entryDoorFormId,
        RuntimeConfiguration configuration,
        string? configuredSavePath,
        bool useXrHud = false)
    {
        _configuration = configuration;
        Name = "GameplaySession";
        _cellFormId = cellFormId;
        _entryDoorFormId = entryDoorFormId;
        _useXrHud = useXrHud;
        _savePath = ResolvePath(configuredSavePath ?? configuration.Hud.DefaultSavePath);
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
            Position = _configuration.Hud.DesktopPanelPositionPixels.Vector2(),
            Size = _configuration.Hud.DesktopPanelSizePixels.Vector2(),
            Color = _configuration.Hud.DesktopPanelColorRgba.Color(),
        };
        layer.AddChild(panel);
        var labels = new VBoxContainer
        {
            Position = _configuration.Hud.DesktopLabelsPositionPixels.Vector2(),
            Size = _configuration.Hud.DesktopLabelsSizePixels.Vector2(),
        };
        layer.AddChild(labels);
        _objectiveLabel = new Label();
        _statusLabel = new Label();
        _inventoryLabel = new Label();
        foreach (var label in new[] { _objectiveLabel, _statusLabel, _inventoryLabel })
        {
            label.AddThemeColorOverride("font_color", _configuration.Hud.TextColorRgba.Color());
            label.AddThemeFontSizeOverride("font_size", _configuration.Hud.DesktopFontSizePixels);
            labels.AddChild(label);
        }
        var crosshair = new Label
        {
            Text = "+",
            Position = _configuration.Hud.CrosshairPositionPixels.Vector2(),
        };
        crosshair.AddThemeColorOverride("font_color", Colors.White);
        crosshair.AddThemeFontSizeOverride("font_size", _configuration.Hud.CrosshairFontSizePixels);
        layer.AddChild(crosshair);
        RefreshHud("WASD move • E activate • Left click use/fire • R reload/reset • F5 save");
    }

    internal void AttachXrHud(Node3D leftHand)
    {
        if (!_useXrHud)
            throw new InvalidOperationException("Cannot attach an XR HUD to a desktop gameplay session.");
        var mount = new Node3D
        {
            Name = "XrWristHud",
            Position = _configuration.Hud.XrMountPositionMeters.Vector3(),
            RotationDegrees = _configuration.Hud.XrMountRotationDegrees.Vector3(),
        };
        leftHand.AddChild(mount);
        _xrHudLabel = new Label3D
        {
            Name = "XrObjectiveInventory",
            FontSize = _configuration.Hud.XrFontSizePixels,
            PixelSize = _configuration.Hud.XrPixelSizeMeters,
            Modulate = _configuration.Hud.TextColorRgba.Color(),
            OutlineSize = _configuration.Hud.XrOutlineSizePixels,
            Text = "OPENNV XR HUD",
        };
        mount.AddChild(_xrHudLabel);
        RefreshHud("Left stick move • Right stick snap-turn • Grip activate • Trigger fire • B reload • X save");
    }

    internal void PrepareXrStartingLoadout(StartingWeapon loadout)
    {
        if (_equippedWeaponFormId is not null)
            return;
        AddInventory(loadout.WeaponFormId, loadout.WeaponEditorId, "WEAP", EquippedWeaponCount);
        AddInventory(loadout.AmmoFormId, loadout.AmmoEditorId, "AMMO", loadout.ReserveRounds);
        _equippedWeaponFormId = loadout.WeaponFormId;
        _weaponAmmoFormId = loadout.AmmoFormId;
        _weaponDamage = loadout.Damage;
        _weaponClipSize = loadout.ClipSize;
        _ammoInMagazine = loadout.ClipSize;
        Save();
        RefreshHud($"Equipped {loadout.WeaponEditorId} with one reserve magazine");
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

    internal void RegisterPool(PoolTableInstance table)
    {
        _pools.Add(table.ReferenceFormId, table);
        if (_loadedPoolStates.TryGetValue(table.ReferenceFormId, out var state))
            table.RestoreState(state);
    }

    internal void Notify(string status) => RefreshHud(status);

    internal void DoorChanged(DoorInstance door)
    {
        _doorStates[door.ReferenceFormId] = door.IsOpen;
        if (door.LinkedDoor is not null)
            _doorStates[door.LinkedDoor.ReferenceFormId] = door.LinkedDoor.IsOpen;
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
        var to = from - aimSource.GlobalBasis.Z * _configuration.Player.FireRayDistanceMeters;
        var hit = aimSource.GetWorld3D().DirectSpaceState.IntersectRay(
            PhysicsRayQueryParameters3D.Create(from, to, _configuration.Player.CollisionMask));
        Save();
        RefreshHud(
            hit.Count == 0
                ? $"{WeaponLabel} fired ({_weaponDamage} damage profile) • miss"
                : $"{WeaponLabel} fired ({_weaponDamage} damage profile) • hit {hit["collider"].AsGodotObject()}");
        return true;
    }

    internal bool Reload()
    {
        if (_equippedWeaponFormId is null || _weaponAmmoFormId is null)
        {
            RefreshHud("No weapon equipped");
            return false;
        }
        if (_ammoInMagazine >= _weaponClipSize)
        {
            RefreshHud($"{WeaponLabel}: magazine already full");
            return false;
        }
        if (!_inventory.TryGetValue(_weaponAmmoFormId, out var reserve) || reserve.Count <= 0)
        {
            RefreshHud($"{WeaponLabel}: no reserve ammunition");
            return false;
        }
        var loaded = Math.Min(_weaponClipSize - _ammoInMagazine, reserve.Count);
        _ammoInMagazine += loaded;
        if (loaded == reserve.Count)
            _inventory.Remove(_weaponAmmoFormId);
        else
            _inventory[_weaponAmmoFormId] = reserve with { Count = reserve.Count - loaded };
        Save();
        RefreshHud($"{WeaponLabel}: reloaded {loaded} round(s)");
        return true;
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        var document = new
        {
            schema = SaveSchemaV2,
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
            objectiveStage = (int)ObjectiveStage,
            poolTables = _pools.Values
                .OrderBy(table => table.ReferenceFormId, StringComparer.OrdinalIgnoreCase)
                .Select(table => table.CaptureState())
                .Select(state => new
                {
                    referenceFormId = state.ReferenceFormId,
                    balls = state.Balls.Select(ball => new
                    {
                        referenceFormId = ball.ReferenceFormId,
                        position = Vector(ball.Position),
                        rotation = Quaternion(ball.Rotation),
                        linearVelocity = Vector(ball.LinearVelocity),
                        angularVelocity = Vector(ball.AngularVelocity),
                        pocketed = ball.Pocketed,
                    }),
                }),
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
        schema = SaveSchemaV2,
        savePath = _savePath,
        objectiveStage = (int)ObjectiveStage,
        objectiveComplete = ObjectiveComplete,
        inventoryEntries = _inventory.Count,
        inventoryCount = _inventory.Values.Sum(entry => entry.Count),
        equippedWeaponFormId = _equippedWeaponFormId,
        weaponAmmoFormId = _weaponAmmoFormId,
        weaponDamage = _weaponDamage,
        weaponClipSize = _weaponClipSize,
        ammoInMagazine = _ammoInMagazine,
        reserveAmmo = ReserveAmmo,
        shotsFired = _shotsFired,
        removedReferences = _removedReferences.Count,
        emptiedContainers = _emptiedContainers.Count,
        openDoors = _doorStates.Count(entry => entry.Value),
        poolTables = _pools.Count,
        poolBalls = _pools.Values.Sum(table => table.BallCount),
        pocketedPoolBalls = _pools.Values.Sum(table => table.PocketedBallCount),
    };

    private void Load(string cellFormId)
    {
        if (!File.Exists(_savePath))
            return;
        using var document = JsonDocument.Parse(File.ReadAllText(_savePath));
        var root = document.RootElement;
        var schema = root.GetProperty("schema").GetString();
        if (schema != SaveSchemaV1 && schema != SaveSchemaV2)
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
        if (schema == SaveSchemaV2 && root.TryGetProperty("poolTables", out var pools))
        {
            foreach (var pool in pools.EnumerateArray())
            {
                var referenceFormId = pool.GetProperty("referenceFormId").GetString()!;
                var balls = pool.GetProperty("balls").EnumerateArray()
                    .Select(ball => new PoolBallInstance.BallState(
                        ball.GetProperty("referenceFormId").GetString()!,
                        ReadVector(ball.GetProperty("position")),
                        ReadQuaternion(ball.GetProperty("rotation")),
                        ReadVector(ball.GetProperty("linearVelocity")),
                        ReadVector(ball.GetProperty("angularVelocity")),
                        ball.GetProperty("pocketed").GetBoolean()))
                    .ToArray();
                _loadedPoolStates.Add(
                    referenceFormId,
                    new PoolTableInstance.PoolState(referenceFormId, balls));
            }
        }
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
            SandboxObjectiveStage.EquipWeapon => "OBJECTIVE  Equip an authored weapon",
            SandboxObjectiveStage.FireWeapon => "OBJECTIVE  Fire the equipped weapon once",
            SandboxObjectiveStage.TakeAid => "OBJECTIVE  Take any authored aid item",
            SandboxObjectiveStage.OpenEntryDoor => "OBJECTIVE  Open the saloon entry door",
            _ => "OBJECTIVE COMPLETE  Goodsprings sandbox route passed",
        };
        var ammunition = _equippedWeaponFormId is null
            ? "--/--"
            : $"{_ammoInMagazine}/{_weaponClipSize}";
        var statusLine = $"{WeaponLabel} {ammunition} +{ReserveAmmo}   {status}";
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
        {
            var xrObjective = ObjectiveStage switch
            {
                SandboxObjectiveStage.EquipWeapon => "OBJ Equip weapon",
                SandboxObjectiveStage.FireWeapon => "OBJ Fire weapon",
                SandboxObjectiveStage.TakeAid => "OBJ Take aid",
                SandboxObjectiveStage.OpenEntryDoor => "OBJ Open entry door",
                _ => "OBJ Complete",
            };
            var maximumStatusCharacters = _configuration.Hud.XrMaximumStatusCharacters;
            var compactStatus = status.Length <= maximumStatusCharacters
                ? status
                : status[..maximumStatusCharacters];
            _xrHudLabel.Text =
                $"{xrObjective}\n{WeaponLabel} {ammunition} +{ReserveAmmo}\n{compactStatus}";
        }
    }

    private static string ResolvePath(string path) =>
        path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private static float[] Quaternion(Quaternion value) => [value.X, value.Y, value.Z, value.W];

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Pool save vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4)
            throw new InvalidOperationException("Pool save quaternion must contain four values.");
        return new Quaternion(values[0], values[1], values[2], values[3]).Normalized();
    }

    private string WeaponLabel =>
        _equippedWeaponFormId is not null && _inventory.TryGetValue(_equippedWeaponFormId, out var weapon)
            ? weapon.EditorId
            : "Weapon";

    internal readonly record struct StartingWeapon(
        string WeaponFormId,
        string WeaponEditorId,
        string AmmoFormId,
        string AmmoEditorId,
        int Damage,
        int ClipSize,
        int ReserveRounds);

    private readonly record struct InventoryEntry(
        string ItemFormId,
        string EditorId,
        string RecordType,
        int Count);

    internal enum SandboxObjectiveStage
    {
        EquipWeapon,
        FireWeapon,
        TakeAid,
        OpenEntryDoor,
        Complete,
    }
}
