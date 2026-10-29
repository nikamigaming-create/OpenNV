using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Godot;

namespace OpenNV.Runtime.Presentation.Rendering;

// Functional occlusion measurement, not frame recording. Both passes draw the
// actual presented body-part meshes with the same skin, morphs and alpha test.
// Two mask colors distinguish query fragments from unchanged world colors.
// Async RD transfers keep the paused menu responsive; no pixels reach disk.
internal sealed class NativeVatsVisibilitySamples : IDisposable
{
    private sealed record Pixels(byte[] Data, RenderingDevice.DataFormat Format);
    private sealed record Pair(Pixels Red, Pixels Blue);
    private static readonly ConditionalWeakTable<Shader, Shader> MaskShaders = new();
    private readonly Node3D _owner;
    private readonly SubViewport _view;
    private readonly Camera3D _camera;
    private readonly uint _queryLayer, _hiddenLayer, _worldMask;
    private readonly List<(Camera3D Camera, uint Mask)> _cameraMasks = [];
    private readonly List<(ReflectionProbe Probe, uint Mask)> _probeMasks = [];
    private readonly CancellationTokenSource _cancel = new();
    private readonly List<(MeshInstance3D Mesh, uint Layers)> _hidden = [];
    private readonly List<MeshInstance3D> _copies = [];
    private readonly List<ShaderMaterial> _materials = [];
    private int _disposed;
    private bool _measuring;

