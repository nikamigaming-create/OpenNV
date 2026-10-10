using Godot;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativePipBoyAudit
{
    private async Task ExerciseNativePointerCancellation()
    {
        GD.Print($"OPENNV_PIPBOY_INPUT_ASSEMBLY location={typeof(NativePipBoyAudit).Assembly.Location} mvid={typeof(NativePipBoyAudit).Module.ModuleVersionId}");
        SubViewport? canvas = null;
        try
        {
            canvas = new SubViewport
            {
                Name = "AuthoredMenuInputCanvas",
                Size = new(128, 128),
                Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            AddChild(canvas);
            var button = new Button
            {
                Name = "AuthoredSourceAction",
                Position = new(16, 16),
                Size = new(64, 48),
                ActionMode = BaseButton.ActionModeEnum.Release
            };
            canvas.AddChild(button);
            var actions = 0;
            var downs = 0;
            var ups = 0;
            var capturedMotions = new List<Vector2>();
            button.Pressed += () => actions++;
            button.ButtonDown += () => downs++;
            button.ButtonUp += () => ups++;
            button.GuiInput += input =>
            {
                if (input is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
                    capturedMotions.Add(motion.Relative);
            };
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var inside = button.Position + button.Size / 2;

            NativeMenuPointerInput.Motion(canvas, inside, Vector2.Zero, false);
            NativeMenuPointerInput.LeftButton(canvas, inside, true);
            if (downs != 1 || ups != 0 || button.GetDrawMode() is not (BaseButton.DrawMode.Pressed or BaseButton.DrawMode.HoverPressed))
                throw new InvalidOperationException("Authored native button never captured its ordinary press.");
            NativeMenuPointerInput.LeftButton(canvas, inside, false);
            if (actions != 1 || downs != 1 || ups != 1)
                throw new InvalidOperationException("Actual native release did not commit exactly one authored action.");

            NativeMenuPointerInput.Motion(canvas, inside, Vector2.Zero, false);
            NativeMenuPointerInput.LeftButton(canvas, inside, true);
            if (downs != 2 || ups != 1) throw new InvalidOperationException("Second native pointer capture was not entered.");
            NativeMenuPointerInput.CancelLeft(canvas);
            if (actions != 1 || downs != 2 || ups != 2 || capturedMotions.Count != 1 || capturedMotions[0] != Vector2.Zero)
                throw new InvalidOperationException("Pointer cancellation committed an action, retained capture, or manufactured a map drag.");

            NativeMenuPointerInput.Motion(canvas, inside, Vector2.Zero, false);
            NativeMenuPointerInput.LeftButton(canvas, inside, true);
            NativeMenuPointerInput.LeftButton(canvas, inside, false);
            if (actions != 2 || downs != 3 || ups != 3)
                throw new InvalidOperationException("Cancelled native capture poisoned a later actual release.");

            GD.Print("OPENNV_NATIVE_PIPBOY_INPUT_LIFETIME_PASS nativeButton=true ordinaryRelease=true cancelledAction=false cancelledDrag=false freshCapture=true physicalXrAcceptance=false recording=off");
        }
        finally
        {
            if (canvas is not null && GodotObject.IsInstanceValid(canvas)) canvas.Free();
            canvas?.Dispose();
        }
    }
}
