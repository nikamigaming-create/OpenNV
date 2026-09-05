using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeRenderedMenuAudit : Control
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length is < 2 or > 4 || args.Length >= 3 && args[2] is not ("--tiles" or "--screen" or "--portrait" or "--input") ||
                args.Length == 4 && args[2] != "--portrait")
                throw new ArgumentException("Owned data, private output and an admitted audit mode are required.");
            var root = args[0]; var output = args[1];
            if (!Path.IsPathFullyQualified(output) || Path.GetFullPath(output).StartsWith(
                Path.GetFullPath(ProjectSettings.GlobalizePath("res://../")), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Owned visual evidence must be outside the repository.");
            RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            SubViewport view;
            Exception? failure = null;
            if (args.Length == 3 && args[2] == "--tiles")
            {
                var menu = new NativeOwnedRaceSexMenu(records, error => failure = error);
                menu.SetCanvas(Size);
                menu.SetPage(0, FalloutGameSettingStrings.Read(records, "sRSMSex"),
                    [new(FalloutGameSettingStrings.Read(records, "sMale"), true, () => { }),
                     new(FalloutGameSettingStrings.Read(records, "sFemale"), false, () => { })]);
                var panel = menu.Panel;
                view = new SubViewport { Size = new((int)panel.Size.X, (int)panel.Size.Y), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                AddChild(view); view.AddChild(menu);
                menu.Position = -panel.Position;
            }
            else
            {
                var device = new NativeOwnedRenderedDevice("meshes/terminals/nv_reflectron_ui.nif", FalloutInstallationSettings.Read(source)) { Size = Size };
                AddChild(device); view = device.View;
                GD.Print($"OPENNV_RENDERED_DEVICE_LIGHT_RADIUS {device.Model.GetMeta("opennv_menu_light_radius")}");
                if (args.Length >= 3 && args[2] is "--screen" or "--portrait" or "--input")
                {
                    var screen = new NativeOwnedRenderedScreen(device, records, FalloutInstallationSettings.Read(source), error => failure = error);
                    AddChild(screen);
                    if (args[2] == "--portrait")
                    {
                        var contract = FalloutNativeRaceSexResolver.Resolve(records);
                        var appearance = FalloutNpcAppearanceResolver.Resolve(records, contract.Player) with { RuntimeFace = true };
                        var portrait = new NativeOwnedActorPreview(records, appearance, FalloutInstallationSettings.Read(source), (int)Size.X);
                        AddChild(portrait);
                        if (args.Length == 4)
                        {
                            if (!source.TryRead(args[3], null, out var poseBytes, out var poseOwner)) throw new FileNotFoundException(args[3]);
                            var pose = FalloutNifFile.Read(poseBytes);
                            var sequence = pose.Roots.Select(pose.ReadControllerSequence).Single();
                            portrait.Actor.PlayBaseSequence(pose, sequence, poseOwner);
                            portrait.UpdateProjection();
                            var skeletonBefore = Enumerable.Range(0, portrait.Actor.Skeleton.Node.GetBoneCount())
                                .Select(portrait.Actor.Skeleton.Node.GetBonePose).ToArray();
                            var started = Time.GetTicksMsec();
                            while (Time.GetTicksMsec() - started < 600)
                                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                            if (portrait.Actor.AnimationError is not null || portrait.Actor.BaseSourceSeconds <= sequence.StartTime ||
                                !skeletonBefore.Where((poseBefore, index) => poseBefore != portrait.Actor.Skeleton.Node.GetBonePose(index)).Any())
                                throw new InvalidOperationException("The complete source preview clip did not advance its actual bone poses.");
                            GD.Print($"OPENNV_PREVIEW_MOTION_PASS sourceSeconds={portrait.Actor.BaseSourceSeconds} completeClip=true actualBones=true parity=unverified");
                            GD.Print($"OPENNV_PREVIEW_ANIMATION source={poseOwner} state={System.Text.Json.JsonSerializer.Serialize(portrait.Actor.AnimationState)} parity=unverified");
                        }
                        screen.SetPortrait(portrait);
                        GD.Print($"OPENNV_PREVIEW_CANVAS target={screen.ContentView.Size} projection={portrait.View.GetCamera3D().GetCameraProjection()}");
                        GD.Print($"OPENNV_PREVIEW_BOUND height={portrait.Actor.SourceHeight} radius={portrait.GetMeta("opennv_preview_light_radius")} position={portrait.GetMeta("opennv_preview_translation")}");
                        var headParts = portrait.Actor.Parts.Where(part => part.Root.GetMeta("opennv_source_part").AsString() == "head");
                        var headVertices = new SortedDictionary<int, float[]>();
                        foreach (var mesh in headParts.SelectMany(part => part.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>()))
                        {
                            var map = mesh.GetMeta("opennv_nif_skin_vertex_map").AsInt32Array();
                            var vertices = mesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                            for (var index = 0; index < map.Length; index++)
                                headVertices[map[index]] = [vertices[index].X, -vertices[index].Z, vertices[index].Y];
                        }
                        GD.Print("OPENNV_PREVIEW_HEAD_VERTICES " + System.Text.Json.JsonSerializer.Serialize(new { count = headVertices.Count, first = headVertices.Take(16).ToArray() }));
                        File.WriteAllText(Path.ChangeExtension(output, ".head.json"), System.Text.Json.JsonSerializer.Serialize(headVertices));
                        var skeleton = portrait.Actor.Skeleton.Node;
                        var headPose = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(portrait.Actor.Skeleton.BoneIndex("Bip01 Head"));
                        GD.Print($"OPENNV_PREVIEW_HEAD_WORLD {headPose}");
                        static float[] SourceMatrix(Transform3D matrix)
                        {
                            Vector3 Native(Vector3 value) => new(value.X, -value.Z, value.Y);
                            var x = Native(matrix.Basis.X); var y = Native(-matrix.Basis.Z);
                            var z = Native(matrix.Basis.Y); var origin = Native(matrix.Origin);
                            return [x.X, y.X, z.X, origin.X, x.Y, y.Y, z.Y, origin.Y, x.Z, y.Z, z.Z, origin.Z];
                        }
                        foreach (var mesh in headParts.SelectMany(part => part.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>()))
                        {
                            var binds = Enumerable.Range(0, mesh.Skin.GetBindCount()).Select(index => new
                            {
                                name = mesh.Skin.GetBindName(index).ToString(),
                                matrix = SourceMatrix(skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(mesh.Skin.GetBindBone(index)) * mesh.Skin.GetBindPose(index)),
                                inverse = SourceMatrix(mesh.Skin.GetBindPose(index)),
                            }).ToArray();
                            GD.Print("OPENNV_PREVIEW_HEAD_SKIN " + System.Text.Json.JsonSerializer.Serialize(new { name = mesh.Name.ToString(), mesh = SourceMatrix(mesh.Transform), binds }));
                        }
                    }
                    var selectedSex = false; var activations = 0;
                    var femaleLabel = FalloutGameSettingStrings.Read(records, "sFemale");
                    screen.Menu.SetPage(0, FalloutGameSettingStrings.Read(records, "sRSMSex"),
                        [new(FalloutGameSettingStrings.Read(records, "sMale"), true, () => { selectedSex = false; activations++; }),
                         new(femaleLabel, false, () => { selectedSex = true; activations++; })]);
                    if (args[2] == "--input")
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var button = screen.Menu.GetChildren().OfType<NativeOwnedTileTarget>().Single(button => button.Text == femaleLabel);
                        var rectangle = button.GetGlobalRect();
                        GD.Print($"OPENNV_INPUT_TARGET rectangle={rectangle} panel={screen.Menu.Panel} sourceProbe={screen.CanvasPoint(new(0.8f, 0.5f))}");
                        var mesh = device.Geometry("Screen:0");
                        var arrays = mesh.Mesh.SurfaceGetArrays(0);
                        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                        Vector2? pointer = null; var candidates = 0; var visibleCandidates = 0;
                        for (var triangle = 0; triangle < indices.Length && pointer is null; triangle += 3)
                        {
                            var a = indices[triangle]; var b = indices[triangle + 1]; var c = indices[triangle + 2];
                            for (var first = 1; first < 24 && pointer is null; first++)
                                for (var second = 1; second < 24 - first && pointer is null; second++)
                                {
                                    var u = first / 24f; var v = second / 24f;
                                    var uv = uvs[a] * (1 - u - v) + uvs[b] * u + uvs[c] * v;
                                    if (screen.CanvasPoint(uv) is not { } canvas || !rectangle.HasPoint(canvas)) continue;
                                    candidates++;
                                    var world = mesh.GlobalTransform * (vertices[a] * (1 - u - v) + vertices[b] * u + vertices[c] * v);
                                    var point = device.Camera.UnprojectPosition(world);
                                    if (screen.SourcePoint(point) is { } mapped) { visibleCandidates++; if (rectangle.HasPoint(mapped)) pointer = point; }
                                    if (candidates == 1) GD.Print($"OPENNV_INPUT_OCCLUDER {device.GetMeta("opennv_pointer_surface", "no-hit")} point={point} source={canvas}");
                                }
                        }
                        if (pointer is not { } click) throw new InvalidOperationException($"Source button has no visible input projection: candidates={candidates}, visible={visibleCandidates}.");
                        foreach (var pressed in new[] { true, false })
                            GetViewport().PushInput(new InputEventMouseButton { Position = click, GlobalPosition = click, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
                        if (!selectedSex || activations != 1) throw new InvalidOperationException("Source-projected pointer did not activate the original button.");
                        foreach (var key in new[] { Key.Up, Key.Enter })
                            foreach (var pressed in new[] { true, false })
                                GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
                        if (selectedSex || activations != 2) throw new InvalidOperationException("Native keyboard focus did not activate the preceding source choice.");
                        GD.Print("OPENNV_RENDERED_MENU_INPUT_PASS modelTriangles=true ownedProgramCoordinates=true nativePointer=true nativeKeyboard=true parity=unverified");
                    }
                }
            }
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (failure is not null) throw failure;
            using var frame = view.GetTexture().GetImage();
            if (frame.IsEmpty()) throw new InvalidOperationException("Rendered device produced no native pixels.");
            if (frame.SavePng(output) != Error.Ok) throw new IOException("Private device frame could not be retained.");
            GD.Print($"OPENNV_RENDERED_DEVICE_FRAME source={(args.Length >= 3 ? "owned-menu-xml" : "direct-nif")} width={frame.GetWidth()} height={frame.GetHeight()} output={output} parity=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
