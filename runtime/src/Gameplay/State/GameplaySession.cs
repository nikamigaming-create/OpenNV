using System.Text.Json;
using Godot;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Gameplay.Containers;
using OpenNV.Runtime.Gameplay.Crafting;
using OpenNV.Runtime.Gameplay.Items;


using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Interactions;

namespace OpenNV.Runtime.Gameplay.State;

internal partial class GameplaySession : Node
{
    private const string SaveSchemaV1 = "opennv-sandbox-save/v1";
    private const string SaveSchemaV2 = "opennv-sandbox-save/v2";
    private const string SaveSchemaV3 = "opennv-campaign-save/v3";
    private const string SaveSchemaV4 = "opennv-campaign-save/v4";
    private const string SaveSchemaV5 = "opennv-campaign-save/v5";
    private const string SaveSchemaV6 = "opennv-campaign-save/v6";
    private const string SaveSchemaV7 = "opennv-campaign-save/v7";
    private const int EquippedWeaponCount = 1;

    private readonly Dictionary<string, InventoryEntry> _inventory = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _doorStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _emptiedContainers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PickupInstance> _pickups =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PickupInstance.PickupState> _loadedPickupStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PoolTableInstance> _pools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PoolTableInstance.PoolState> _loadedPoolStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ContainerInventoryStore _containerInventories = new();
    private GameplayUiController? _uiController;
    private ContainerInteractionView? _containerView;
    private string _savePath = "";
    private string _cellFormId = "";
    private string _cellEditorId = "";
    private string _activeCellFormId = "";
    private string _activeCellEditorId = "";
    private string? _loadedActiveCellFormId;
    private bool _activeCellRestored;
    private IReadOnlyDictionary<string, WorldSpace> _worldSpaces =
        new Dictionary<string, WorldSpace>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<PortalEdge> _portalEdges = new HashSet<PortalEdge>();
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
    private int? _weaponAnimationType;
    private int _ammoInMagazine;
    private int _shotsFired;
    private OpeningCampaignState? _openingState;
    private OwnedGameplayUiPresentation? _gameplayUi;
    private OpeningGameplayVitalsContract? _vitalsContract;
    private GameplayVitals? _vitals;
    private Func<GamebryoHitscanHit, bool>? _hitscanHitHandler;

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
    internal bool IsContainerOpen => _containerView?.IsOpen == true;
    internal bool HasItem(string itemFormId) => _inventory.ContainsKey(itemFormId);
    internal OpeningCampaignState? OpeningState => _openingState;
    internal string ActiveCellFormId => _activeCellFormId;
    internal string ActiveCellEditorId => _activeCellEditorId;
    internal bool ActiveCellRestored => _activeCellRestored;
    internal bool IsContainerEmptied(string referenceFormId) => _emptiedContainers.Contains(referenceFormId);
    internal SandboxObjectiveStage ObjectiveStage =>
        _equippedWeaponFormId is null ? SandboxObjectiveStage.EquipWeapon :
        _shotsFired == 0 ? SandboxObjectiveStage.FireWeapon :
        !_inventory.Values.Any(entry => entry.RecordType == "ALCH") ? SandboxObjectiveStage.TakeAid :
        !_doorStates.GetValueOrDefault(_entryDoorFormId) ? SandboxObjectiveStage.OpenEntryDoor :
        SandboxObjectiveStage.Complete;
    internal int? PlayerHitPoints => _vitals?.HitPoints;

    internal OpeningEquippedWeaponState? CaptureOpeningEquippedWeaponState() =>
        _equippedWeaponFormId is null
            ? null
            : new OpeningEquippedWeaponState(
                _equippedWeaponFormId,
                _weaponAmmoFormId,
                _weaponDamage,
                _weaponClipSize,
                _ammoInMagazine)
            {
                AnimationType = _weaponAnimationType,
            };