    internal NativeVatsVisibilitySamples(Node3D owner, Camera3D camera)
    {
        _owner = owner;
        if (RenderingServer.GetRenderingDevice() is null)
            throw new NotSupportedException("VATS visibility requires asynchronous rendering-device samples.");
        uint used = 0;
        var nodes = owner.GetTree().Root.FindChildren("*", "", true, false);
        foreach (var visual in nodes.OfType<VisualInstance3D>()) used |= visual.Layers;
        var free = Enumerable.Range(0, 20).Select(bit => 1u << bit).Where(bit => (used & bit) == 0).Take(2).ToArray();
        if (free.Length != 2) throw new NotSupportedException("VATS visibility has no free temporary rendering layers.");
        _queryLayer = free[0]; _hiddenLayer = free[1]; _worldMask = camera.CullMask & ~_queryLayer & ~_hiddenLayer;
        _view = new SubViewport
        {
            Name = "VatsBodyPartSamples",
            World3D = owner.GetWorld3D(),
            Size = (Vector2I)camera.GetViewport().GetVisibleRect().Size,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            ProcessMode = Node.ProcessModeEnum.Always
        };
        _camera = new Camera3D
        {
            Projection = camera.Projection,
            Fov = camera.Fov,
            Size = camera.Size,
            Near = camera.Near,
            Far = camera.Far,
            KeepAspect = camera.KeepAspect,
            FrustumOffset = camera.FrustumOffset,
            CullMask = _queryLayer,
            Current = true,
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                TonemapMode = Godot.Environment.ToneMapper.Linear,
                TonemapExposure = 1,
            }
        };
        try
        {
            owner.AddChild(_view); _view.AddChild(_camera); _camera.GlobalTransform = camera.GlobalTransform;
            foreach (var active in nodes.OfType<Camera3D>().Where(value => value.GetWorld3D() == owner.GetWorld3D()))
            {
                _cameraMasks.Add((active, active.CullMask));
                active.CullMask = active.CullMask & ~_queryLayer;
            }
            foreach (var probe in nodes.OfType<ReflectionProbe>())
            { _probeMasks.Add((probe, probe.CullMask)); probe.CullMask &= ~_queryLayer; }
        }
        catch { Dispose(); throw; }
    }

    internal async Task<float> MeasureAsync(Node3D actor, byte part)
    {
        RequireActive();
        if (_measuring) throw new InvalidOperationException("VATS visibility requests must be sequential.");
        _measuring = true;
        var meshes = actor.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
            .Where(mesh => mesh.Visible && mesh.Mesh is not null && mesh.HasMeta("opennv_nif_body_part") &&
                mesh.GetMeta("opennv_nif_body_part").AsInt32() == part).ToArray();
        if (meshes.Length == 0) { _measuring = false; return 0; }
        try
        {
            foreach (var mesh in meshes)
            {
                _hidden.Add((mesh, mesh.Layers));
                foreach (var (active, mask) in _cameraMasks)
                    if (GodotObject.IsInstanceValid(active) && (mask & mesh.Layers) != 0) active.CullMask |= _hiddenLayer;
                foreach (var (probe, mask) in _probeMasks)
                    if (GodotObject.IsInstanceValid(probe) && (mask & mesh.Layers) != 0) probe.CullMask |= _hiddenLayer;
                mesh.Layers = _hiddenLayer;
                var copy = new MeshInstance3D
                {
                    Name = "SourcePartQuery",
                    Mesh = mesh.Mesh,
                    Skin = mesh.Skin,
                    Layers = _queryLayer,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    ExtraCullMargin = mesh.ExtraCullMargin,
                    LodBias = mesh.LodBias
                };
                _copies.Add(copy); _view.AddChild(copy); copy.GlobalTransform = mesh.GlobalTransform;
                foreach (var property in mesh.GetPropertyList())
                {
                    var propertyName = property["name"].AsString();
                    const string prefix = "instance_shader_parameters/";
                    if (propertyName.StartsWith(prefix, StringComparison.Ordinal))
                        copy.SetInstanceShaderParameter(propertyName[prefix.Length..], mesh.Get(propertyName));
                }
                if (mesh.Skin is not null)
                    copy.Skeleton = mesh.GetNode<Skeleton3D>(mesh.Skeleton).GetPath();
                if (mesh.Mesh is ArrayMesh array)
                    for (var blend = 0; blend < array.GetBlendShapeCount(); ++blend) copy.SetBlendShapeValue(blend, mesh.GetBlendShapeValue(blend));
                for (var surface = 0; surface < mesh.Mesh!.GetSurfaceCount(); ++surface)
                {
                    if (mesh.GetActiveMaterial(surface) is not ShaderMaterial { Shader: { } shader } original)
                        throw new NotSupportedException("VATS body-part visibility requires its original alpha-aware shader material.");
                    var material = (ShaderMaterial)original.Duplicate();
                    material.Shader = MaskShaders.GetValue(shader, MaskShader);
                    material.NextPass = null;
                    foreach (var uniform in shader.GetShaderUniformList())
                    {
                        var name = uniform.AsGodotDictionary()["name"].AsString();
                        material.SetShaderParameter(name, original.GetShaderParameter(name));
                    }
                    _materials.Add(material); copy.SetSurfaceOverrideMaterial(surface, material);
                }
            }
            _camera.CullMask = _queryLayer;
            var totalPair = await ColorsAsync();
            var total = Count(totalPair, totalPair);
            if (total == 0) return 0;
            _camera.CullMask = _worldMask | _queryLayer;
            var visible = Count(await ColorsAsync(), totalPair);
            RequireActive();
            return Math.Clamp(visible / (float)total, 0, 1);
        }
        finally { RestorePart(); _measuring = false; }
    }

    private static Shader MaskShader(Shader source)
    {
        var code = source.Code;
        var fragment = code.IndexOf("void fragment()", StringComparison.Ordinal);
        if (fragment < 0) throw new NotSupportedException("VATS query cannot find the owned material fragment stage.");
        var open = code.IndexOf('{', fragment); var depth = 1; var end = open + 1;
        for (; end < code.Length && depth != 0; ++end)
        { if (code[end] == '{') ++depth; else if (code[end] == '}') --depth; }
        if (depth != 0) throw new InvalidDataException("VATS material fragment stage is unbalanced.");
        if (code[open..end].Contains("return;", StringComparison.Ordinal))
            throw new NotSupportedException("VATS material has an early fragment return requiring its query adapter.");
        code = code.Insert(end - 1, "\nALBEDO = opennv_vats_sample_color.rgb;\nEMISSION = vec3(0.0);\n");
        var declaration = Regex.Match(code, @"shader_type\s+spatial\s*;");
        if (!declaration.Success) throw new NotSupportedException("VATS query material is not spatial.");
        code = code.Insert(declaration.Index + declaration.Length, "\nuniform vec4 opennv_vats_sample_color : source_color;\n");
        var modes = Regex.Match(code, @"render_mode\s+([^;]+);");
        if (!modes.Success) code = code.Insert(declaration.Index + declaration.Length, "\nrender_mode unshaded, fog_disabled;\n");
        else
        {
            var values = modes.Groups[1].Value.Split(',').Select(value => value.Trim()).ToList();
            foreach (var mode in new[] { "unshaded", "fog_disabled" }) if (!values.Contains(mode)) values.Add(mode);
            code = code.Remove(modes.Index, modes.Length).Insert(modes.Index, "render_mode " + string.Join(", ", values) + ";");
        }
        return new Shader { Code = code };
    }

    private async Task<Pair> ColorsAsync()
    {
        foreach (var material in _materials) material.SetShaderParameter("opennv_vats_sample_color", new Color(1, 0, 0, 1));
        var first = await ReadAsync();
        RequireActive();
        foreach (var material in _materials) material.SetShaderParameter("opennv_vats_sample_color", new Color(0, 0, 1, 1));
        var second = await ReadAsync();
        RequireActive();
        return new(first, second);
    }

    private static int Count(Pair measured, Pair reference)
    {
        var first = measured.Red; var second = measured.Blue;
        if (first.Format != second.Format || first.Data.Length != second.Data.Length) throw new InvalidDataException("VATS sample images changed extent.");
        if (first.Format != reference.Red.Format || first.Data.Length != reference.Red.Data.Length)
            throw new InvalidDataException("VATS sample reference changed extent.");
        var half = first.Format == RenderingDevice.DataFormat.R16G16B16A16Sfloat;
        var bgra = first.Format is RenderingDevice.DataFormat.B8G8R8A8Unorm or RenderingDevice.DataFormat.B8G8R8A8Srgb;
        if (!half && first.Format is not (RenderingDevice.DataFormat.R8G8B8A8Unorm or RenderingDevice.DataFormat.R8G8B8A8Srgb) && !bgra)
            throw new NotSupportedException("VATS sample pixel format is unbound: " + first.Format);
        var stride = half ? 8 : 4; var count = 0;
        float Component(byte[] data, int offset, int channel) => half
            ? (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + channel * 2)))
            : data[offset + channel] / 255f;
        for (var offset = 0; offset < first.Data.Length; offset += stride)
        {
            var red = bgra ? 2 : 0; var blue = bgra ? 0 : 2;
            var expectedRed = Component(reference.Red.Data, offset, red) - Component(reference.Blue.Data, offset, red);
            var expectedBlue = Component(reference.Blue.Data, offset, blue) - Component(reference.Red.Data, offset, blue);
            const float quantum = 1f / 255;
            if (expectedRed < 2 * quantum || expectedBlue < 2 * quantum || MathF.Abs(expectedRed - expectedBlue) > 2 * quantum) continue;
            var actualRed = Component(first.Data, offset, red) - Component(second.Data, offset, red);
            var actualBlue = Component(second.Data, offset, blue) - Component(first.Data, offset, blue);
            // Only a pixel inside the isolated source-part mask, matching its
            // measured alpha-aware red/blue response, can count as visible.
            if (MathF.Abs(actualRed - expectedRed) <= quantum && MathF.Abs(actualBlue - expectedBlue) <= quantum &&
                MathF.Abs(Component(first.Data, offset, 1) - Component(second.Data, offset, 1)) <= quantum) ++count;
        }
        return count;
    }

    private async Task<Pixels> ReadAsync()
    {
        RequireActive();
        _view.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await _owner.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        RequireActive();
        var complete = new TaskCompletionSource<Pixels>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled = _cancel.Token.Register(() => complete.TrySetCanceled(_cancel.Token));
        var texture = _view.GetTexture().GetRid();
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0) { complete.TrySetCanceled(); return; }
                var device = RenderingServer.GetRenderingDevice();
                var rid = RenderingServer.TextureGetRdTexture(texture);
                var format = device.TextureGetFormat(rid).Format;
                var error = device.TextureGetDataAsync(rid, 0, Callable.From<byte[]>(data => complete.TrySetResult(new(data, format))));
                if (error != Error.Ok) complete.TrySetException(new InvalidOperationException("VATS fragment readback failed: " + error));
            }
            catch (Exception error) { complete.TrySetException(error); }
        }));
        return await complete.Task;
    }

    private void RequireActive() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    private void RestorePart()
    {
        foreach (var (mesh, layers) in _hidden) if (GodotObject.IsInstanceValid(mesh)) mesh.Layers = layers;
        _hidden.Clear();
        foreach (var copy in _copies) if (GodotObject.IsInstanceValid(copy)) copy.Free();
        _copies.Clear(); _materials.Clear();
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancel.Cancel(); RestorePart();
        foreach (var (camera, mask) in _cameraMasks) if (GodotObject.IsInstanceValid(camera)) camera.CullMask = mask;
        _cameraMasks.Clear();
        foreach (var (probe, mask) in _probeMasks) if (GodotObject.IsInstanceValid(probe)) probe.CullMask = mask;
        _probeMasks.Clear();
        if (GodotObject.IsInstanceValid(_view)) _view.QueueFree();
    }
}
