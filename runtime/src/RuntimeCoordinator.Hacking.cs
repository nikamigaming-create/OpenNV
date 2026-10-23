using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private CanvasLayer? _nativeHackingLayer;

    private void OpenNativeHacking(FalloutPlacedReference reference)
    {
        if (_nativeHackingLayer is not null) return;
        if (_nativeXr is not null)
            throw new NotSupportedException("Terminal hacking needs its XR input surface.");
        if (_nativePlayer is not { ModalInput: false } player || _nativeDoorLoading || GetTree().Paused)
            throw new InvalidOperationException("Hacking cannot acquire gameplay input now.");
        var driver = _nativeOpeningStageDriver!;
        var session = driver.BeginTerminalHacking(reference.FormKey);
        var tree = GetTree(); var paused = tree.Paused; var mouse = Input.MouseMode;
        Action? release = null;
        CanvasLayer? layer = null;
        var closed = false;
        void Close(bool success, bool save = true)
        {
            if (closed) return;
            closed = true; _nativeHackingLayer = null;
            try { if (GodotObject.IsInstanceValid(layer)) layer!.QueueFree(); }
            finally
            {
                driver.EndTerminalHacking(reference.FormKey);
                try { if (GodotObject.IsInstanceValid(tree)) tree.Paused = paused; Input.MouseMode = mouse; }
                finally { release?.Invoke(); }
            }
            if (success) OpenNativeTerminal(reference);
            else if (save) SaveNativeInteraction();
        }
        void Fail(Exception error)
        {
            try { driver.FailTerminalHacking(reference.FormKey, error); }
            finally { Close(false); }
            GD.PushError($"OPENNV_NATIVE_HACKING_FAIL reference={reference.FormKey} {error.Message}");
        }
        try
        {
            release = player.AcquireModalInput();
            layer = new CanvasLayer { Name = "NativeHackingLayer", Layer = 110, ProcessMode = ProcessModeEnum.Always };
            // A stopped successful panel no longer emits Cancel on exit. The
            // layer therefore owns unconditional retirement, including teardown
            // during the delayed contents handoff.
            layer.TreeExiting += () => Close(false, save: false);
            var timer = new Godot.Timer { WaitTime = .025, ProcessMode = ProcessModeEnum.Always, IgnoreTimeScale = true };
            uint successAt = 0;
            timer.Timeout += () =>
            {
                if (closed) return;
                try
                {
                    if (unchecked(FalloutSourceMilliseconds.Read() - successAt) >= 3000) Close(true);
                }
                catch (Exception error) { Fail(error); }
            };
            var menu = new NativeHackingMenu(session, outcome =>
            {
                if (session.Stage is not (FalloutHackingStage.Succeeded or FalloutHackingStage.LockedOut)) return;
                // The source's success deadline is established before publishing
                // hacking, XP and statistic effects. The timer only polls it.
                successAt = FalloutSourceMilliseconds.Read();
                driver.ApplyTerminalHackingOutcome(reference.FormKey, session);
                if (session.Stage == FalloutHackingStage.Succeeded) timer.Start();
                else Close(false);
            }, () => Close(false, save: false), Fail);
            // Child exit can precede its parent's exit signal. Cancellation has
            // no persistent outcome, so neither path saves during retirement.
            menu.TreeExiting += () => Close(false, save: false);
            _nativeHackingLayer = layer;
            AddChild(layer); layer.AddChild(timer); layer.AddChild(menu);
            if (menu.Error is { } error) throw new InvalidOperationException(error);
            tree.Paused = true; Input.MouseMode = Input.MouseModeEnum.Visible;
            GD.Print($"OPENNV_NATIVE_HACKING_OPEN reference={reference.FormKey} input=mouse-keyboard source=owned-dictionary");
        }
        catch (Exception error)
        {
            // Presentation failure keeps any consumed outcome prefix and always
            // returns the player's input lease. It never reruns the transaction.
            Fail(error); throw;
        }
    }
}
