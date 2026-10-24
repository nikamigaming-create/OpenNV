using Godot;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeModFolderAudit
{
    private async Task CheckLauncherDeviceInput(NativeGodotLauncher launcher, string output)
    {
        DeviceCheck(launcher.DevicePresentationConstructed, launcher.DevicePresentationStatus);
        var camera = (Camera3D)launcher.FindChild("DeviceCamera", true, false);
        var screen = (MeshInstance3D)launcher.FindChild("CRT_Screen", true, false);
        var canvas = (SubViewport)launcher.FindChild("LiveLauncherCanvas", true, false);
        var search = (LineEdit)launcher.FindChild("LibrarySearch", true, false);
        var interfaceRoot = (Control)launcher.FindChild("LauncherInterface", true, false);
        var first = (Button)launcher.FindChild("Campaign_fallout1", true, false);
        var second = (Button)launcher.FindChild("Campaign_fallout2", true, false);
        var vegas = (Button)launcher.FindChild("Campaign_newvegas", true, false);
        var setup = (Button)launcher.FindChild("ToggleFolderSetup", true, false);
        var quit = (Button)launcher.FindChild("QuitLauncher", true, false);
        var firstPresses = 0; var secondPresses = 0; var vegasPresses = 0; var quitPresses = 0;
        first.Pressed += () => firstPresses++;
        second.Pressed += () => secondPresses++;
        vegas.Pressed += () => vegasPresses++;
        quit.Pressed += () => quitPresses++;

        Vector2 ScreenPoint(Vector2 uv) => camera.UnprojectPosition(DeviceScreenPosition(screen, uv));
        Vector2 ControlPoint(string name)
        {
            var control = (Node3D)launcher.FindChild(name, true, false);
            var face = control.FindChildren("*", "MeshInstance3D", true, false).Prepend(control).OfType<MeshInstance3D>()
                .Where(mesh => mesh.IsVisibleInTree())
                .SelectMany(DeviceFaceCenters)
                .OrderBy(point => point.DistanceSquaredTo(camera.GlobalPosition)).First();
            return camera.UnprojectPosition(face);
        }
        Vector2 UiPoint(Control control) => ScreenPoint(control.GetGlobalRect().GetCenter() / new Vector2(canvas.Size.X, canvas.Size.Y));
        foreach (var uv in new[] { new Vector2(.06f, .06f), new Vector2(.94f, .06f), new Vector2(.06f, .94f), new Vector2(.94f, .94f), new Vector2(.5f, .5f) })
        {
            DeviceMotion(ScreenPoint(uv));
            await DeviceFrames();
            DeviceCheck(canvas.GetMousePosition().DistanceTo(uv * new Vector2(canvas.Size.X, canvas.Size.Y)) < 4,
                "CRT pointer projection differs at " + uv + ": " + canvas.GetMousePosition());
        }

        // A press dragged outside the device must not activate the former target.
        var cancelPoint = UiPoint(second);
        DeviceMouse(cancelPoint, MouseButton.Left, true);
        await DeviceFrames();
        DeviceMotion(new Vector2(2, GetViewportRect().Size.Y - 2));
        DeviceMouse(new Vector2(2, GetViewportRect().Size.Y - 2), MouseButton.Left, false);
        await DeviceFrames();
        DeviceCheck(secondPresses == 0, "Pointer cancellation activated a campaign button.");

        await DeviceClick(UiPoint(search));
        await DeviceType("Fallout 1");
        DeviceCheck(search.Text == "Fallout 1", "The live CRT did not receive keyboard text.");
        DeviceMouse(ControlPoint("CONTROL_MainTuning_pivotY"), MouseButton.WheelDown, true);
        await DeviceFrames();
        DeviceCheck(firstPresses == 1 && vegasPresses == 0, "Tuning did not respect the active library filter.");
        await DeviceClick(UiPoint(search));
        await DeviceType("New Vegas");
        DeviceMouse(ControlPoint("CONTROL_Range_pivotY"), MouseButton.WheelDown, true);
        await DeviceFrames();
        DeviceCheck(vegasPresses == 1, $"Range control did not select the visible game: search={search.Text}, visible={vegas.IsVisibleInTree()}, presses={vegasPresses}.");
        await DeviceClick(UiPoint(search));
        await DeviceType("");

        await DeviceClick(ControlPoint("CONTROL_FunctionKey_1"));
        DeviceCheck(setup.Text.StartsWith("Back to library", StringComparison.Ordinal), "Physical settings key did not open settings.");
        await DeviceClick(ControlPoint("CONTROL_FunctionKey_2"));
        DeviceCheck(setup.Text.StartsWith("Folder settings", StringComparison.Ordinal), "Physical back key did not return to the library.");
        foreach (var name in new[] { "CONTROL_Toggle_0_pivotX", "CONTROL_Toggle_1_pivotX" })
        {
            await DeviceClick(ControlPoint(name));
            DeviceCheck(((Node)launcher.FindChild(name, true, false)).GetMeta("launcher_toggle_on", false).AsBool(), "Physical toggle did not move: " + name);
        }

        DeviceKey(Key.F2, true); DeviceKey(Key.F2, false);
        await DeviceFrames();
        DeviceCheck(interfaceRoot.GetParent() == launcher, "F2 did not restore the full-size accessible interface.");
        SaveDeviceFrame(Path.Combine(output, "launcher-standard.png"));
        DeviceKey(Key.F2, true); DeviceKey(Key.F2, false);
        await DeviceFrames();
        DeviceCheck(interfaceRoot.GetParent() == canvas, "F2 did not restore the same live CRT interface.");
        var read = launcher.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == "Read screen");
        await DeviceClick(read.GetGlobalRect().GetCenter());
        await DeviceFrames();
        SaveDeviceFrame(Path.Combine(output, "launcher-read.png"));
        await DeviceClick(read.GetGlobalRect().GetCenter());

        NativeGodotLauncherLaunchRequest? launch = null;
        launcher.LaunchRequested += request => launch = request;
        await DeviceClick(ControlPoint("CONTROL_FunctionKey_0"));
        DeviceCheck(launch?.CampaignId == "newvegas", "Physical launch key did not produce the selected game's ordinary launch request.");
        // This preview subscribes to the normal request; it never starts a game.
        quit.Pressed += () =>
        {
            DeviceCheck(quitPresses == 1, "Physical quit key did not reach the real quit button.");
            File.WriteAllText(Path.Combine(output, "device-input.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    screenPoints = 5,
                    filteredTuning = true,
                    range = true,
                    cancellation = true,
                    physicalFunctionKeys = 4,
                    toggles = 2,
                    accessibleView = true,
                    launch = launch?.CampaignId,
                    gameplayLaunched = false
                }));
            GD.Print("OPENNV_NATIVE_LAUNCHER_DEVICE_INPUT_PASS screenPoints=5 filteredTuning=true cancellation=true physicalKeys=4 toggles=2 F2=true launchRequest=true gameplay=false");
        };
        var quitPoint = ControlPoint("CONTROL_FunctionKey_3");
        DeviceMouse(quitPoint, MouseButton.Left, true);
        await DeviceFrames();
        DeviceMouse(quitPoint, MouseButton.Left, false);
        await DeviceFrames();
        DeviceCheck(quitPresses == 1, "Physical quit key did not reach the real quit button.");
    }

    private static void DeviceCheck(bool condition, string error)
    { if (!condition) throw new InvalidOperationException(error); }

    private async Task DeviceFrames()
    {
        for (var i = 0; i < 2; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    private async Task DeviceClick(Vector2 point)
    {
        DeviceMotion(point); DeviceMouse(point, MouseButton.Left, true);
        await DeviceFrames();
        DeviceMouse(point, MouseButton.Left, false);
        await DeviceFrames();
    }

    private async Task DeviceType(string text)
    {
        DeviceKey(Key.A, true, control: true); DeviceKey(Key.A, false, control: true);
        DeviceKey(Key.Backspace, true); DeviceKey(Key.Backspace, false);
        foreach (var character in text)
        {
            var key = (Key)char.ToUpperInvariant(character);
            DeviceKey(key, true, character); DeviceKey(key, false);
        }
        await DeviceFrames();
    }

    private static void DeviceKey(Key key, bool pressed, uint unicode = 0, bool control = false) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, Unicode = unicode, CtrlPressed = control });
    private static void DeviceMotion(Vector2 point) => Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
    private static void DeviceMouse(Vector2 point, MouseButton button, bool pressed) =>
        Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = button, Pressed = pressed });

    private void SaveDeviceFrame(string path)
    {
        using var image = GetViewport().GetTexture().GetImage();
        if (image.IsEmpty() || image.SavePng(path) != Error.Ok) throw new IOException("Missing native device frame: " + path);
    }

    private static Vector3 DeviceScreenPosition(MeshInstance3D screen, Vector2 uv)
    {
        var mesh = screen.Mesh ?? throw new InvalidDataException("The CRT has no mesh.");
        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            using var arrays = mesh.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (indices.Length == 0) indices = Enumerable.Range(0, vertices.Length).ToArray();
            for (var i = 0; i < indices.Length; i += 3)
            {
                var a = indices[i]; var b = indices[i + 1]; var c = indices[i + 2];
                var ab = uvs[b] - uvs[a]; var ac = uvs[c] - uvs[a]; var delta = uv - uvs[a];
                var determinant = ab.Cross(ac);
                if (MathF.Abs(determinant) < 1e-8f) continue;
                var v = delta.Cross(ac) / determinant; var w = ab.Cross(delta) / determinant;
                if (v < -1e-5f || w < -1e-5f || v + w > 1.00001f) continue;
                return screen.ToGlobal(vertices[a] * (1 - v - w) + vertices[b] * v + vertices[c] * w);
            }
        }
        throw new InvalidDataException("No actual CRT triangle at " + uv);
    }

    private static IEnumerable<Vector3> DeviceFaceCenters(MeshInstance3D node)
    {
        if (node.Mesh is not { } mesh) yield break;
        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            using var arrays = mesh.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (indices.Length == 0) indices = Enumerable.Range(0, vertices.Length).ToArray();
            for (var i = 0; i < indices.Length; i += 3)
                yield return node.ToGlobal((vertices[indices[i]] + vertices[indices[i + 1]] + vertices[indices[i + 2]]) / 3);
        }
    }
}
