using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicInventoryModelSlot(string Key, string Name, Rect2 Bounds, Func<Node3D?> Build);

/// <summary>Transient, isolated views of the same source-bound models used in the world.</summary>
internal sealed partial class ClassicInventoryModels : Control
{
    private sealed record Render(SubViewport? View, Node3D? Model, Camera3D? Camera, string? Failure)
    {
        internal int Frames { get; set; }
    }
    private readonly Dictionary<string, Render> _renders = [];
    private IReadOnlyList<ClassicInventoryModelSlot> _slots = [];
    private ClassicOwnedFont _font = null!;

    internal void Configure(ClassicOwnedFont font)
    {
        Name = "ClassicInventory3DModels"; _font = font;
        MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Linear;
        ProcessMode = ProcessModeEnum.Always;
    }

    internal void Display(IReadOnlyList<ClassicInventoryModelSlot> slots, bool enabled, float uiScale)
    {
        _slots = slots; Visible = enabled;
        var desired = slots.Select(slot => slot.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, render) in _renders.ToArray())
            if (!desired.Contains(key)) { render.View?.QueueFree(); _renders.Remove(key); }
        if (enabled)
        {
            foreach (var group in slots.GroupBy(slot => slot.Key))
            {
                var slot = group.First();
                if (!_renders.TryGetValue(slot.Key, out var render))
                { render = Create(slot); _renders.Add(slot.Key, render); }
                if (render.View is not { } view) continue;
                var bounds = group.MaxBy(row => row.Bounds.Size.X * row.Bounds.Size.Y)!.Bounds.Size;
                var size = new Vector2I(Math.Clamp((int)Math.Ceiling(bounds.X * uiScale * 2), 64, 1024),
                    Math.Clamp((int)Math.Ceiling(bounds.Y * uiScale * 2), 64, 1024));
                if (view.Size != size) { view.Size = size; Frame(render); }
                var live = render.Model is ClassicPlayerBody || render.Model!.HasMeta("live_item_model") && render.Model.GetMeta("live_item_model").AsBool();
                view.RenderTargetUpdateMode = live ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Once;
            }
        }
        else foreach (var render in _renders.Values)
                if (render.View is { } view) view.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        SetMeta("represented_models", _renders.Where(row => row.Value.Model is not null).Select(row => row.Key).ToArray());
        SetMeta("missing_models", _renders.Where(row => row.Value.Failure is not null).Select(row => row.Key + ": " + row.Value.Failure).ToArray());
        QueueRedraw();
    }

    private Render Create(ClassicInventoryModelSlot slot)
    {
        Node3D? model = null;
        SubViewport? view = null;
        try
        {
            model = slot.Build();
            if (model is null) return new(null, null, null, "No source-bound model is available.");
            view = new SubViewport
            {
                Name = "InventoryModelView",
                OwnWorld3D = true,
                TransparentBg = true,
                Size = new(256, 256),
                HandleInputLocally = false,
                GuiDisableInput = true,
                Msaa3D = Viewport.Msaa.Msaa4X,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            AddChild(view); view.AddChild(model);
            view.AddChild(new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = Colors.Transparent,
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = new Color(0.82f, 0.86f, 0.91f),
                    AmbientLightEnergy = 0.3f,
                    TonemapMode = Godot.Environment.ToneMapper.Filmic,
                }
            });
            view.AddChild(new DirectionalLight3D
            {
                RotationDegrees = new(-45, -35, 0),
                LightEnergy = 0.9f,
                LightColor = new Color(1, 0.94f, 0.83f)
            });
            view.AddChild(new DirectionalLight3D
            {
                RotationDegrees = new(-25, 140, 0),
                LightEnergy = 0.35f,
                LightColor = new Color(0.76f, 0.85f, 1)
            });
            var camera = new Camera3D
            {
                Current = true,
                Projection = Camera3D.ProjectionType.Orthogonal,
                KeepAspect = Camera3D.KeepAspectEnum.Height,
                Near = 0.01f,
                Far = 200
            };
            view.AddChild(camera);
            if (model is ClassicPlayerBody body) body.Publish(false, 0, Vector3.Forward);
            var result = new Render(view, model, camera, null); Frame(result);
            GD.Print($"OPENNV_CLASSIC_INVENTORY_MODEL key={slot.Key} name={slot.Name}");
            return result;
        }
        catch (Exception error) when (error is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            if (view is not null) view.QueueFree(); else model?.Free();
            GD.Print($"OPENNV_CLASSIC_INVENTORY_MODEL_UNBOUND key={slot.Key}: {error.Message}");
            return new(null, null, null, error.Message);
        }
    }

    private static void Frame(Render render)
    {
        var camera = render.Camera!;
        var bounds = PosedBounds(render.Model!);
        var center = bounds.GetCenter();
        var distance = Math.Max(2, bounds.Size.Length() * 2);
        var direction = render.Model is ClassicPlayerBody ? new Vector3(0.32f, 0.08f, -1) : new Vector3(0.8f, 1.6f, 1);
        camera.Position = center + direction.Normalized() * distance;
        camera.LookAt(center, Vector3.Up);
        var projected = camera.GlobalTransform.AffineInverse() * bounds;
        camera.Size = Math.Max(0.01f, Math.Max(projected.Size.Y, projected.Size.X * render.View!.Size.Y / render.View.Size.X) * 1.12f);
    }

    private static Aabb PosedBounds(Node3D root)
    {
        Aabb? bounds = null;
        foreach (var mesh in root.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Where(mesh => mesh.IsVisibleInTree() && mesh.Mesh is not null))
        {
            var box = ClassicSceneryPlacement.MeshBounds(mesh, mesh.GlobalTransform);
            bounds = bounds?.Merge(box) ?? box;
        }
        return bounds ?? throw new InvalidDataException("Inventory model has no visible posed geometry.");
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        foreach (var render in _renders.Values)
        {
            if (render.Model is not ClassicPlayerBody body) continue;
            body.Publish(false, 0, Vector3.Forward);
            // Bone attachments publish after entering the isolated world. Frame
            // the held weapon with the dressed body, then keep the camera still.
            if (++render.Frames == 3) Frame(render);
        }
    }

    public override void _Draw()
    {
        foreach (var slot in _slots)
        {
            if (!_renders.TryGetValue(slot.Key, out var render)) continue;
            if (render.View is not { } view)
            {
                _font.Draw(this, slot.Name + "\n3D unavailable", slot.Bounds, HorizontalAlignment.Center, VerticalAlignment.Center, 4);
                continue;
            }
            var texture = view.GetTexture();
            var scale = Math.Min(slot.Bounds.Size.X / view.Size.X, slot.Bounds.Size.Y / view.Size.Y);
            var size = new Vector2(view.Size.X, view.Size.Y) * scale;
            DrawTextureRect(texture, new Rect2(slot.Bounds.Position + (slot.Bounds.Size - size) / 2, size), false);
        }
    }
}
