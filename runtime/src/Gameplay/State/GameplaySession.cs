using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Gameplay.State;

internal partial class GameplaySession : Node
{
    private const string SaveSchemaV1 = "opennv-sandbox-save/v1";
    private const string SaveSchemaV2 = "opennv-sandbox-save/v2";
    private const string SaveSchemaV3 = "opennv-campaign-save/v3";
    private const int EquippedWeaponCount = 1;

    private readonly Dictionary<string, InventoryEntry> _inventory = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _doorStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _emptiedContainers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PoolTableInstance> _pools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PoolTableInstance.PoolState> _loadedPoolStates =
        new(StringComparer.OrdinalIgnoreCase);
    private GameplayUiController? _uiController;
    private string _savePath = "";
    private string _cellFormId = "";
    private string _cellEditorId = "";
    private string _entryDoorFormId = "";
    private RuntimeConfiguration _configuration = null!;
    private bool _useXrHud;
    private bool _showHud = true;
    private bool _useClassicDioramaHud;
    private string? _objectiveOverride;
    private string _lastStatus = "Ready";
    private CellPlayer? _player;
    private PlayerTransformState? _loadedPlayerTransform;
    private IReadOnlyList<GameplayUiMapMarker> _mapMarkers = Array.Empty<GameplayUiMapMarker>();
    private string? _equippedWeaponFormId;
    private string? _weaponAmmoFormId;
    private int _weaponDamage;
    private int _weaponClipSize;
    private int _ammoInMagazine;
    private int _shotsFired;
    private OpeningCampaignState? _openingState;
    private OwnedGameplayUiPresentation? _gameplayUi;

    internal bool ObjectiveComplete => ObjectiveStage == SandboxObjectiveStage.Complete;
    internal string SavePath => _savePath;
    internal int ShotsFired => _shotsFired;
    internal int AmmoInMagazine => _ammoInMagazine;
    internal int WeaponClipSize => _weaponClipSize;
    internal int EmptiedContainersCount => _emptiedContainers.Count;
    internal int OpenDoorsCount => _doorStates.Count(entry => entry.Value);
    internal int ReserveAmmo =>
        _weaponAmmoFormId is null ? 0 : _inventory.GetValueOrDefault(_weaponAmmoFormId).Count;
    internal bool HasXrHud => _uiController?.HasXrHud == true;
    internal bool HasDesktopHud => _uiController?.HasDesktopHud == true;
    internal bool HasPipBoy => _uiController?.HasPipBoy == true;
    internal float XrHudPixelSize => _uiController?.XrHudPixelSize ?? 0.0f;
    internal bool IsPipBoyOpen => _uiController?.IsPipBoyOpen == true;
    internal bool HasItem(string itemFormId) => _inventory.ContainsKey(itemFormId);
    internal OpeningCampaignState? OpeningState => _openingState;
    internal bool IsContainerEmptied(string referenceFormId) => _emptiedContainers.Contains(referenceFormId);
    internal SandboxObjectiveStage ObjectiveStage =>
        _equippedWeaponFormId is null ? SandboxObjectiveStage.EquipWeapon :
        _shotsFired == 0 ? SandboxObjectiveStage.FireWeapon :
        !_inventory.Values.Any(entry => entry.RecordType == "ALCH") ? SandboxObjectiveStage.TakeAid :
        !_doorStates.GetValueOrDefault(_entryDoorFormId) ? SandboxObjectiveStage.OpenEntryDoor :
        SandboxObjectiveStage.Complete;

    internal static bool CanContinueOpening(
        string savePath,
        string expectedCellFormId,
        Func<OpeningCampaignState, bool> acceptsOpeningState)
    {
        if (!File.Exists(savePath))
            return false;
        GameplaySession? probe = null;
        try
        {
            probe = new GameplaySession
            {
                _savePath = savePath,
            };
            probe.Load(expectedCellFormId);
            return probe._openingState is not null &&
                acceptsOpeningState(probe._openingState) &&
                probe.HasConsistentOpeningGameplayState();
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            probe?.Free();
        }
    }

