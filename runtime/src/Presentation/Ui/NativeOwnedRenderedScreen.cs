using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedRenderedScreen : Node
{
    private readonly ShaderMaterial _material;
    private readonly NativeOwnedRenderedDevice _device;
    private readonly FalloutD3D9PixelProgram _program;
    private readonly Dictionary<int, System.Numerics.Vector4> _constants = [];
    private readonly Dictionary<int, Image> _effectImages = [];
    private Vector2? _lastPointer;
    private TextureRect? _portrait;
    internal SubViewport ContentView { get; }
    internal NativeOwnedRaceSexMenu Menu { get; }

    internal NativeOwnedRenderedScreen(NativeOwnedRenderedDevice device, FalloutPluginStack records,
        FalloutInstallationSettings settings, Action<Exception> failed)
    {
        Name = "OwnedRenderedScreen";
        ProcessMode = ProcessModeEnum.Always;
        _device = device;
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned screen content is absent.");
        var path = $"shaders/shaderpackage{settings.Renderer.ShaderPackage:000}.sdp";
        if (!source.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
        var bytecode = FalloutShaderPackage.Read(bytes).Single(shader => shader.Name.Equals("ISTV.pso", StringComparison.OrdinalIgnoreCase));
        var program = _program = FalloutD3D9PixelProgram.Read(bytecode.Bytecode, new HashSet<int> { 1, 2 });
        if (!program.Constants.SequenceEqual(new[] { 0, 1, 2, 3, 4 }) || !program.Samplers.SequenceEqual(new[] { 0, 1, 2 }))
            throw new NotSupportedException("Owned rendered-menu pixel program requires another parameter owner.");
        // The menu renderer's orthographic source canvas is an engine protocol.
        // The source pixel program owns its packing into the model's unchanged UVs.
        ContentView = new SubViewport
        {
            Name = "SourceCanvas",
            Size = new(1280, 960),
            Disable3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(ContentView);
        Menu = new(records, failed);
        Menu.PageChanged += page => device.SelectSection(page < 4 ? page : page is 5 or 6 or 7 ? 3 : 2);
        Menu.SetCanvas(new(1280, 960));
        ContentView.AddChild(Menu);
        var shader = new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_back, depth_draw_opaque;
            """ + program.GodotSource + """
            void fragment() {
                vec3 encoded = owned_pixel_program(UV).rgb * COLOR.rgb;
                ALBEDO = mix(encoded / 12.92, pow((max(encoded, vec3(0.0)) + 0.055) / 1.055,
                    vec3(2.4)), step(vec3(0.04045), encoded));
            }
            """ };
        var material = _material = new ShaderMaterial { Shader = shader, ResourceName = "Owned rendered-menu program" };
        material.SetShaderParameter("s0", ContentView.GetTexture());
        material.SetShaderParameter("s1", NativeOwnedMediaLoader.LoadTexture("textures/pipboy3000/PipboyDistortEffectMap.dds"));
        material.SetShaderParameter("s2", NativeOwnedMediaLoader.LoadTexture("textures/pipboy3000/PipboyScanlines.dds"));
        material.SetShaderParameter("c0", new Vector4(1f / ContentView.Size.X, 1f / ContentView.Size.Y, 0, 0));
        material.SetShaderParameter("c1", new Vector4(1, 0, 0, 0));
        material.SetShaderParameter("c2", new Vector4(0, -1, 0, 0));
        material.SetShaderParameter("c3", new Vector4(0, -1, 0, 0));
        material.SetShaderParameter("c4", Vector4.One);
        foreach (var index in program.Constants)
        {
            var value = material.GetShaderParameter($"c{index}").AsVector4();
            _constants.Add(index, new(value.X, value.Y, value.Z, value.W));
        }
        foreach (var index in new[] { 1, 2 })
        {
            var image = ((Texture2D)material.GetShaderParameter($"s{index}").AsGodotObject()).GetImage();
            if (image.IsCompressed() && image.Decompress() != Error.Ok) throw new InvalidDataException("Screen effect texture could not be sampled.");
            _effectImages.Add(index, image);
        }
        device.Geometry("Screen:0").MaterialOverride = material;
        SetMeta("opennv_screen_program", identity + "::" + bytecode.Name);
        SetMeta("opennv_screen_unbound", "portrait-drag-and-zoom,animated-distortion,pass-quantization");
        device.Resized += ResizeCanvas;
    }

    public override void _Ready() => ResizeCanvas();
    public override void _ExitTree()
    {
        _device.Resized -= ResizeCanvas;
        foreach (var image in _effectImages.Values) image.Dispose();
        _effectImages.Clear();
    }

    internal Vector2? SourcePoint(Vector2 screenPosition)
    {
        if (_device.PickScreen(screenPosition) is not { } uv) return null;
        return CanvasPoint(uv);
    }

    internal Vector2? CanvasPoint(Vector2 uv)
    {
        System.Numerics.Vector2 point;
        try { point = _program.VisibleSampleCoordinate(0, new(uv.X, uv.Y), _constants, SampleEffect); }
        catch (NotSupportedException) { return null; } // The filtered portrait has no unique menu-tile sample.
        var result = new Vector2(point.X * 1280, point.Y * 960);
        return Menu.Panel.HasPoint(result) ? result : null;
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion)
        {
            ContentView.PushInput(input, true); GetViewport().SetInputAsHandled(); return;
        }
        if (input is not InputEventMouse mouse) return;
        var point = SourcePoint(mouse.Position - _device.GlobalPosition);
        using var copy = (InputEventMouse)mouse.Duplicate();
        var position = point is { } canvas ? canvas * new Vector2(ContentView.Size.X / 1280f, ContentView.Size.Y / 960f) : new Vector2(-1, -1);
        copy.Position = copy.GlobalPosition = position;
        if (copy is InputEventMouseMotion motion) motion.Relative = _lastPointer is { } previous ? position - previous : Vector2.Zero;
        _lastPointer = position;
        ContentView.PushInput(copy, true); GetViewport().SetInputAsHandled();
    }

    private System.Numerics.Vector4 SampleEffect(int sampler, System.Numerics.Vector2 coordinate)
    {
        var image = _effectImages[sampler];
        var x = coordinate.X * image.GetWidth() - 0.5f; var y = coordinate.Y * image.GetHeight() - 0.5f;
        var left = (int)MathF.Floor(x); var top = (int)MathF.Floor(y);
        Color Pixel(int px, int py) => image.GetPixel((px % image.GetWidth() + image.GetWidth()) % image.GetWidth(),
            (py % image.GetHeight() + image.GetHeight()) % image.GetHeight());
        var value = Pixel(left, top).Lerp(Pixel(left + 1, top), x - left)
            .Lerp(Pixel(left, top + 1).Lerp(Pixel(left + 1, top + 1), x - left), y - top);
        return new(value.R, value.G, value.B, value.A);
    }

    internal void SetPortrait(NativeOwnedActorPreview portrait)
    {
        if (_portrait is not null) { _portrait.QueueFree(); _portrait = null; }
        _portrait = new TextureRect
        {
            // Ignore the texture minimum before setting Size; reversing this
            // order silently enlarged and cropped the portrait's logical canvas.
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = portrait.View.GetTexture(),
            Size = new(1280, 960),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ContentView.AddChild(_portrait); ContentView.MoveChild(_portrait, 0);
        if (_portrait.Size != new Vector2(1280, 960)) throw new InvalidOperationException("Portrait canvas extent changed.");
    }

    private void ResizeCanvas()
    {
        if (!IsInsideTree()) return;
        var target = FalloutRenderedMenuProjection.RenderTargetSize(_device.View.Size.X, _device.View.Size.Y);
        var size = new Vector2I(target.Width, target.Height);
        ContentView.Size = size;
        ContentView.CanvasTransform = new Transform2D(0, new Vector2(size.X / 1280f, size.Y / 960f), 0, Vector2.Zero);
        _material.SetShaderParameter("c0", new Vector4(1f / size.X, 1f / size.Y, 0, 0));
        _constants[0] = new(1f / size.X, 1f / size.Y, 0, 0);
    }
}