    internal static bool CanContinueOpening(
        string savePath,
        string expectedCellFormId,
        IReadOnlySet<string> allowedActiveCellFormIds,
        OpeningGameplayVitalsContract vitalsContract,
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
                _vitalsContract = vitalsContract,
            };
            probe.Load(expectedCellFormId);
            probe.DeriveMissingVitals();
            if (!allowedActiveCellFormIds.Contains(probe._activeCellFormId))
                return false;
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
        OwnedGameplayUiPresentation? gameplayUi = null,
        OpeningGameplayVitalsContract? vitalsContract = null)
    {
        if (useXrHud && useClassicDioramaHud)
            throw new ArgumentException(
                "Classic Diorama and OpenXR HUDs are separate presentation adapters.");
        _configuration = configuration;
        Name = "GameplaySession";
        _cellFormId = cellFormId;
        _cellEditorId = cellEditorId;
        _activeCellFormId = cellFormId;
        _activeCellEditorId = cellEditorId;
        _entryDoorFormId = entryDoorFormId;
        _useXrHud = useXrHud;
        _showHud = showHud;
        _useClassicDioramaHud = useClassicDioramaHud;
        _objectiveOverride = objectiveOverride;
        _gameplayUi = gameplayUi;
        _vitalsContract = vitalsContract;
        _savePath = ResolvePath(configuredSavePath ?? configuration.Hud.DefaultSavePath);
        if (loadExistingSave)
            Load(cellFormId);
        DeriveMissingVitals();
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
        _containerView = new ContainerInteractionView();
        AddChild(_containerView);
        _containerView.Configure(_useXrHud);
        _craftingView = new CraftingInteractionView();
        AddChild(_craftingView);
        _craftingView.Configure(_useXrHud);
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
        IEnumerable<CellContentLoader.LoadedContent> contents,
        IEnumerable<(string FromCellFormId, string ToCellFormId)> portalEdges)
    {
        var loadedContents = contents.ToArray();
        _worldSpaces = loadedContents.ToDictionary(
            content => content.FormId,
            content => new WorldSpace(content.FormId, content.EditorId),
            StringComparer.OrdinalIgnoreCase);
        _portalEdges = portalEdges
            .Select(edge => PortalEdge.Create(edge.FromCellFormId, edge.ToCellFormId))
            .ToHashSet();
        var restoredCellFormId = _loadedActiveCellFormId ?? _cellFormId;
        if (!_worldSpaces.TryGetValue(restoredCellFormId, out var activeSpace))
            throw new InvalidOperationException(
                $"Saved active CELL is outside the prepared route: {restoredCellFormId}");
        _activeCellFormId = activeSpace.FormId;
        _activeCellEditorId = activeSpace.EditorId;
        _activeCellRestored = _loadedActiveCellFormId is not null;
        _player = player;
        SynchronizeHeldWeaponPresentation();
        _loadedPlayerTransform?.Apply(player);
        _mapMarkers = loadedContents
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

    internal void SetGameplayUiVisible(bool visible)
    {
        if (!visible && IsContainerOpen)
            CloseContainer();
        _uiController?.SetGameplayVisible(visible);
    }

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
            ? equippedItem.DisplayName ?? equippedItem.EditorId
            : "None";
        var controls = _configuration.Player.DesktopInput;
        return new GameplayUiSnapshot(
            _activeCellFormId,
            _activeCellEditorId,
            _player?.GlobalPosition ?? Vector3.Zero,
            _lastStatus,
            objective,
            opening?.PlayerName ?? "",
            opening?.Completed == true,
            _vitals?.Level,
            _vitals?.HitPoints,
            _vitals?.MaximumHitPoints,
            _vitals?.ActionPoints,
            _vitals?.MaximumActionPoints,
            _vitals?.ExperiencePoints,
            _vitals?.NextLevelExperiencePoints,
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
                    item.DisplayName ?? item.EditorId,
                    item.RecordType,
                    item.Definition.Value,
                    item.Definition.Weight,
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
        AddInventory(
            loadout.WeaponFormId,
            loadout.WeaponEditorId,
            loadout.WeaponDisplayName,
            "WEAP",
            EquippedWeaponCount);
        AddInventory(
            loadout.AmmoFormId,
            loadout.AmmoEditorId,
            loadout.AmmoDisplayName,
            "AMMO",
            loadout.ReserveRounds);
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

    internal void RegisterPickup(PickupInstance pickup)
    {
        _pickups.Add(pickup.ReferenceFormId, pickup);
        if (_loadedPickupStates.TryGetValue(pickup.ReferenceFormId, out var state))
            pickup.RestoreState(state);
    }

    internal void PickupMoved(PickupInstance pickup)
    {
        if (!_pickups.TryGetValue(pickup.ReferenceFormId, out var registered) ||
            registered != pickup)
            throw new InvalidOperationException("Moved pickup is not registered to this session.");
        Save();
        RefreshHud($"Moved {pickup.DisplayName ?? pickup.EditorId}");
    }

    internal void Collect(PickupInstance pickup)
    {
        if (!_removedReferences.Add(pickup.ReferenceFormId))
            return;
        pickup.Drop();
        _pickups.Remove(pickup.ReferenceFormId);
        AddInventory(pickup.Item, pickup.Count);
        if (pickup.Weapon is { } weapon)
        {
            _equippedWeaponFormId = pickup.ItemFormId;
            _weaponAmmoFormId = weapon.AmmoFormId;
            _weaponDamage = weapon.Damage;
            _weaponClipSize = weapon.ClipSize;
            _ammoInMagazine = weapon.ClipSize;
            SynchronizeHeldWeaponPresentation();
        }
        pickup.QueueFree();
        Save();
        RefreshHud($"Picked up {pickup.EditorId} x{pickup.Count}");
    }

    internal void OpenContainer(ContainerInstance container)
    {
        if (string.IsNullOrWhiteSpace(container.DisplayName) ||
            container.Items.Any(item => !item.Resolved || string.IsNullOrWhiteSpace(item.DisplayName)))
        {
            RefreshHud("Container names or contents are unresolved; rebuild the owned cache");
            return;
        }
        if (_containerView is null)
            throw new InvalidOperationException("Container interaction UI is not ready.");
        ClosePipBoy();
        var snapshot = _containerInventories.Register(
            container,
            _emptiedContainers.Contains(container.ReferenceFormId));
        SynchronizeContainerEmptyMarker(snapshot.ReferenceFormId);
        _containerView.Open(
            snapshot,
            BuildPlayerContainerInventory(),
            itemFormId => TakeOneFromContainer(snapshot.ReferenceFormId, itemFormId),
            itemFormId => StoreOneInContainer(snapshot.ReferenceFormId, itemFormId),
            () => TakeAllFromContainer(snapshot.ReferenceFormId),
            CloseContainer);
        RefreshHud($"Opened {snapshot.DisplayName}: {snapshot.Items.Sum(item => item.RemainingCount)} item(s)");
    }

    internal void TakeOneFromContainer(string referenceFormId, string itemFormId)
    {
        var available = _containerInventories.Snapshot(referenceFormId).Items.Single(item =>
            item.ItemFormId.Equals(itemFormId, StringComparison.OrdinalIgnoreCase));
        ValidateInventoryAddition(available.Definition, 1);
        var transfer = _containerInventories.TakeOne(referenceFormId, itemFormId);
        AddInventory(transfer.Definition, transfer.Count);
        SynchronizeContainerEmptyMarker(referenceFormId);
        Save();
        _containerView!.Refresh(
            _containerInventories.Snapshot(referenceFormId),
            BuildPlayerContainerInventory());
        RefreshHud($"Took {transfer.DisplayName} x{transfer.Count}");
    }

    internal void TakeAllFromContainer(string referenceFormId)
    {
        foreach (var item in _containerInventories.Snapshot(referenceFormId).Items)
            ValidateInventoryAddition(item.Definition, item.RemainingCount);
        var transfers = _containerInventories.TakeAll(referenceFormId);
        foreach (var transfer in transfers)
            AddInventory(transfer.Definition, transfer.Count);
        SynchronizeContainerEmptyMarker(referenceFormId);
        Save();
        _containerView!.Refresh(
            _containerInventories.Snapshot(referenceFormId),
            BuildPlayerContainerInventory());
        RefreshHud($"Took all: {transfers.Sum(transfer => transfer.Count)} item(s)");
    }

    internal void StoreOneInContainer(string referenceFormId, string itemFormId)
    {
        var normalizedItemFormId = FalloutFormId.Normalize(itemFormId);
        if (!_inventory.TryGetValue(normalizedItemFormId, out var item) || item.Count <= 0)
            throw new InvalidOperationException(
                $"Player inventory item is unavailable: {normalizedItemFormId}");
        if (string.IsNullOrWhiteSpace(item.DisplayName))
        {
            RefreshHud($"{item.EditorId} has no owned display identity; rebuild the item cache");
            return;
        }
        var displayName = item.DisplayName;
        _containerInventories.Put(
            referenceFormId,
            new ContainerTransfer(item.Definition, 1));
        if (item.Count == 1)
        {
            _inventory.Remove(normalizedItemFormId);
            if (normalizedItemFormId.Equals(
                    _equippedWeaponFormId,
                    StringComparison.OrdinalIgnoreCase))
                ClearEquippedWeapon();
        }
        else
            _inventory[normalizedItemFormId] = item with { Count = item.Count - 1 };
        SynchronizeContainerEmptyMarker(referenceFormId);
        Save();
        _containerView!.Refresh(
            _containerInventories.Snapshot(referenceFormId),
            BuildPlayerContainerInventory());
        RefreshHud($"Stored {displayName}");
    }

    private void CloseContainer()
    {
        if (_containerView?.IsOpen != true)
            return;
        _containerView.Close();
        RefreshHud("Container closed");
    }

    private void SynchronizeContainerEmptyMarker(string referenceFormId)
    {
        if (_containerInventories.IsEmpty(referenceFormId))
            _emptiedContainers.Add(referenceFormId);
        else
            _emptiedContainers.Remove(referenceFormId);
    }

    internal void SaveAndNotify()
    {
        Save();
        RefreshHud("Game saved");
    }

    internal void StoreOpeningState(OpeningCampaignState state)
    {
        state.Validate();
        var knownDefinitions = _inventory.Values
            .ToDictionary(
                item => item.ItemFormId,
                item => item,
                StringComparer.OrdinalIgnoreCase);
        _inventory.Clear();
        _openingState = state;
        if (_vitalsContract is not null && (_vitals is null || !state.Completed))
            _vitals = _vitalsContract.CreateInitial(state);
        foreach (var item in state.Inventory)
        {
            var knownDefinition = knownDefinitions.TryGetValue(item.FormId, out var knownItem) &&
                knownItem.EditorId.Equals(item.EditorId, StringComparison.OrdinalIgnoreCase) &&
                knownItem.RecordType.Equals(item.RecordType, StringComparison.Ordinal)
                    ? knownItem.Definition
                    : null;
            _inventory[item.FormId] = new InventoryEntry(
                new ItemDefinition(
                    item.FormId,
                    item.EditorId,
                    knownDefinition?.DisplayName,
                    item.RecordType,
                    knownDefinition?.Value,
                    knownDefinition?.Weight),
                item.Count);
        }
        if (state.EquippedWeapon is { } weapon)
        {
            _equippedWeaponFormId = weapon.WeaponFormId;
            _weaponAmmoFormId = weapon.AmmoFormId;
            _weaponDamage = weapon.Damage;
            _weaponClipSize = weapon.ClipSize;
            _ammoInMagazine = weapon.AmmoInMagazine;
            _weaponAnimationType = weapon.AnimationType;
        }
        else
            ClearEquippedWeapon();
        SynchronizeHeldWeaponPresentation();
        Save();
    }

    private void ClearEquippedWeapon()
    {
        _equippedWeaponFormId = null;
        _weaponAmmoFormId = null;
        _weaponDamage = 0;
        _weaponClipSize = 0;
        _ammoInMagazine = 0;
        _weaponAnimationType = null;
        SynchronizeHeldWeaponPresentation();
    }

    private void SynchronizeHeldWeaponPresentation() =>
        _player?.SynchronizeHeldWeapon(_equippedWeaponFormId);

    internal void RegisterPool(PoolTableInstance table)
    {
        _pools.Add(table.ReferenceFormId, table);
        if (_loadedPoolStates.TryGetValue(table.ReferenceFormId, out var state))
            table.RestoreState(state);
    }

    internal bool TryGetLoadedPoolStateForProof(
        string referenceFormId,
        out PoolTableInstance.PoolState state) =>
        _loadedPoolStates.TryGetValue(referenceFormId, out state);

    internal bool TryGetLoadedPickupStateForProof(
        string referenceFormId,
        out PickupInstance.PickupState state) =>
        _loadedPickupStates.TryGetValue(referenceFormId, out state);

    internal void Notify(string status) => RefreshHud(status);

    internal void DoorChanged(DoorInstance door)
    {
        _doorStates[door.ReferenceFormId] = door.IsOpen;
        if (door.LinkedDoor is not null)
            _doorStates[door.LinkedDoor.ReferenceFormId] = door.LinkedDoor.IsOpen;
        Save();
        RefreshHud($"Door {door.ReferenceFormId}: {(door.IsOpen ? "open" : "closed")}");
    }

    internal void CrossPortal(
        string expectedFromCellFormId,
        string targetCellFormId,
        DoorInstance sourceDoor)
    {
        if (!_activeCellFormId.Equals(expectedFromCellFormId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Portal source CELL is stale: active={_activeCellFormId} " +
                $"expected={expectedFromCellFormId}");
        if (!_portalEdges.Contains(PortalEdge.Create(expectedFromCellFormId, targetCellFormId)))
            throw new InvalidOperationException(
                $"Portal edge is outside the prepared route: " +
                $"{expectedFromCellFormId} -> {targetCellFormId}");
        if (!_worldSpaces.TryGetValue(targetCellFormId, out var targetSpace))
            throw new InvalidOperationException(
                $"Portal target CELL is not loaded: {targetCellFormId}");
        if (!sourceDoor.IsOpen || sourceDoor.LinkedDoor?.IsOpen != true)
            throw new InvalidOperationException(
                $"Portal crossing requires an open reciprocal door: {sourceDoor.ReferenceFormId}");

        _activeCellFormId = targetSpace.FormId;
        _activeCellEditorId = targetSpace.EditorId;
        _doorStates[sourceDoor.ReferenceFormId] = true;
        _doorStates[sourceDoor.LinkedDoor.ReferenceFormId] = true;
        Save();
        RefreshHud($"Entered {_activeCellEditorId} ({_activeCellFormId})");
        GD.Print(
            $"OPENNV_ACTIVE_CELL from={expectedFromCellFormId} " +
            $"to={_activeCellFormId} door={sourceDoor.ReferenceFormId}");
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
        var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask);
        query.CollideWithAreas = false;
        var hit = aimSource.GetWorld3D().DirectSpaceState.IntersectRay(query);
        var worldHitDistance = hit.Count == 0
            ? float.PositiveInfinity
            : from.DistanceTo(hit["position"].AsVector3());
        var actorCandidates = NodeTraversal.Descendants<GamebryoActorCollision>(
                aimSource.GetTree().Root)
            .Where(candidate => (candidate.CollisionLayer & collisionMask) != 0u)
            .Select(candidate => new
            {
                Collision = candidate,
                Intersects = candidate.IntersectsSegment(from, to, out var distance),
                Distance = distance,
            })
            .ToArray();
        var actorHit = actorCandidates
            .Where(candidate => candidate.Intersects)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        Node? collider = actorHit is not null && actorHit.Distance < worldHitDistance
            ? actorHit.Collision
            : hit.Count != 0
                ? hit["collider"].AsGodotObject() as Node
                : null;
        var gameplayHit = collider is not null &&
            _hitscanHitHandler?.Invoke(new GamebryoHitscanHit(
                _equippedWeaponFormId,
                _weaponAnimationType,
                collider)) == true;
        Save();
        RefreshHud(
            collider is null
                ? $"{WeaponLabel} fired ({_weaponDamage} damage profile) • miss"
                : $"{WeaponLabel} fired ({_weaponDamage} damage profile) • hit {collider}");
        return true;
    }

    internal bool CanHitscanReach(
        Node3D aimSource,
        uint collisionMask,
        Node3D target) => CanHitscanReach(
            aimSource.GlobalPosition,
            aimSource.GlobalPosition +
                -aimSource.GlobalBasis.Z * _configuration.Player.FireRayDistanceMeters,
            collisionMask,
            target);

    internal bool CanHitscanReach(
        Vector3 from,
        Vector3 aimTarget,
        uint collisionMask,
        Node3D target)
    {
        if (_equippedWeaponFormId is null || _ammoInMagazine <= 0)
            return false;
        var offset = aimTarget - from;
        if (offset.Length() > _configuration.Player.FireRayDistanceMeters)
            return false;
        var to = from + offset.Normalized() * _configuration.Player.FireRayDistanceMeters;
        var actorCollision = NodeTraversal.Descendants<GamebryoActorCollision>(target)
            .SingleOrDefault();
        if (actorCollision is null ||
            !actorCollision.IntersectsSegment(from, to, out var actorDistance))
            return false;
        var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask);
        query.CollideWithAreas = false;
        var hit = target.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return true;
        return from.DistanceTo(hit["position"].AsVector3()) >= actorDistance;
    }

    internal void SetHitscanHitHandler(Func<GamebryoHitscanHit, bool>? handler) =>
        _hitscanHitHandler = handler;

    internal static bool WorldLineIsClear(
        Node3D worldOwner,
        Vector3 from,
        Vector3 to,
        uint collisionMask)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask);
        query.CollideWithAreas = false;
        return worldOwner.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
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
            schema = SaveSchemaV7,
            cellFormId = _cellFormId,
            activeCellFormId = _activeCellFormId,
            opening = _openingState,
            vitals = _vitals,
            inventory = _inventory.Values
                .OrderBy(entry => entry.ItemFormId, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new
                {
                    entry.ItemFormId,
                    entry.EditorId,
                    entry.DisplayName,
                    entry.RecordType,
                    Value = entry.Definition.Value,
                    Weight = entry.Definition.Weight,
                    entry.Count,
                }),
            removedReferences = _removedReferences.Order(StringComparer.OrdinalIgnoreCase),
            doorStates = _doorStates
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            emptiedContainers = _emptiedContainers.Order(StringComparer.OrdinalIgnoreCase),
            containerInventories = _containerInventories.Capture().Select(container => new
            {
                referenceFormId = container.ReferenceFormId,
                editorId = container.EditorId,
                displayName = container.DisplayName,
                items = container.Items.Select(item => new
                {
                    itemFormId = item.ItemFormId,
                    editorId = item.EditorId,
                    displayName = item.DisplayName,
                    recordType = item.RecordType,
                    value = item.Definition.Value,
                    weight = item.Definition.Weight,
                    remainingCount = item.RemainingCount,
                }),
            }),
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
            weaponAnimationType = _weaponAnimationType,
            ammoInMagazine = _ammoInMagazine,
            shotsFired = _shotsFired,
            objectiveStage = (int)ObjectiveStage,
            pickupTransforms = _pickups.Values
                .Where(pickup => pickup.CanGrab)
                .OrderBy(pickup => pickup.ReferenceFormId, StringComparer.OrdinalIgnoreCase)
                .Select(pickup => pickup.CaptureState())
                .Select(state => new
                {
                    referenceFormId = state.ReferenceFormId,
                    position = Vector(state.Position),
                    rotation = Quaternion(state.Rotation),
                    linearVelocity = Vector(state.LinearVelocity),
                    angularVelocity = Vector(state.AngularVelocity),
                }),
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
        var temporary = $"{_savePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(document, new JsonSerializerOptions
                {
                    WriteIndented = true,
                }) + System.Environment.NewLine);
            for (var attempt = 1;
                 attempt <= _configuration.Persistence.AtomicReplaceAttempts;
                 attempt++)
            {
                try
                {
                    File.Move(temporary, _savePath, true);
                    break;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException &&
                    attempt < _configuration.Persistence.AtomicReplaceAttempts)
                {
                    Thread.Sleep(
                        _configuration.Persistence.AtomicReplaceRetryMilliseconds);
                }
            }
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal int ApplySourceDamage(int damage)
    {
        if (damage <= 0 || _vitals is null || _vitals.HitPoints <= 0)
            throw new InvalidOperationException(
                "Source damage cannot be applied to the current gameplay vitals.");
        var hitPoints = Math.Max(0, _vitals.HitPoints - damage);
        _vitals = _vitals with { HitPoints = hitPoints };
        _vitals.Validate();
        Save();
        RefreshHud($"Hit points: {hitPoints}/{_vitals.MaximumHitPoints}");
        return hitPoints;
    }

    internal object Report() => new
    {
        schema = SaveSchemaV7,
        savePath = _savePath,
        routeCellFormId = _cellFormId,
        activeCellFormId = _activeCellFormId,
        activeCellEditorId = _activeCellEditorId,
        activeCellRestored = _activeCellRestored,
        objectiveStage = (int)ObjectiveStage,
        objectiveComplete = ObjectiveComplete,
        inventoryEntries = _inventory.Count,
        inventoryCount = _inventory.Values.Sum(entry => entry.Count),
        equippedWeaponFormId = _equippedWeaponFormId,
        weaponAmmoFormId = _weaponAmmoFormId,
        weaponDamage = _weaponDamage,
        weaponClipSize = _weaponClipSize,
        weaponAnimationType = _weaponAnimationType,
        ammoInMagazine = _ammoInMagazine,
        reserveAmmo = ReserveAmmo,
        shotsFired = _shotsFired,
        removedReferences = _removedReferences.Count,
        movablePickups = _pickups.Values.Count(pickup => pickup.CanGrab),
        unsupportedPickupPhysics = _pickups.Values.Count(pickup => !pickup.CanGrab),
        emptiedContainers = _emptiedContainers.Count,
        containerInventories = _containerInventories.RegisteredContainers,
        containerRemainingItems = _containerInventories.RemainingItemCount,
        openDoors = _doorStates.Count(entry => entry.Value),
        poolTables = _pools.Count,
        poolBalls = _pools.Values.Sum(table => table.BallCount),
        pocketedPoolBalls = _pools.Values.Sum(table => table.PocketedBallCount),
        playerTransformRestored = _loadedPlayerTransform is not null,
        vitals = _vitals,
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
        if (schema != SaveSchemaV1 && schema != SaveSchemaV2 &&
            schema != SaveSchemaV3 && schema != SaveSchemaV4 && schema != SaveSchemaV5 &&
            schema != SaveSchemaV6 && schema != SaveSchemaV7)
            throw new InvalidOperationException($"Unexpected sandbox save schema: {_savePath}");
        if (root.GetProperty("cellFormId").GetString() != cellFormId)
            throw new InvalidOperationException($"Sandbox save belongs to another cell: {_savePath}");
        if (schema == SaveSchemaV4 || schema == SaveSchemaV5 || schema == SaveSchemaV6 ||
            schema == SaveSchemaV7)
        {
            if (!root.TryGetProperty("activeCellFormId", out var activeCell) ||
                activeCell.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Campaign save has no active CELL identity.");
            _loadedActiveCellFormId = FalloutFormId.Normalize(activeCell.GetString()!);
            _activeCellFormId = _loadedActiveCellFormId;
        }
        foreach (var item in root.GetProperty("inventory").EnumerateArray())
        {
            var entry = new InventoryEntry(
                new ItemDefinition(
                    item.GetProperty("ItemFormId").GetString()!,
                    item.GetProperty("EditorId").GetString()!,
                    item.TryGetProperty("DisplayName", out var displayName) &&
                        displayName.ValueKind == JsonValueKind.String
                            ? displayName.GetString()
                            : null,
                    item.GetProperty("RecordType").GetString()!,
                    item.TryGetProperty("Value", out var value) &&
                        value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null,
                    item.TryGetProperty("Weight", out var weight) &&
                        weight.ValueKind == JsonValueKind.Number ? weight.GetSingle() : null),
                item.GetProperty("Count").GetInt32());
            _inventory.Add(entry.ItemFormId, entry);
        }
        foreach (var value in root.GetProperty("removedReferences").EnumerateArray())
            _removedReferences.Add(value.GetString()!);
        foreach (var property in root.GetProperty("doorStates").EnumerateObject())
            _doorStates.Add(property.Name, property.Value.GetBoolean());
        foreach (var value in root.GetProperty("emptiedContainers").EnumerateArray())
            _emptiedContainers.Add(value.GetString()!);
        if (schema == SaveSchemaV5 || schema == SaveSchemaV6 || schema == SaveSchemaV7)
        {
            if (!root.TryGetProperty("containerInventories", out var containers))
                throw new InvalidOperationException(
                    "Campaign save has no container inventory state.");
            _containerInventories.Load(containers);
            _containerInventories.ValidateEmptiedReferences(_emptiedContainers);
        }
        _equippedWeaponFormId = root.GetProperty("equippedWeaponFormId").ValueKind == JsonValueKind.String
            ? root.GetProperty("equippedWeaponFormId").GetString()
            : null;
        _weaponAmmoFormId = root.GetProperty("weaponAmmoFormId").ValueKind == JsonValueKind.String
            ? root.GetProperty("weaponAmmoFormId").GetString()
            : null;
        _weaponDamage = root.GetProperty("weaponDamage").GetInt32();
        _weaponClipSize = root.GetProperty("weaponClipSize").GetInt32();
        _weaponAnimationType = root.TryGetProperty(
            "weaponAnimationType", out var weaponAnimationType) &&
            weaponAnimationType.ValueKind != JsonValueKind.Null
                ? weaponAnimationType.GetInt32()
                : null;
        _ammoInMagazine = root.GetProperty("ammoInMagazine").GetInt32();
        _shotsFired = root.GetProperty("shotsFired").GetInt32();
        if (schema == SaveSchemaV7)
        {
            if (!root.TryGetProperty("pickupTransforms", out var pickups) ||
                pickups.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException(
                    "Campaign save has no movable pickup transform state.");
            foreach (var pickup in pickups.EnumerateArray())
            {
                var referenceFormId = pickup.GetProperty("referenceFormId").GetString()!;
                _loadedPickupStates.Add(
                    referenceFormId,
                    new PickupInstance.PickupState(
                        referenceFormId,
                        ReadVector(pickup.GetProperty("position")),
                        ReadQuaternion(pickup.GetProperty("rotation")),
                        ReadVector(pickup.GetProperty("linearVelocity")),
                        ReadVector(pickup.GetProperty("angularVelocity"))));
            }
        }
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
        if ((schema == SaveSchemaV3 || schema == SaveSchemaV4 || schema == SaveSchemaV5 ||
             schema == SaveSchemaV6 || schema == SaveSchemaV7) &&
            root.TryGetProperty("opening", out var opening) &&
            opening.ValueKind == JsonValueKind.Object)
            _openingState = OpeningCampaignState.Parse(opening);
        if (schema == SaveSchemaV6 || schema == SaveSchemaV7)
        {
            if (!root.TryGetProperty("vitals", out var vitals) ||
                vitals.ValueKind is not JsonValueKind.Object and not JsonValueKind.Null)
                throw new InvalidOperationException("Campaign save has malformed gameplay vitals.");
            if (vitals.ValueKind == JsonValueKind.Object)
                _vitals = ParseVitals(vitals);
        }
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

    internal bool PersistAndVerifyVitalsColdRestore()
    {
        if (_vitals is null || _vitalsContract is null)
            return false;
        Save();
        GameplaySession? probe = null;
        try
        {
            probe = new GameplaySession
            {
                _savePath = _savePath,
                _vitalsContract = _vitalsContract,
            };
            probe.Load(_cellFormId);
            probe.DeriveMissingVitals();
            return probe._vitals == _vitals;
        }
        finally
        {
            probe?.Free();
        }
    }

    private void DeriveMissingVitals()
    {
        if (_openingState is null || _vitalsContract is null)
            return;
        var expected = _vitalsContract.CreateInitial(_openingState);
        if (_vitals is null)
        {
            _vitals = expected;
            return;
        }
        _vitals.Validate();
        if (_vitals.Level != expected.Level ||
            _vitals.MaximumHitPoints != expected.MaximumHitPoints ||
            _vitals.MaximumActionPoints != expected.MaximumActionPoints ||
            _vitals.NextLevelExperiencePoints != expected.NextLevelExperiencePoints)
            throw new InvalidOperationException(
                "Saved gameplay vitals do not match the owned opening derivation contract.");
    }

    private static GameplayVitals ParseVitals(JsonElement source)
    {
        var result = new GameplayVitals(
            source.GetProperty(nameof(GameplayVitals.Level)).GetInt32(),
            source.GetProperty(nameof(GameplayVitals.HitPoints)).GetInt32(),
            source.GetProperty(nameof(GameplayVitals.MaximumHitPoints)).GetInt32(),
            source.GetProperty(nameof(GameplayVitals.ActionPoints)).GetInt32(),
            source.GetProperty(nameof(GameplayVitals.MaximumActionPoints)).GetInt32(),
            source.GetProperty(nameof(GameplayVitals.ExperiencePoints)).GetInt32(),
            source.GetProperty(nameof(GameplayVitals.NextLevelExperiencePoints)).GetInt32());
        result.Validate();
        return result;
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
                _ammoInMagazine)
            {
                AnimationType = _weaponAnimationType,
            };
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

    private PlayerContainerInventorySnapshot BuildPlayerContainerInventory()
    {
        var items = _inventory.Values
            .OrderBy(item => item.DisplayName ?? item.EditorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ItemFormId, StringComparer.OrdinalIgnoreCase)
            .Select(item => new PlayerContainerInventoryItem(
                item.ItemFormId,
                item.EditorId,
                item.DisplayName ?? item.EditorId,
                item.RecordType,
                item.Definition.Value,
                item.Definition.Weight,
                item.Count,
                item.ItemFormId.Equals(
                    _equippedWeaponFormId,
                    StringComparison.OrdinalIgnoreCase),
                !string.IsNullOrWhiteSpace(item.DisplayName)))
            .ToArray();
        return new PlayerContainerInventorySnapshot(items);
    }

    private void AddInventory(
        string itemFormId,
        string editorId,
        string? displayName,
        string recordType,
        int count)
        => AddInventory(
            new ItemDefinition(itemFormId, editorId, displayName, recordType, null, null),
            count);

    private void AddInventory(ItemDefinition definition, int count)
    {
        ValidateInventoryAddition(definition, count);
        var normalizedItemFormId = definition.FormId;
        if (_inventory.TryGetValue(normalizedItemFormId, out var current))
        {
            _inventory[normalizedItemFormId] = current with
            {
                Definition = current.Definition.Merge(definition),
                Count = checked(current.Count + count),
            };
        }
        else
        {
            _inventory.Add(
                normalizedItemFormId,
                new InventoryEntry(
                    definition,
                    count));
        }
    }

    private void ValidateInventoryAddition(ItemDefinition definition, int count)
    {
        if (count <= 0)
            throw new InvalidOperationException(
                $"Inventory item count is invalid: {definition.FormId}.");
        var normalizedItemFormId = definition.FormId;
        if (!_inventory.TryGetValue(normalizedItemFormId, out var current))
            return;
        _ = current.Definition.Merge(definition);
        _ = checked(current.Count + count);
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

    private sealed record WorldSpace(string FormId, string EditorId);

    private readonly record struct PortalEdge(string FirstCellFormId, string SecondCellFormId)
    {
        internal static PortalEdge Create(string firstCellFormId, string secondCellFormId) =>
            string.Compare(firstCellFormId, secondCellFormId, StringComparison.OrdinalIgnoreCase) <= 0
                ? new PortalEdge(firstCellFormId.ToLowerInvariant(), secondCellFormId.ToLowerInvariant())
                : new PortalEdge(secondCellFormId.ToLowerInvariant(), firstCellFormId.ToLowerInvariant());
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
        int ReserveRounds,
        string? WeaponDisplayName = null,
        string? AmmoDisplayName = null);

    internal readonly record struct InventoryEntry(ItemDefinition Definition, int Count)
    {
        internal string ItemFormId => Definition.FormId;
        internal string EditorId => Definition.EditorId;
        internal string? DisplayName => Definition.DisplayName;
        internal string RecordType => Definition.RecordType;
    }

    internal enum SandboxObjectiveStage
    {
        EquipWeapon,
        FireWeapon,
        TakeAid,
        OpenEntryDoor,
        Complete,
    }
}
