using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeRenderedMenuAudit
{
    private async Task RaceMenuDevices(string baseRoot, string mod, string root, string[] dependencies)
    {
        NativeOwnedRenderedDevice? device = null;
        NativeOwnedRenderedScreen? screen = null;
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            if (!source.TryRead("nvse/plugins/ttw_nvse.dll", null, out var plugin, out var pluginIdentity))
                throw new FileNotFoundException("Selected TTW plugin is absent.");
            var executable = Path.Combine(baseRoot, "FalloutNV.exe");
            var executableHash = SHA256.HashData(File.ReadAllBytes(executable)); var pluginHash = SHA256.HashData(plugin);
            var models = FalloutExecutableStringTable.ReadTtwRaceMenuDevices(executable, plugin);
            var settings = FalloutInstallationSettings.Read(source);
            var contract = FalloutNativeRaceSexResolver.Resolve(records);
            var creation = new FalloutNativeCharacterCreation(records, contract, contract.Initial, settings);
            var traces = new List<object>();
            byte[]? first = null, projector = null;
            foreach (var command in new[] { "ShowRaceMenu", "TTW_ShowGeneProjector", "ShowRaceMenu" })
            {
                Exception? failure = null; var activations = 0;
                var model = models.ModelFor(command);
                device = new(model, settings) { Size = Size }; AddChild(device);
                screen = new(device, records, settings, error => failure = error); AddChild(screen);
                void Select(bool female) { creation.ChangeIdentity(creation.Selection.RaceRuntimeFormId, female); activations++; }
                screen.Menu.SetPage(0, FalloutGameSettingStrings.Read(records, "sRSMSex"),
                    [new(FalloutGameSettingStrings.Read(records, "sMale"), !creation.Selection.Female, () => Select(false)),
                     new(FalloutGameSettingStrings.Read(records, "sFemale"), creation.Selection.Female, () => Select(true))]);
                for (var frame = 0; frame < 3; ++frame) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (failure is not null) throw failure;
                using var pixels = device.View.GetTexture().GetImage();
                if (pixels.IsEmpty()) throw new InvalidDataException("Race-menu model produced no native pixels.");
                var data = pixels.GetData();
                if (first is null) first = data;
                else if (projector is null) projector = data;
                else if (!first.AsSpan().SequenceEqual(data)) throw new InvalidDataException("Normal race menu did not restore its original device pixels.");
                var femaleLabel = FalloutGameSettingStrings.Read(records, "sFemale");
                var target = screen.Menu.GetChildren().OfType<NativeOwnedTileTarget>().Single(button => button.Text == femaleLabel);
                var rectangle = target.GetGlobalRect();
                var geometry = device.Geometry(device.ScreenName); var arrays = geometry.Mesh.SurfaceGetArrays(0);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(); var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                Vector2? pointer = null;
                for (var triangle = 0; triangle < indices.Length && pointer is null; triangle += 3)
                {
                    var a = indices[triangle]; var b = indices[triangle + 1]; var c = indices[triangle + 2];
                    for (var firstWeight = 1; firstWeight < 24 && pointer is null; ++firstWeight)
                        for (var secondWeight = 1; secondWeight < 24 - firstWeight && pointer is null; ++secondWeight)
                        {
                            var u = firstWeight / 24f; var v = secondWeight / 24f;
                            if (screen.CanvasPoint(uvs[a] * (1 - u - v) + uvs[b] * u + uvs[c] * v) is not { } canvas || !rectangle.HasPoint(canvas)) continue;
                            var point = device.Camera.UnprojectPosition(geometry.GlobalTransform * (vertices[a] * (1 - u - v) + vertices[b] * u + vertices[c] * v));
                            if (screen.SourcePoint(point) is { } mapped && rectangle.HasPoint(mapped)) pointer = point;
                        }
                }
                if (pointer is not { } click) throw new InvalidDataException("Race-menu source choice has no visible pointer projection.");
                foreach (var pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventMouseButton { Position = click, GlobalPosition = click, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
                if (!creation.Selection.Female || activations != 1) throw new InvalidDataException("Race-menu pointer did not edit the shared draft.");
                foreach (var key in new[] { Key.Up, Key.Enter })
                    foreach (var pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
                if (creation.Selection.Female || activations != 2 || failure is not null) throw new InvalidDataException("Race-menu keyboard did not edit the shared draft.");
                traces.Add(new { command, model, sourceHash = device.Source.Sha256, pointer = true, keyboard = true });
                screen.Free(); screen = null; device.Free(); device = null;
            }
            if (first is null || projector is null || first.AsSpan().SequenceEqual(projector))
                throw new InvalidDataException("Distinct source race-menu models produced indistinguishable native pixels.");
            if (!executableHash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(executable))) ||
                !source.TryRead("nvse/plugins/ttw_nvse.dll", null, out var afterPlugin, out _) || !pluginHash.AsSpan().SequenceEqual(SHA256.HashData(afterPlugin)))
                throw new InvalidDataException("Race-menu fixture changed source bytes.");
            GD.Print("OPENNV_NATIVE_RACE_MENU_DEVICES_PASS " + JsonSerializer.Serialize(new
            {
                models,
                pluginIdentity,
                traces,
                sourceReadonly = true,
                recording = false,
                retainedFrames = 0,
                boundary = "isolated-owned-device-XML-font-pointer-keyboard;portrait-campaign-retail-and-XR-unverified"
            }));
        }
        finally { screen?.Free(); device?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
