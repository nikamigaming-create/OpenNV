using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private CanvasLayer? _nativeLockpickLayer;

    private void OpenNativeLockpick(FalloutPlacedReference reference)
    {
        if (_nativeLockpickLayer is not null) return;
        if (_nativeXr is not null) throw new NotSupportedException("The initial force-lock panel has no XR input surface.");
        if (_nativePlayer is not { ModalInput: false } player || _nativeDoorLoading || GetTree().Paused)
            throw new InvalidOperationException("Lockpicking cannot acquire ordinary gameplay input now.");
        if (Enumerable.Range(0, 4).Any(control => !InputMap.HasAction(RuntimeNativeInputControls.Action(control))))
            throw new NotSupportedException("Lockpicking requires the player's actual rebound control adapter.");
        var records = _nativePluginStack!;
        var world = _nativeReferences!;
        var driver = _nativeOpeningStageDriver!;
        if (world.Get(reference.FormKey).Script is not null)
            throw new NotSupportedException("Picking an attached script needs the source-selected post-unlock activation policy.");
        var events = _nativeReferenceEvents ?? throw new NotSupportedException("The reference event owner is absent.");
        if (!events.CanAdmitIndependentDefaultInteraction(reference.FormKey))
            throw new InvalidOperationException("The reference has no current independent interaction binding.");
        var state = world.RequirePickLock(reference.FormKey);
        if (state.FailedPickAttempts >= (driver.IgnoreBrokenLock() ? 2u : 1u))
            throw new InvalidOperationException("This lock is broken.");
        var admission = FalloutLockpickSession.Admission(records, state.Level, driver.PlayerSkillValue("Lockpick"));
        var pin = records.RuntimeFormKey(0x0a);
        _ = FalloutLockpickSession.RequirePlainPins(records, _nativeInventory, pin);
        var geometry = NativeLockpickMenu.ReadGeometry(GetViewport().GetVisibleRect().Size);
        var settings = FalloutLockpickSession.ManualSettings(records, admission.Class, driver.PlayerSkillValue("Lockpick"));
        void Sound(string name) => _nativeQuestScripts!.Scripts.Sounds.Play(records.RuntimeFormKey(0x14),
            FalloutDialogueTopic.Find(records, "SOUN", name).FormKey, systemSound: true);
        int BreakPin()
        {
            _ = FalloutLockpickSession.RequirePlainPins(records, _nativeInventory, pin);
            Sound("UILockpickingPickBreak");
            _nativeInventory.Remove(pin, 1, silent: true);
            return _nativeInventory.Item(pin)?.Count ?? 0;
        }
        // Prepare every deterministic geometry/setting refusal before capturing
        // player actions. CreateManual validates before its shared layout draw.
        var session = FalloutLockpickSession.CreateManual(
            () => FalloutLockpickSession.ForceChance(records, admission.Class, driver.PlayerSkillValue("Lockpick")),
            FalloutSharedUnsignedRandom.Process.NextBounded,
            () => world.CompletePickLock(reference.FormKey, state, () => driver.RewardExperience(admission.Reward)),
            () => world.FailForceLock(reference.FormKey, state),
            geometry.Width, geometry.Position, settings.Length, settings.Concentric, settings.Health, settings.BreakSeconds,
            BreakPin, () => Sound("UILockpickingPickMovement"));
        var tree = GetTree();
        var wasPaused = tree.Paused;
        var mouse = Input.MouseMode;
        var release = player.AcquireModalInput();
        var layer = new CanvasLayer { Name = "ForceLockLayer", Layer = 110, ProcessMode = ProcessModeEnum.Always };
        var released = false;
        void Close(bool success)
        {
            if (released) return;
            released = true;
            _nativeLockpickLayer = null;
            try { if (GodotObject.IsInstanceValid(layer)) layer.QueueFree(); }
            finally
            {
                try { if (GodotObject.IsInstanceValid(tree)) tree.Paused = wasPaused; Input.MouseMode = mouse; }
                finally { release(); }
            }
            if (success)
                events.DefaultActivate(reference.FormKey, records.RuntimeFormKey(0x14));
        }
        try
        {
            var menu = new NativeLockpickMenu(session, Close,
                error => GD.PushError($"OPENNV_LOCKPICK_FORCE_FAIL {error.Message}"))
            { MouseScale = geometry.MouseScale };
            _nativeLockpickLayer = layer;
            AddChild(layer); layer.AddChild(menu);
            GetTree().Paused = true; Input.MouseMode = Input.MouseModeEnum.Captured;
            GD.Print($"OPENNV_LOCKPICK_OPEN reference={reference.FormKey} chance={admission.Chance} manual=mouse-rebound-controls presentation=first-party");
        }
        catch { Close(false); throw; }
    }
}
