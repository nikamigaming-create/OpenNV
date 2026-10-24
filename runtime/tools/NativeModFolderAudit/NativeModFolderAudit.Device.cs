using Godot;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeModFolderAudit
{
    private async Task CheckLauncherDeviceInput(NativeGodotLauncher launcher, string output)
    {
        DeviceCheck(launcher.InstrumentPresentationConstructed, launcher.DevicePresentationStatus);
        var camera = (Camera3D)launcher.FindChild("DeviceCamera", true, false);
        var screen = (MeshInstance3D)launcher.FindChild("CRT_Screen", true, false);
        var canvas = (SubViewport)launcher.FindChild("DedicatedInstrumentCanvas", true, false);
        var readout = (Control)launcher.FindChild("InstrumentReadout", true, false);
        var search = (LineEdit)launcher.FindChild("LibrarySearch", true, false);
        var interfaceRoot = (Control)launcher.FindChild("LauncherInterface", true, false);
        DeviceCheck(!interfaceRoot.Visible && interfaceRoot.GetParent() == launcher,
            "Accessible library is not separate from the instrument.");
        DeviceCheck(!canvas.FindChildren("*", "BaseButton", true, false).Any() &&
            !canvas.FindChildren("*", "LineEdit", true, false).Any(), "The CRT still contains browser controls.");

        Vector2 ScreenPoint(Vector2 uv) => camera.UnprojectPosition(DeviceScreenPosition(screen, uv));
        Vector2 ControlPoint(string name)
        {
            var control = (Node3D)(launcher.FindChild(name, true, false) ??
                (name == "CONTROL_Range_pivotY" ? launcher.FindChild("CONTROL_Brightness_pivotY", true, false) : null) ??
                throw new InvalidDataException("Physical control is absent: " + name));
            var face = control.FindChildren("*", "MeshInstance3D", true, false).Prepend(control).OfType<MeshInstance3D>()
                .Where(mesh => mesh.IsVisibleInTree()).SelectMany(DeviceFaceCenters)
                .OrderBy(point => point.DistanceSquaredTo(camera.GlobalPosition)).First();
            return camera.UnprojectPosition(face);
        }
        var bus = AudioServer.GetBusIndex("Master");
        DeviceCheck(bus >= 0, "Master bus is missing.");
        var initialMute = AudioServer.IsBusMute(bus);
        try
        {
            foreach (var uv in new[] { new Vector2(.06f, .06f), new Vector2(.94f, .06f), new Vector2(.06f, .94f), new Vector2(.94f, .94f), new Vector2(.5f, .5f) })
            {
                var point = ScreenPoint(uv);
                var picked = launcher.DeviceScreenUv(point);
                DeviceCheck(picked is { } actualUv && actualUv.DistanceTo(uv) < .005f,
                    "Native CRT ray/UV alignment differs or is occluded at " + uv + ": " + picked);
                await DeviceClick(point);
                DeviceCheck(!interfaceRoot.Visible && !search.HasFocus(), "CRT output click activated the separate library.");
            }

            // The accessible library remains functional, and its filter cannot
            // change the hardware selector's independent set of game detents.
            DeviceKey(Key.F2, true); DeviceKey(Key.F2, false);
            await DeviceFrames();
            DeviceCheck(interfaceRoot.Visible && interfaceRoot.GetParent() == launcher, "F2 did not expose the separate accessible library.");
            search.GrabFocus(); await DeviceType("Fallout 1");
            DeviceCheck(search.Text == "Fallout 1", "Accessible search did not receive normal keyboard input.");
            SaveDeviceFrame(Path.Combine(output, "launcher-standard.png"));
            DeviceKey(Key.F2, true); DeviceKey(Key.F2, false);
            await DeviceFrames();
            DeviceCheck(!interfaceRoot.Visible && interfaceRoot.GetParent() == launcher, "F2 moved the library onto the CRT.");

            var tuning = (Node3D)launcher.FindChild("CONTROL_MainTuning_pivotY", true, false);
            var tuningBefore = tuning.Transform;
            var selectedBefore = launcher.InstrumentSelectedGame;
            var cancelPoint = ControlPoint("CONTROL_MainTuning_pivotY");
            DeviceMouse(cancelPoint, MouseButton.Left, true); await DeviceFrames();
            var outside = new Vector2(2, GetViewportRect().Size.Y - 2);
            DeviceMotion(outside); DeviceMouse(outside, MouseButton.Left, false); await DeviceFrames();
            DeviceCheck(tuning.Transform.IsEqualApprox(tuningBefore), "Cancelled selector press changed its detent.");
            DeviceCheck(launcher.InstrumentSelectedGame == selectedBefore, "Cancelled selector press changed the selected game.");
            DeviceMouse(ControlPoint("CONTROL_MainTuning_pivotY"), MouseButton.WheelUp, true); await DeviceFrames();
            var selectorMoved = !tuning.Transform.IsEqualApprox(tuningBefore);
            DeviceCheck(launcher.InstrumentSelectedGame != selectedBefore, "Physical selector did not select another game.");
            DeviceMouse(ControlPoint("CONTROL_MainTuning_pivotY"), MouseButton.WheelDown, true); await DeviceFrames();
            DeviceCheck(tuning.Transform.IsEqualApprox(tuningBefore), "Physical selector did not return to its original game.");
            DeviceCheck(launcher.InstrumentSelectedGame == selectedBefore, "Selector did not restore its selected game.");
            var games = new HashSet<string>();
            for (var detent = 0; detent < 4; detent++)
            {
                games.Add(launcher.InstrumentSelectedGame);
                DeviceMouse(ControlPoint("CONTROL_MainTuning_pivotY"), MouseButton.WheelDown, true); await DeviceFrames();
            }
            DeviceCheck(games.SetEquals(["fallout1", "fallout2", "fallout3", "newvegas"]) && launcher.InstrumentSelectedGame == selectedBefore,
                "Hardware selector did not cycle all four actual game owners.");

            var brightness = readout.Modulate;
            DeviceMouse(ControlPoint("CONTROL_Range_pivotY"), MouseButton.WheelDown, true); await DeviceFrames();
            DeviceCheck(readout.Modulate.R > brightness.R, "Physical brightness dial did not change actual readout modulation.");
            DeviceMouse(ControlPoint("CONTROL_Range_pivotY"), MouseButton.WheelUp, true); await DeviceFrames();
            DeviceCheck(readout.Modulate.IsEqualApprox(brightness), "Brightness did not return to its original value.");
            await DeviceClick(ControlPoint("CONTROL_Toggle_0_pivotX"));
            DeviceCheck(AudioServer.IsBusMute(bus) != initialMute, "Audio switch did not control the actual Master bus.");
            await DeviceClick(ControlPoint("CONTROL_Toggle_0_pivotX"));
            var cameraBefore = camera.GlobalTransform;
            await DeviceClick(ControlPoint("CONTROL_Toggle_1_pivotX"));
            DeviceCheck(!camera.GlobalTransform.IsEqualApprox(cameraBefore), "Read-view switch did not magnify the camera.");
            SaveDeviceFrame(Path.Combine(output, "launcher-read.png"));
            DeviceKey(Key.F3, true); DeviceKey(Key.F3, false); await DeviceFrames();
            DeviceCheck(camera.GlobalTransform.IsEqualApprox(cameraBefore), "Read view did not restore the device camera.");

            await DeviceClick(ControlPoint("CONTROL_FunctionKey_1"));
            DeviceCheck(!interfaceRoot.Visible, "CONFIG exposed the regular browser on the instrument.");
            await DeviceClick(ControlPoint("CONTROL_FunctionKey_2"));
            DeviceKey(Key.F4, true); DeviceKey(Key.F4, false); await DeviceFrames();
            DeviceCheck(launcher.FindChildren("*", "FileDialog", true, false).OfType<FileDialog>().Any(dialog => dialog.Visible),
                "Instrument source picker did not open the actual installation dialog.");
            DeviceKey(Key.Escape, true); DeviceKey(Key.Escape, false); await DeviceFrames();
            DeviceCheck(!launcher.FindChildren("*", "FileDialog", true, false).OfType<FileDialog>().Any(dialog => dialog.Visible),
                "Installation dialog did not retain normal cancellation ownership.");

            NativeGodotLauncherLaunchRequest? launch = null;
            launcher.LaunchRequested += request => launch = request;
            await DeviceClick(ControlPoint("CONTROL_FunctionKey_0"));
            DeviceCheck(launch?.CampaignId == "newvegas", "Physical LOAD did not request the selected game despite the accessible filter.");
            File.WriteAllText(Path.Combine(output, "device-input.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                presentation = "dedicated-instrument",
                screenOutputPoints = 5,
                separateAccessibleLibrary = true,
                independentSelector = true,
                brightness = true,
                masterAudio = true,
                readCamera = true,
                cancellation = true,
                sourcePicker = true,
                launch = launch?.CampaignId,
                gameplayLaunched = false,
                selectorPoseValidated = selectorMoved,
                selectorStatus = launcher.DeviceSelectorContractStatus,
                powerExitRequested = false
            }));
            // Finish independent controls before reporting a held source-art
            // contract. A semantic cycle is not a working marked detent.
            DeviceCheck(selectorMoved, launcher.DeviceSelectorContractStatus);
            GD.Print("OPENNV_NATIVE_LAUNCHER_INSTRUMENT_CONTROLS_READY outputPoints=5 selector=true brightness=true audio=true readView=true cancellation=true F2=true sourcePicker=true launchRequest=true gameplay=false");
            // Only the physical key may finish this run. If it fails to quit,
            // fail before RenderLauncherPreview can return to its normal exit.
            var powerPoint = ControlPoint("CONTROL_FunctionKey_3");
            DeviceMotion(powerPoint); DeviceMouse(powerPoint, MouseButton.Left, true);
            await DeviceFrames();
            DeviceMouse(powerPoint, MouseButton.Left, false);
            File.WriteAllText(Path.Combine(output, "device-power.json"), System.Text.Json.JsonSerializer.Serialize(new
            { requested = true, source = "physical-key", completion = "requires native process exit0" }));
            GD.Print("OPENNV_NATIVE_LAUNCHER_POWER_EXIT_REQUESTED source=physical-key");
            await Task.Delay(1500);
            throw new InvalidOperationException("Physical POWER did not quit the native launcher.");
        }
        finally { AudioServer.SetBusMute(bus, initialMute); }
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
