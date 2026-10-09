using Godot;
using OpenNV.Runtime.Presentation.Ui;

// Authored source-independent triangles exercise the same native adapter used
// by both owned rendered menus. This is geometry/input evidence, not pixels.
public partial class NativeMenuGeometryProjectionAudit : Node
{
    public override async void _Ready()
    {
        var exit = 0;
        try
        {
            GD.Print($"OPENNV_NATIVE_MENU_PROJECTION_ASSEMBLY mvid={typeof(NativeMenuGeometryProjectionAudit).Assembly.ManifestModule.ModuleVersionId}");
            await Exercise();
            GD.Print("OPENNV_NATIVE_MENU_GEOMETRY_PROJECTION_PASS actualCamera=true actualTexture=true offscreenRefused=true boundOnlyRefused=true partialClip=true nearFar=true reflectedCull=true displayedTransform=true callerCanvas=true parentClip=true selectedDepth=true sourceUnchanged=true detachedRefused=true recording=false pixels=unverified ownedMenuFraming=unverified");
        }
        catch (Exception error) { exit = 1; GD.PushError(error.ToString()); }
        GetTree().Quit(exit);
    }

    private async Task Exercise()
    {
        // The headless display server keeps its root at 64x64 even with a
        // command-line resolution. Own the authored canvas extent explicitly.
        var canvas = new SubViewport { Name = "AuthoredCanvasExtent", Size = new(1280, 720) };
        var panel = new Control { Name = "AuthoredClipPanel", Size = new(900, 600), ClipContents = true };
        var owner = new Control { Name = "AuthoredInputOwner", Position = new(31, 27), Size = new(600, 400), Rotation = .11f };
        var view = new SubViewport
        {
            Name = "AuthoredPixelExtent",
            Size = new(640, 360),
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        var camera = new Camera3D { Name = "ActualCamera", Current = true, Near = 1, Far = 10, Fov = 90 };
        var resources = new List<ArrayMesh>();
        using var material = new StandardMaterial3D { CullMode = BaseMaterial3D.CullModeEnum.Back };
        ViewportTexture? texture = null;
        try
        {
            AddChild(canvas); canvas.AddChild(panel); panel.AddChild(owner); owner.AddChild(view); view.AddChild(camera);
            texture = view.GetTexture();
            var pixels = new TextureRect
            {
                Name = "ActualDisplayedTexture",
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Texture = texture,
                Position = new(10.5f, 12.25f),
                Size = new(509.5f, 286.59375f)
            };
            owner.AddChild(pixels);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Apply the authored extent after the real Control minimum-size
            // publication; setting a texture can queue that recalculation.
            pixels.Size = new(509.5f, 286.59375f);
            Check(pixels.Size == new Vector2(509.5f, 286.59375f), "The authored fractional texture extent was not published.");
            using var adapter = new NativeMenuGeometryProjection(owner, pixels, view, camera);
            MeshInstance3D Triangle(string name, Vector2 a, Vector2 b, Vector2 c, float depth = 2,
                bool indexed = true, float? firstDepth = null)
            {
                Vector3[] vertices = [camera.ProjectPosition(a, firstDepth ?? depth), camera.ProjectPosition(b, depth), camera.ProjectPosition(c, depth)];
                var mesh = new ArrayMesh(); resources.Add(mesh);
                using var arrays = new Godot.Collections.Array();
                if (arrays.Resize((int)Mesh.ArrayType.Max) != Error.Ok) throw new InvalidOperationException("Authored triangle arrays could not be allocated.");
                using var vertexValue = Variant.From(vertices);
                arrays[(int)Mesh.ArrayType.Vertex] = vertexValue;
                if (indexed)
                {
                    using var indexValue = Variant.From(new[] { 0, 1, 2 });
                    arrays[(int)Mesh.ArrayType.Index] = indexValue;
                }
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                mesh.SurfaceSetMaterial(0, material);
                var instance = new MeshInstance3D { Name = name, Mesh = mesh };
                view.AddChild(instance); return instance;
            }
            static KeyValuePair<string, MeshInstance3D>[] Select(params MeshInstance3D[] targets) =>
                targets.Select(target => new KeyValuePair<string, MeshInstance3D>(target.Name.ToString(), target)).ToArray();
            var front = Triangle("front", new(100, 80), new(300, 80), new(200, 250));
            var original = front.Transform;
            var selected = Select(front);
            var candidate = adapter.Survey(selected).Single();
            Check(candidate.InputPoint is { } witness && adapter.Pick(witness, selected) == "front" && candidate.InFrame,
                "The actual native displayed triangle has no identical picker witness: " + System.Text.Json.JsonSerializer.Serialize(candidate.Observation) +
                $" viewport={owner.GetViewport().GetVisibleRect()} panel={panel.Size} owner={owner.Size} displayed={pixels.Size}");
            var sourceInterior = new Vector2(200, (80 + 80 + 250) / 3f);
            var displayedInterior = pixels.Position + sourceInterior * pixels.Size / new Vector2(view.Size.X, view.Size.Y);
            Check(adapter.Pick(displayedInterior, selected) == "front" &&
                adapter.Pick(pixels.Position + new Vector2(-20, 100), selected) is null,
                "Actual fractional display/offset used direct render-pixel input or clamped an outside point.");
            var callerPoint = panel.GetGlobalTransformWithCanvas().AffineInverse() *
                (owner.GetGlobalTransformWithCanvas() * displayedInterior);
            var remapped = NativeMenuGeometryProjection.MapInput(panel, owner, callerPoint);
            Check(remapped.DistanceTo(displayedInterior) < .001f && adapter.Pick(remapped, selected) == "front",
                "The actual caller/surface canvas transforms lost local pointer ownership.");
            var outside = Triangle("outside", new(100, 480), new(300, 480), new(200, 500));
            candidate = adapter.Survey(Select(outside)).Single();
            Check(!candidate.InFrame && candidate.InputPoint is null && candidate.Bounds is null &&
                candidate.SourceCenter is { } raw && raw.Y > pixels.Position.Y + pixels.Size.Y,
                "An in-front off-screen source center was hidden or fabricated into an input target.");
            var boundOnly = Triangle("boundOnly", new(-100, 10), new(10, -100), new(-100, -100));
            Check(!adapter.Survey(Select(boundOnly)).Single().InFrame,
                "A bound-only viewport overlap was accepted without an actual clipped triangle.");
            var partial = Triangle("partial", new(-40, 80), new(140, 80), new(100, 200), indexed: false);
            candidate = adapter.Survey(Select(partial)).Single();
            Check(candidate.InputPoint is { } part && adapter.Pick(part, Select(partial)) == "partial" && candidate.ProjectedTriangles > 0,
                "A partially clipped nonindexed source triangle lost its native input extent.");
            var near = Triangle("near", new(100, 80), new(300, 80), new(200, 250), firstDepth: .5f);
            var far = Triangle("far", new(100, 80), new(300, 80), new(200, 250), firstDepth: 12);
            Check(adapter.Survey(Select(near)).Single().InputPoint is not null && adapter.Survey(Select(far)).Single().InputPoint is not null,
                "Actual native near/far crossings lost their surviving clipped triangles.");
            var farther = Triangle("farther", new(100, 80), new(300, 80), new(200, 250), 4);
            var depth = adapter.Survey(Select(farther, front));
            Check(depth.Single(value => value.Geometry == "front").InputPoint is not null &&
                depth.Single(value => value.Geometry == "farther").InputPoint is null && adapter.Pick(displayedInterior, Select(farther, front)) == "front",
                "Source enumeration order replaced the actual selected triangle ray depth.");
            front.Scale = new(-1, 1, 1);
            Check(adapter.Survey(selected).Single().InputPoint is not null, "Native reflected instance culling lost the source front face.");
            front.Transform = original;
            var originalCamera = camera.Transform;
            camera.Scale = new(-1, 1, 1);
            Check(camera.GetCameraTransform().Basis.Determinant() < 0 && adapter.Survey(selected).Single().InputPoint is not null,
                "The actual reflected camera culling variant was not owned.");
            camera.Transform = originalCamera;
            panel.Size = new(64, 64);
            Check(adapter.Survey(selected).Single().InputPoint is null && adapter.Pick(displayedInterior, selected) is null,
                "A target hidden by an actual ancestor Control clip retained a click point.");
            panel.Size = new(900, 600);
            material.Grow = true;
            Reject<NotSupportedException>(() => adapter.Survey(selected));
            material.Grow = false;
            var originalMask = camera.CullMask;
            camera.CullMask = 0;
            candidate = adapter.Survey(selected).Single();
            Check(!candidate.InFrame && candidate.Disposition == "camera-layer-excluded" && candidate.InputPoint is null,
                "A target excluded by the actual camera layer mask earned an input witness.");
            camera.CullMask = originalMask;
            panel.RemoveChild(owner);
            Reject<InvalidOperationException>(() => adapter.Survey(selected));
            panel.AddChild(owner);
            Check(front.Transform == original, $"Projection changed the source placement: {front.Transform} expected {original}.");
            Check(camera.Transform == originalCamera, $"Projection changed the restored camera: {camera.Transform} expected {originalCamera}.");
            Check(view.Size == new Vector2I(640, 360), $"Projection changed the actual pixel extent: {view.Size}.");
            Check(pixels.Size == new Vector2(509.5f, 286.59375f), $"Projection changed the displayed extent: {pixels.Size}.");
            candidate = adapter.Survey(selected).Single();
            Check(candidate.InputPoint is not null,
                "The republished source triangle lost its input witness: " + System.Text.Json.JsonSerializer.Serialize(candidate.Observation));
        }
        finally
        {
            if (owner.GetParent() is null) owner.Free();
            panel.Free();
            canvas.Free();
            foreach (var resource in resources) resource.Dispose();
            texture?.Dispose();
        }
        Check(!GodotObject.IsInstanceValid(canvas) && !GodotObject.IsInstanceValid(panel) && !GodotObject.IsInstanceValid(owner) &&
            !GodotObject.IsInstanceValid(view) && !GodotObject.IsInstanceValid(camera), "Owned projection fixture nodes were not retired.");
    }

    private static void Check(bool value, string failure) { if (!value) throw new InvalidOperationException(failure); }
    private static void Reject<T>(Action operation) where T : Exception
    {
        try { operation(); } catch (Exception error) when (error is T) { return; }
        throw new InvalidOperationException($"Missing projection ownership did not refuse with {typeof(T).Name}.");
    }
}