    internal void Configure(
        string cellFormId,
        string cellEditorId,
        string entryDoorFormId,
        RuntimeConfiguration configuration,
        string? configuredSavePath,
        bool useXrHud = false,
        bool loadExistingSave = true,
        bool showHud = true,
        bool useClassicDioramaHud = false,
        string? objectiveOverride = null,
        OwnedGameplayUiPresentation? gameplayUi = null)
    {
        if (useXrHud && useClassicDioramaHud)
            throw new ArgumentException(
                "Classic Diorama and OpenXR HUDs are separate presentation adapters.");
        _configuration = configuration;
        Name = "GameplaySession";
        _cellFormId = cellFormId;
        _cellEditorId = cellEditorId;
        _entryDoorFormId = entryDoorFormId;
        _useXrHud = useXrHud;
        _showHud = showHud;
        _useClassicDioramaHud = useClassicDioramaHud;
        _objectiveOverride = objectiveOverride;
        _gameplayUi = gameplayUi;
        _savePath = ResolvePath(configuredSavePath ?? configuration.Hud.DefaultSavePath);
        if (loadExistingSave)
            Load(cellFormId);
    }

    public override void _Ready()
    {
        _uiController = new GameplayUiController();
        AddChild(_uiController);
        _uiController.Configure(
            this,
            _configuration,
            _useXrHud,
            _showHud,
            _useClassicDioramaHud,
            _gameplayUi);
        RefreshHud(
            _useClassicDioramaHud
                ? "WASD pan • Wheel zoom • Q/E rotate 60° • Home reset • F5 save"
                : "WASD move • E activate • Left click use/fire • R reload/reset • F5 save");
    }

    internal void AttachXrHud(Node3D leftHand, Node3D aimSource)
    {
        if (_uiController is null)
            throw new InvalidOperationException("Gameplay UI is not ready for XR attachment.");
        _uiController.AttachXrHud(leftHand, aimSource);
        RefreshHud("Left stick move • Right stick snap-turn • Grip activate • Trigger fire • B reload • X save");
    }

