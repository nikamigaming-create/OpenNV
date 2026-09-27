using Godot;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private NativeCombatWheel? _nativeCombatWheel;
    private void OpenNativeCombatWheel(bool consumables)
    {
        if (_nativeCombatWheel is not null || _nativePlayer is not { ModalInput: false } player ||
            _nativeDoorLoading || GetTree().Paused || _nativeOpeningStageDriver!.Vitals.HitPoints <= 0) return;
        var paused = GetTree().Paused;
        var mouse = Input.MouseMode;
        var layer = new CanvasLayer { Name = "NativeCombatWheelLayer", Layer = 110, ProcessMode = ProcessModeEnum.Always };
        _nativeCombatWheel = new(_nativePluginStack!, _nativeInventory, consumables, _nativeXr is not null, form =>
        {
            if (_nativeInventory.Item(form) is not { Count: > 0 }) throw new InvalidOperationException("Item is no longer in your inventory.");
            if (consumables) _nativeOpeningStageDriver.UseAid(form);
            else _nativeInventory.Equip(_nativePluginStack!, form);
        }, () =>
        {
            _nativeCombatWheel = null; layer.QueueFree();
            GetTree().Paused = paused; player.SetModalInput(false); Input.MouseMode = mouse;
        });
        player.SetModalInput(true);
        AddChild(layer); layer.AddChild(_nativeCombatWheel);
        GetTree().Paused = true;
        if (_nativeXr is null)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().WarpMouse(GetViewport().GetVisibleRect().Size * .5f);
        }
        GD.Print($"OPENNV_COMBAT_WHEEL_OPEN kind={(consumables ? "aid" : "weapon")} xr={_nativeXr is not null}");
    }
}
