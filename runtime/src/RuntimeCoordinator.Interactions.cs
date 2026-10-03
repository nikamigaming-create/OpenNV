using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private Presentation.OpenXR.NativeXrRig? _nativeXr;
    private CanvasLayer? _nativeContainerLayer;
    private FalloutFormKey? _nativeContainerReference;
    private NativeOwnedGameplayHud? _nativeGameplayHud;
    private NativeOwnedHudMessages? _nativeHudMessages;
    private NativeOwnedSubtitles? _nativeSubtitles;
    private CanvasLayer? _nativePipBoyLayer;
    private NativeOwnedPipBoy? _nativePipBoy;
    private void AddNativeGameplayHud()
    {
        var layer = new CanvasLayer { Name = "NativeGameplayHud", Layer = 2 };
        _nativeGameplayHud = new(_nativePluginStack!, _configuration.Player.DesktopInput.Activate.PhysicalKey,
            NativeAimedTarget, () => _nativeXr is null && _nativePlayer is { ModalInput: false } &&
                !_nativeDoorLoading && !GetTree().Paused, () => _nativeOpeningStageDriver!.Vitals,
            () => _nativePlayer?.AmmunitionHud, () => _nativePlayer?.WeaponActionNotice, _nativeUi,
            () => _nativePlayer!.SourceControls);
        layer.AddChild(_nativeGameplayHud);
        _nativeHudMessages = new(_nativePluginStack!, _nativeInventory.Notifications,
            () => _nativeXr is null && _nativePlayer is { ModalInput: false } && !_nativeDoorLoading && !GetTree().Paused, _nativeQuestState,
            () => _nativeLoadingScreens is not null || _nativeLoadingLayer is not null);
        layer.AddChild(_nativeHudMessages);
        layer.AddChild(_nativeSubtitles ?? throw new InvalidOperationException("Source subtitles were not prepared with the player.")); AddChild(layer);
        _nativePlayer!.OpenPipBoy += OpenNativePipBoy;
        _nativePlayer.OpenCombatWheel += OpenNativeCombatWheel;
        if (_nativeXr is not null)
        {
            _nativePlayer.ResolveXrTarget = collider =>
            {
                if (_nativeReferenceEvents?.AimedReference(collider) is not { } reference ||
                    _nativeReferenceEvents.AimedPresentation(collider) is not { } root) return null;
                var record = _nativePluginStack!.GetEffective(reference.Base);
                var full = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "FULL").Data;
                return new(root, full.IsEmpty ? reference.FormKey.ToString() : FalloutDialogueTopic.Text(full.Span),
                    FalloutReferenceWorld.IsInventoryItem(record.Signature));
            };
            _nativeXr.Modal = () => _nativePlayer.ModalInput || GetTree().Paused || _nativeDoorLoading;
            _nativeXr.SetPipBoyHeld = FocusNativeXrPipBoy;
            _nativeXr.OpenCombatWheel = OpenNativeCombatWheel;
            _nativeXr.CombatWheelOpen = () => _nativeCombatWheel is not null;
            _nativeXr.CloseCombatWheel = accept => _nativeCombatWheel?.Finish(accept);
            _nativeXr.SelectCombatWheel = direction => _nativeCombatWheel?.SelectDirection(direction);
            _nativeXr.PageCombatWheel = delta => _nativeCombatWheel?.ChangePage(delta);
            _nativePlayer.PresentationChanged += RebuildNativeXrPipBoy;
        }
    }

    private void OpenNativePipBoy()
    {
        if (_nativeXr is not null) { FocusNativeXrPipBoy(true); return; }
        if (_nativePipBoyLayer is not null || _nativePlayer is not { ModalInput: false } player || _nativeDoorLoading) return;
        var driver = _nativeOpeningStageDriver!;
        var state = driver.PipBoy;
        if (!state.Available) return;
        var wasPaused = GetTree().Paused;
        var menu = CreateNativePipBoyMenu(player);
        void Close()
        {
            state.SetOpen(false); _nativePipBoyLayer?.QueueFree(); _nativePipBoyLayer = null; _nativePipBoy = null;
            GetTree().Paused = wasPaused; player.SetModalInput(false); Input.MouseMode = Input.MouseModeEnum.Captured;
            SaveNativeInteraction();
        }
        try
        {
            state.SetOpen(true);
            var appearance = driver.PlayerAppearance;
            var arms = new RuntimeNativePlayerActor(_nativePluginStack!, RuntimeLiveContentSource.Current!, appearance, null, true,
                1, NativeAmbient(_nativeActiveCell!.Cell));
            _nativePipBoy = new(menu, FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!), arms, appearance.Female, NativeAmbient(_nativeActiveCell.Cell),
                _configuration.Player.DesktopInput.PipBoy.Action, Close);
            _nativePipBoyLayer = new CanvasLayer { Name = "NativePipBoyLayer", Layer = 100, ProcessMode = ProcessModeEnum.Always };
            AddChild(_nativePipBoyLayer); _nativePipBoyLayer.AddChild(_nativePipBoy);
            player.SetModalInput(true); Input.MouseMode = Input.MouseModeEnum.Visible; GetTree().Paused = true;
            GD.Print("OPENNV_NATIVE_PIPBOY_OPEN source=live-device-menus-map-state");
        }
        catch (Exception error) { Close(); GD.PushError($"OPENNV_NATIVE_PIPBOY_FAIL {error.Message}"); }
    }
    private NativeOwnedPipBoyMenu CreateNativePipBoyMenu(RuntimeNativePlayer player)
    {
        var driver = _nativeOpeningStageDriver!;
        var source = NativePipBoyMapPosition(player);
        return new(_nativePluginStack!, driver.PipBoy, _nativeInventory, driver.Quests, _nativeReferences!,
            () => driver.Vitals, () => driver.Special, driver.PlayerName, driver.Skills, driver.Tags, driver.Traits,
            _nativeActiveCell!.Cell.Worldspace, source,
            MathF.Atan2(-player.GlobalBasis.Z.Z, -player.GlobalBasis.Z.X), driver.PlayerSkillValue, driver.UseAid, driver.Radio);
    }
    private static Vector3 NativePipBoyMapPosition(RuntimeNativePlayer player) =>
        new Vector3(player.GlobalPosition.X, -player.GlobalPosition.Z, player.GlobalPosition.Y) / player.UnitsToMeters;

    private NativeHudTarget? NativeAimedTarget()
    {
        if (_nativePlayer?.AimedObject() is not { } collider || _nativeReferenceEvents?.AimedReference(collider) is not { } reference) return null;
        var source = _nativePluginStack!.GetEffective(reference.Base);
        var full = source.ReadSubrecords().SingleOrDefault(field => field.Signature == "FULL").Data;
        if (full.IsEmpty) return null;
        var name = FalloutDialogueTopic.Text(full.Span);
        var action = source.Signature switch
        {
            "DOOR" => _nativeReferences!.Get(reference.FormKey).DoorOpen ? "sCloseButton" : "sTargetTypeOpenDoor",
            "CONT" => "sSearch",
            "FURN" => "sTargetTypeSit",
            "NPC_" or "CREA" when _nativeReferences!.IsDead(reference.FormKey) => "sSearch",
            "NPC_" or "CREA" when FalloutDialogueSpeaker.AllowsPlayerDialogue(_nativePluginStack, reference.Base, _nativeReferences!.Get(reference.FormKey).Templates) => "sTargetTypeTalk",
            _ when FalloutReferenceWorld.IsInventoryItem(source.Signature) => "sTargetTypeTake",
            _ => "sTargetTypeActivate",
        };
        var locked = _nativeReferences!.Lock(reference.FormKey);
        return new(FalloutGameSettingStrings.Read(_nativePluginStack, action), name,
            locked is null ? null : FalloutGameSettingStrings.Read(_nativePluginStack, "sLocked"));
    }
    private void SaveNativeInteraction()
    {
        // The source owns creation autosaves. Later interactions update that
        // existing campaign save without manufacturing an earlier stage.
        if (_nativeOpeningStageDriver is { HasCampaignSave: true } driver && driver.Vitals.HitPoints > 0)
            Callable.From(() =>
            {
                if (driver.Vitals.HitPoints > 0) driver.PersistWorldState(_nativeActiveCell!.Cell.FormKey);
            }).CallDeferred();
    }

    private void ActivateNativeObject(FalloutPlacedReference reference, Node3D? node, string type, FalloutFormKey actor)
    {
        var world = _nativeReferences!;
        var player = actor == _nativePluginStack!.RuntimeFormKey(0x14);
        if (!player && (type != "DOOR" || reference.Teleport is not null || world.GetLocked(reference.FormKey) != 0))
            throw new NotSupportedException($"Default interaction by {actor} requires an unlocked ordinary door.");
        if (player && type is "DOOR" or "CONT" && !world.UnlockWithKey(reference.FormKey, _nativeInventory))
        {
            GD.Print($"OPENNV_NATIVE_LOCKED reference={reference.FormKey} level={world.Lock(reference.FormKey)!.Level}");
            return;
        }
        if (type == "DOOR")
        {
            if (node?.GetChildren().OfType<RuntimeNativeDoorPortal>().SingleOrDefault() is { } portal) portal.Activate();
            else (node?.GetChildren().OfType<RuntimeNativeDoorMotion>().SingleOrDefault() ??
                throw new NotSupportedException("Door has no source animation owner.")).Activate();
            return;
        }
        if (type == "CONT" || type is "NPC_" or "CREA" && world.IsDead(reference.FormKey))
        {
            if (_nativeContainerLayer is not null) return;
            var inventory = world.Inventory(reference.FormKey, _nativeOpeningStageDriver!.PlayerLevel, _nativeGlobals).Contents;
            var record = _nativePluginStack!.GetEffective(reference.Base);
            var title = FalloutDialogueTopic.Text(record.ReadSubrecords().Single(field => field.Signature == "FULL").Data.Span);
            var wasPaused = GetTree().Paused;
            var menu = new NativeOwnedContainerMenu(_nativePluginStack, _nativeInventory, inventory,
                _nativeOpeningStageDriver.PlayerName, title, () =>
                {
                    _nativeContainerLayer!.QueueFree(); _nativeContainerLayer = null;
                    _nativeContainerReference = null;
                    GetTree().Paused = wasPaused; _nativePlayer!.SetModalInput(false);
                    Input.MouseMode = Input.MouseModeEnum.Captured; SaveNativeInteraction();
                }, () => { });
            _nativePlayer!.SetModalInput(true); Input.MouseMode = Input.MouseModeEnum.Visible;
            _nativeContainerLayer = new CanvasLayer { Name = "NativeContainerLayer", Layer = 100, ProcessMode = ProcessModeEnum.Always };
            AddChild(_nativeContainerLayer);
            _nativeContainerLayer.AddChild(menu);
            _nativeContainerReference = reference.FormKey;
            if (menu.Error is { } failure)
            {
                _nativeContainerLayer.QueueFree(); _nativeContainerLayer = null;
                _nativeContainerReference = null;
                GetTree().Paused = wasPaused; _nativePlayer.SetModalInput(false);
                Input.MouseMode = Input.MouseModeEnum.Captured;
                throw new NotSupportedException(failure);
            }
            GetTree().Paused = _nativeXr is null;
            GD.Print($"OPENNV_NATIVE_CONTAINER_OPEN reference={reference.FormKey} items={inventory.Items.Count} source=winning-CNTO-LVLI");
            return;
        }
        world.Take(reference.FormKey, _nativeInventory, _nativeOpeningStageDriver!.PlayerLevel, _nativeGlobals);
        SaveNativeInteraction();
        GD.Print($"OPENNV_NATIVE_ITEM_TAKEN reference={reference.FormKey} base={reference.Base} retained=true");
    }
}
