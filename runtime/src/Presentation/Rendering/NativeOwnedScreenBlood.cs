using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

internal sealed partial class NativeOwnedScreenBlood(FalloutScreenBlood owner,
    RuntimeLiveContentSource source, FalloutScriptMenus menus) : Node
{
    private sealed record Texture(Texture2D Image, FalloutScreenBloodMedia Media);
    private readonly Dictionary<string, Texture> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<NativeScreenBloodGroup> _groups = [];
    private IDisposable? _binding;
    private CanvasLayer _layer = null!;
    private Shader _shader = null!;
    internal int ActiveGroups => _groups.Count;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; ProcessPriority = int.MinValue + 2;
        _shader = new() { Code = """
            shader_type canvas_item;
            render_mode unshaded, blend_mul;
            uniform sampler2D blood_mask : filter_linear, repeat_disable;
            uniform sampler2D blood_color : filter_linear, repeat_disable;
            uniform float group_fade = 1.0;
            void fragment() {
                vec4 mask = texture(blood_mask, UV);
                vec3 color = texture(blood_color, UV).rgb;
                float weight = COLOR.a * group_fade * mask.a;
                vec3 tint = vec3(1.0) + weight * (mask.rgb - vec3(1.0));
                COLOR = vec4(mix(tint, color, weight), 1.0);
            }
            """ };
        _layer = new() { Name = "NativeScreenBloodLayer", Layer = 0, ProcessMode = ProcessModeEnum.Always };
        AddChild(_layer);
        _binding = owner.Bind(FalloutInstallationSettings.Read(source).Boolean("ScreenSplatter", "bBloodSplatterEnabled"), Prepare);
    }

    private Texture Read(string path)
    {
        var key = FalloutBsaArchive.CanonicalPath(path);
        if (_textures.TryGetValue(key, out var texture)) return texture;
        if (!source.TryRead(path, null, out var payload, out var identity)) throw new FileNotFoundException("Owned screen-blood texture is missing.", path);
        var image = NativeDdsTexture.Load(payload, identity);
        if (image.GetWidth() < 2 || image.GetHeight() < 2)
        {
            image.Dispose(); throw new InvalidDataException("Owned blood texture cannot contain its source atlas.");
        }
        texture = new(image, new(path, identity, Convert.ToHexString(SHA256.HashData(payload))));
        _textures.Add(key, texture); return texture;
    }

    private FalloutScreenBloodPresentation Prepare(FalloutScreenBloodRequest request)
    {
        var mask = Read(request.Mask); var color = Read(request.Color);
        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("blood_mask", mask.Image); material.SetShaderParameter("blood_color", color.Image);
        var xr = GetViewport().UseXR;
        var group = new NativeScreenBloodGroup(request.Drops) { Name = $"ScreenBlood_{request.Id}", Material = material, Visible = !xr };
        try { _layer.AddChild(group); _groups.Add(group); }
        catch { group.Free(); material.Dispose(); throw; }
        string[] unbound = xr
            ? ["directional-light-color-uv-offset", "directional-light-additive-flare", "source-color-transfer-and-layer-timing", "xr-final-eye-screen-blood-adapter"]
            : ["directional-light-color-uv-offset", "directional-light-additive-flare", "source-color-transfer-and-layer-timing"];
        GD.Print($"OPENNV_SCREEN_BLOOD request={request.Id} caller={request.Caller} drops={request.Drops.Count} " +
            $"duration={request.Duration:R} mask={mask.Media.Source} color={color.Media.Source} unbound={string.Join(',', unbound)} parity=unverified");
        var released = false;
        return new([mask.Media, color.Media], fade => material.SetShaderParameter("group_fade", fade), () =>
        {
            if (released) return;
            released = true; _groups.Remove(group);
            if (IsInstanceValid(group)) { group.Visible = false; group.QueueFree(); }
        }, unbound);
    }

    public override void _Process(double delta)
    {
        try { owner.Advance(delta, !GetTree().Paused && menus.Query() == 0); }
        catch (Exception error) { SetProcess(false); GD.PushError($"OPENNV_SCREEN_BLOOD_FAILURE {error}"); }
    }
    public override void _ExitTree()
    {
        _binding?.Dispose(); _binding = null;
        foreach (var texture in _textures.Values) texture.Image.Dispose();
        _textures.Clear(); _shader?.Dispose();
    }
}

internal sealed partial class NativeScreenBloodGroup(IReadOnlyList<FalloutScreenBloodDrop> drops) : Node2D
{
    private Vector2 _size;
    public override void _Process(double delta)
    {
        var size = GetViewportRect().Size;
        if (_size == size) return;
        _size = size; QueueRedraw();
    }
    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        foreach (var drop in drops)
        {
            var center = new Vector2((drop.X * .5f + .5f) * size.X, (.5f - drop.Y * .5f) * size.Y);
            var radius = drop.HalfSize * size.Y;
            Vector2[] points = [center + new Vector2(-radius, -radius), center + new Vector2(radius, -radius),
                center + new Vector2(radius, radius), center + new Vector2(-radius, radius)];
            Vector2[] uv = [new(drop.AtlasU + .5f, drop.AtlasV + .5f), new(drop.AtlasU, drop.AtlasV + .5f),
                new(drop.AtlasU, drop.AtlasV), new(drop.AtlasU + .5f, drop.AtlasV)];
            DrawPolygon(points, [new Color(1, 1, 1, drop.Opacity)], uv);
        }
    }
    public override void _Notification(int what) { if (what == NotificationPredelete) Material?.Dispose(); }
}