    internal void ConfigureWorldContext(
        CellPlayer player,
        IEnumerable<CellContentLoader.LoadedContent> contents)
    {
        _player = player;
        _loadedPlayerTransform?.Apply(player);
        _mapMarkers = contents
            .SelectMany(content => content.PlacedReferences)
            .Where(reference => !IsReferenceRemoved(reference.FormId))
            .Select(reference => new GameplayUiMapMarker(
                reference.FormId,
                reference.BaseEditorId,
                reference.Placement.GlobalPosition))
            .OrderBy(marker => marker.FormId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _uiController?.Refresh();
    }

    internal void TogglePipBoy() => _uiController?.TogglePipBoy();

    internal void ClosePipBoy() => _uiController?.ClosePipBoy();

    internal void SetGameplayUiVisible(bool visible) =>
        _uiController?.SetGameplayVisible(visible);

    internal GameplayUiSnapshot BuildUiSnapshot()
    {
        var objective = _objectiveOverride ?? (ObjectiveStage switch
        {
            SandboxObjectiveStage.EquipWeapon => _configuration.Hud.Copy.ObjectiveEquipWeapon,
            SandboxObjectiveStage.FireWeapon => _configuration.Hud.Copy.ObjectiveFireWeapon,
            SandboxObjectiveStage.TakeAid => _configuration.Hud.Copy.ObjectiveTakeAid,
            SandboxObjectiveStage.OpenEntryDoor => _configuration.Hud.Copy.ObjectiveOpenEntryDoor,
            _ => _configuration.Hud.Copy.ObjectiveComplete,
        });
        var opening = _openingState;
        var equipped = _equippedWeaponFormId is not null &&
            _inventory.TryGetValue(_equippedWeaponFormId, out var equippedItem)
            ? equippedItem.EditorId
            : "None";
        var controls = _configuration.Player.DesktopInput;
        return new GameplayUiSnapshot(
            _cellFormId,
            _cellEditorId,
            _player?.GlobalPosition ?? Vector3.Zero,
            _lastStatus,
            objective,
            opening?.PlayerName ?? "",
            opening?.Completed == true,
            ObjectiveStage,
            _equippedWeaponFormId,
            equipped,
            _ammoInMagazine,
            _weaponClipSize,
            ReserveAmmo,
            _inventory.Values
                .OrderBy(item => item.EditorId, StringComparer.OrdinalIgnoreCase)
                .Select(item => new GameplayUiInventoryItem(
                    item.ItemFormId,
                    item.EditorId,
                    item.RecordType,
                    item.Count,
                    item.ItemFormId.Equals(_equippedWeaponFormId, StringComparison.OrdinalIgnoreCase)))
                .ToArray(),
            opening?.Quests
                .OrderBy(quest => quest.EditorId, StringComparer.OrdinalIgnoreCase)
                .Select(quest => new GameplayUiQuest(
                    quest.FormId,
                    quest.EditorId,
                    quest.Stage,
                    quest.Running,
                    quest.Stopped))
                .ToArray() ?? Array.Empty<GameplayUiQuest>(),
            opening?.Objectives
                .OrderBy(objective => objective.QuestEditorId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(objective => objective.Index)
                .Select(objective => new GameplayUiObjective(
                    objective.QuestEditorId,
                    objective.Index,
                    objective.State,
                    objective.Enabled,
                    objective.Text))
                .ToArray() ?? Array.Empty<GameplayUiObjective>(),
            _mapMarkers,
            [
                new GameplayUiControl("Move", $"{controls.MoveForward.PhysicalKey}/{controls.MoveLeft.PhysicalKey}/" +
                    $"{controls.MoveBackward.PhysicalKey}/{controls.MoveRight.PhysicalKey}"),
                new GameplayUiControl("Activate", controls.Activate.PhysicalKey),
                new GameplayUiControl("Use / fire", controls.Fire.Button),
                new GameplayUiControl("Reload", controls.Reload.PhysicalKey),
                new GameplayUiControl("Save", controls.Save.PhysicalKey),
                new GameplayUiControl("Pip-Boy", controls.PipBoy.PhysicalKey),
                new GameplayUiControl("Close", controls.Cancel.PhysicalKey),
            ],
            _savePath);
    }

    internal void PrepareStartingLoadout(StartingWeapon loadout)
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

    internal void StoreOpeningState(OpeningCampaignState state)
    {
        state.Validate();
        foreach (var previous in _openingState?.Inventory ?? Array.Empty<OpeningInventoryState>())
            _inventory.Remove(previous.FormId);
        _openingState = state;
        foreach (var item in state.Inventory)
            _inventory[item.FormId] = new InventoryEntry(
                item.FormId,
                item.EditorId,
                item.RecordType,
                item.Count);
        if (_equippedWeaponFormId is not null &&
            (!_inventory.TryGetValue(_equippedWeaponFormId, out var equipped) ||
                equipped.RecordType != "WEAP" ||
                _weaponAmmoFormId is null ||
                _weaponDamage <= 0 ||
                _weaponClipSize <= 0))
        {
            _equippedWeaponFormId = null;
            _weaponAmmoFormId = null;
            _weaponDamage = 0;
            _weaponClipSize = 0;
            _ammoInMagazine = 0;
        }
        Save();
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

    internal bool Fire(Node3D aimSource, uint collisionMask)
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
            PhysicsRayQueryParameters3D.Create(from, to, collisionMask));
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
        SynchronizeOpeningGameplayState();
        var capturedPlayerTransform = _player is null
            ? null
            : PlayerTransformState.Capture(_player);
        var document = new
        {
            schema = SaveSchemaV3,
            cellFormId = _cellFormId,
            opening = _openingState,
            inventory = _inventory.Values.OrderBy(entry => entry.ItemFormId, StringComparer.OrdinalIgnoreCase),
            removedReferences = _removedReferences.Order(StringComparer.OrdinalIgnoreCase),
            doorStates = _doorStates
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            emptiedContainers = _emptiedContainers.Order(StringComparer.OrdinalIgnoreCase),
            playerTransform = capturedPlayerTransform is null
                ? null
                : new
                {
                    Position = Vector(capturedPlayerTransform.Position),
                    Rotation = Quaternion(capturedPlayerTransform.Rotation),
                },
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
        schema = SaveSchemaV3,
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
        playerTransformRestored = _loadedPlayerTransform is not null,
        playerPosition = _player is null ? null : Vector(_player.GlobalPosition),
        opening = _openingState is null
            ? null
            : new
            {
                schema = _openingState.Schema,
                questFormId = _openingState.QuestFormId,
                stage = _openingState.Stage,
                completed = _openingState.Completed,
                playerName = _openingState.PlayerName,
                specialValues = _openingState.SpecialValues.Count,
                tagSkills = _openingState.TagSkillFormIds.Count,
                traits = _openingState.TraitFormIds.Count,
                quests = _openingState.Quests.Count,
                inventory = _openingState.Inventory.Count,
            },
    };

    private void Load(string cellFormId)
    {
        if (!File.Exists(_savePath))
            return;
        using var document = JsonDocument.Parse(File.ReadAllText(_savePath));
        var root = document.RootElement;
        var schema = root.GetProperty("schema").GetString();
        if (schema != SaveSchemaV1 && schema != SaveSchemaV2 && schema != SaveSchemaV3)
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
        if (schema != SaveSchemaV1 && root.TryGetProperty("poolTables", out var pools))
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
        if (schema == SaveSchemaV3 &&
            root.TryGetProperty("opening", out var opening) &&
            opening.ValueKind == JsonValueKind.Object)
            _openingState = OpeningCampaignState.Parse(opening);
        if (root.TryGetProperty("playerTransform", out var playerTransform))
        {
            if (playerTransform.ValueKind is not JsonValueKind.Object and not JsonValueKind.Null)
                throw new InvalidOperationException("Saved player transform has an invalid shape.");
            if (playerTransform.ValueKind == JsonValueKind.Object)
            {
                var hasPosition = playerTransform.TryGetProperty("Position", out var position) &&
                    position.ValueKind == JsonValueKind.Array;
                var hasRotation = playerTransform.TryGetProperty("Rotation", out var rotation) &&
                    rotation.ValueKind == JsonValueKind.Array;
                if (hasPosition && hasRotation)
                    _loadedPlayerTransform = PlayerTransformState.Parse(playerTransform);
                else if (_openingState is null)
                    throw new InvalidOperationException("Saved player transform is malformed.");
            }
        }
        if (_openingState is not null && _loadedPlayerTransform is null)
            _loadedPlayerTransform = PlayerTransformState.FromOpening(_openingState.PlayerTransform);
    }

    internal bool HasConsistentOpeningGameplayState()
    {
        if (_openingState is null || _inventory.Count != _openingState.Inventory.Count)
            return false;
        foreach (var expected in _openingState.Inventory)
        {
            if (!_inventory.TryGetValue(expected.FormId, out var actual) ||
                !actual.EditorId.Equals(expected.EditorId, StringComparison.OrdinalIgnoreCase) ||
                !actual.RecordType.Equals(expected.RecordType, StringComparison.Ordinal) ||
                actual.Count != expected.Count)
                return false;
        }
        if (_loadedPlayerTransform is null ||
            !_loadedPlayerTransform.Matches(_openingState.PlayerTransform))
            return false;
        var weapon = _openingState.EquippedWeapon;
        if (weapon is null)
            return _equippedWeaponFormId is null && _weaponAmmoFormId is null &&
                _weaponDamage == 0 && _weaponClipSize == 0 && _ammoInMagazine == 0;
        return string.Equals(
                _equippedWeaponFormId,
                weapon.WeaponFormId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                _weaponAmmoFormId,
                weapon.AmmoFormId,
                StringComparison.OrdinalIgnoreCase) &&
            _weaponDamage == weapon.Damage &&
            _weaponClipSize == weapon.ClipSize &&
            _ammoInMagazine == weapon.AmmoInMagazine;
    }

    private void SynchronizeOpeningGameplayState()
    {
        if (_openingState is not { Completed: true } opening)
            return;
        var inventory = _inventory.Values
            .OrderBy(entry => entry.ItemFormId, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new OpeningInventoryState(
                entry.ItemFormId,
                entry.EditorId,
                entry.RecordType,
                entry.Count))
            .ToArray();
        var equipped = opening.EquippedItemFormIds
            .Where(formId =>
                _inventory.TryGetValue(formId, out var item) && item.RecordType != "WEAP")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        OpeningEquippedWeaponState? weapon = null;
        if (_equippedWeaponFormId is not null)
        {
            if (!_inventory.TryGetValue(_equippedWeaponFormId, out var item) ||
                item.RecordType != "WEAP" || _weaponDamage <= 0 || _weaponClipSize <= 0 ||
                _ammoInMagazine < 0 || _ammoInMagazine > _weaponClipSize)
                throw new InvalidOperationException(
                    "Equipped weapon state cannot be joined to authoritative campaign inventory.");
            equipped.Add(_equippedWeaponFormId);
            weapon = new OpeningEquippedWeaponState(
                _equippedWeaponFormId,
                _weaponAmmoFormId,
                _weaponDamage,
                _weaponClipSize,
                _ammoInMagazine);
        }
        _openingState = opening with
        {
            Inventory = inventory,
            EquippedItemFormIds = equipped
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PlayerTransform = _player is null
                ? opening.PlayerTransform
                : OpeningTransformState.Capture(_player),
            EquippedWeapon = weapon,
        };
        _openingState.Validate();
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
        _lastStatus = status;
        _uiController?.Refresh();
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

    private sealed record PlayerTransformState(Vector3 Position, Quaternion Rotation)
    {
        internal static PlayerTransformState Capture(CellPlayer player)
        {
            var transform = player.GlobalTransform;
            return new PlayerTransformState(
                transform.Origin,
                transform.Basis.GetRotationQuaternion().Normalized());
        }

        internal static PlayerTransformState Parse(JsonElement source)
        {
            var rotationValues = source.GetProperty("Rotation")
                .EnumerateArray()
                .Select(value => value.GetSingle())
                .ToArray();
            if (rotationValues.Length != 4)
                throw new InvalidOperationException("Saved player transform rotation is malformed.");
            var result = new PlayerTransformState(
                ReadVector(source.GetProperty("Position")),
                new Quaternion(
                    rotationValues[0],
                    rotationValues[1],
                    rotationValues[2],
                    rotationValues[3]));
            result.Validate();
            return result;
        }

        internal static PlayerTransformState FromOpening(OpeningTransformState source)
        {
            source.Validate();
            return new PlayerTransformState(
                new Vector3(source.Position[0], source.Position[1], source.Position[2]),
                new Quaternion(
                    source.Rotation[0],
                    source.Rotation[1],
                    source.Rotation[2],
                    source.Rotation[3]));
        }

        internal bool Matches(OpeningTransformState source)
        {
            source.Validate();
            return Position.X == source.Position[0] &&
                Position.Y == source.Position[1] &&
                Position.Z == source.Position[2] &&
                Rotation.X == source.Rotation[0] &&
                Rotation.Y == source.Rotation[1] &&
                Rotation.Z == source.Rotation[2] &&
                Rotation.W == source.Rotation[3];
        }

        internal void Apply(CellPlayer player)
        {
            Validate();
            var scale = player.GlobalTransform.Basis.Scale;
            player.GlobalTransform = new Transform3D(
                new Basis(Rotation).Scaled(scale),
                Position);
        }

        private void Validate()
        {
            if (!float.IsFinite(Position.X) ||
                !float.IsFinite(Position.Y) ||
                !float.IsFinite(Position.Z) ||
                !float.IsFinite(Rotation.X) ||
                !float.IsFinite(Rotation.Y) ||
                !float.IsFinite(Rotation.Z) ||
                !float.IsFinite(Rotation.W) ||
                !Rotation.IsNormalized())
                throw new InvalidOperationException("Saved player transform is invalid.");
        }
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

    internal readonly record struct InventoryEntry(
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
