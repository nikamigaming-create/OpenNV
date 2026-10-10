using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

// One original generated property owns one actual material and program. The
// lease is retained before either native allocation; partial construction and
// retirement never depend on a static Shader cache or a transient wrapper.
internal sealed class RuntimeNativeMoonProperty
{
    private readonly FalloutSkyCelestialPass _source;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private Shader? _shader;
    private ShaderMaterial? _material;
    private bool _retired;
    internal RuntimeNativeMoonProperty(FalloutSkyCelestialPass source) => _source = source;
    internal ShaderMaterial Material => _material ?? throw new InvalidOperationException("Moon property construction has not returned.");
    internal ShaderMaterial? CurrentMaterial => _material;
    internal string SourceIdentity => _source.Identity;
    internal void Construct()
    {
        Require();
        if (_shader is not null || _material is not null) throw new InvalidOperationException("A retained Moon property constructor cannot be replayed.");
        _shader = new Shader();
        _shader.Code = _source.GodotSource;
        var names = _shader.GetShaderUniformList().Select(item => item.AsGodotDictionary()["name"].AsString()).ToHashSet(StringComparer.Ordinal);
        if (!names.SetEquals(new[] { "source_Moon_sampler", "source_Moon_colour", "source_Moon_alpha",
                "source_Moon_rgb_multiplier", "source_Moon_draw_admitted" }))
            throw new InvalidDataException("Native Moon Shader did not expose its exact generated source input interface.");
        _material = new ShaderMaterial();
        _material.Shader = _shader;
        _material.SetMeta("opennv_source_Moon_program", _source.Identity);
        _material.SetMeta("opennv_source_Moon_package", _source.PackageSha256);
        _material.SetMeta("opennv_source_Moon_vertex", _source.VertexSha256);
        _material.SetMeta("opennv_source_Moon_pixel", _source.PixelSha256);
        _material.SetMeta("opennv_sky_object_type", _source.Role);
        _material.SetShaderParameter("source_Moon_colour", Vector3.One);
        _material.SetShaderParameter("source_Moon_alpha", 1f);
        _material.SetShaderParameter("source_Moon_draw_admitted", false);
        RequireMaterial();
    }
    internal void RequireMaterial()
    {
        Require();
        if (_material is null || _shader is null || !GodotObject.IsInstanceValid(_material) ||
            !GodotObject.IsInstanceValid(_shader) || _material.Shader?.GetInstanceId() != _shader.GetInstanceId() ||
            _material.GetMeta("opennv_source_Moon_program").AsString() != _source.Identity ||
            _material.GetMeta("opennv_sky_object_type").AsInt32() != _source.Role)
            throw new InvalidDataException("Moon property lost its actual source material/program lease.");
    }
    private void Require()
    {
        if (_retired || System.Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Moon property left its native construction/retirement thread.");
    }
    internal void Retire()
    {
        if (_retired) return;
        Require();
        // The owning Moon first frees every MeshInstance borrower. A failed
        // material release retains the Shader, whose actual borrower remains.
        if (_material is { } material)
        {
            if (!GodotObject.IsInstanceValid(material) || material.GetReferenceCount() != 1)
                throw new InvalidOperationException("Moon property retains an actual native/foreign material borrower.");
            material.SetShaderParameter("source_Moon_draw_admitted", false);
            material.SetShaderParameter("source_Moon_sampler", default(Variant));
            material.Shader = null;
            material.Dispose(); _material = null;
        }
        if (_shader is { } shader)
        {
            if (!GodotObject.IsInstanceValid(shader) || shader.GetReferenceCount() != 1)
                throw new InvalidOperationException("Moon program retains an actual native/foreign Shader borrower.");
            shader.Dispose(); _shader = null;
        }
        _retired = true;
    }
}
