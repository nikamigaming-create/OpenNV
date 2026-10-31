using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

// All colors/directions are source shader inputs; no rendered observation feeds this record.
internal sealed record RuntimeNativeGrassFrame(float GameHour, Vector2 WindDirection, float WindMagnitude,
    Vector3 Ambient, Vector3 DiffuseDirection, Vector3 DiffuseColor, float DirectionalScale,
    Vector3 FogColor, Vector3 Fog, Vector2 Fade, Vector2 Clip);

internal sealed class RuntimeNativeGrassDraw : IDisposable
{
    private readonly ShaderMaterial _material;
    private readonly MultiMesh _instances;
    private readonly MultiMeshInstance3D _visual;
    private readonly FalloutGrassDefinition _grass;
    private readonly float _maximumWindDisplacement;
    private bool _disposed;
    private readonly bool _ownsMaterial;
    internal FalloutGrassDefinition Source => _grass;

    internal RuntimeNativeGrassDraw(Node3D parent, Shader program, RuntimeNativeGrassModel model,
        FalloutGrassBatch batch, float units, float maximumWindDisplacement, ShaderMaterial? sharedMaterial = null)
    {
        if (batch.Instances.Length == 0) throw new ArgumentException("Empty populations must not create grass cache entries.", nameof(batch));
        if (!float.IsFinite(units) || units <= 0 || !float.IsFinite(maximumWindDisplacement) || maximumWindDisplacement < 0)
            throw new ArgumentOutOfRangeException(nameof(units));
        _grass = batch.Grass;
        _maximumWindDisplacement = maximumWindDisplacement;
        _ownsMaterial = sharedMaterial is null;
        _material = sharedMaterial ?? new ShaderMaterial { Shader = program };
        _instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true };
        _visual = new MultiMeshInstance3D
        {
            Name = "SourceGrass",
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        try
        {
            _material.SetShaderParameter("source_diffuse", model.Texture);
            _material.SetShaderParameter("source_alpha_cutoff", model.AlphaCutoff);
            _material.SetShaderParameter("source_units", units);
            _material.SetShaderParameter("source_vertex_lighting", (_grass.Flags & 1) != 0);
            _material.SetShaderParameter("source_uniform_scale", (_grass.Flags & 2) != 0);
            _material.SetShaderParameter("source_fit_slope", (_grass.Flags & 4) != 0);
            _instances.Mesh = model.Mesh;
            _instances.InstanceCount = batch.Instances.Length;
            Aabb? bounds = null;
            for (var index = 0; index < batch.Instances.Length; index++)
            {
                var packed = batch.Instances[index];
                _instances.SetInstanceTransform(index, Transform3D.Identity);
                _instances.SetInstanceCustomData(index, new Color(packed.X, packed.Y, packed.Z, packed.W));
                var normalLength = FalloutGrassInstanceMath.DecodeNormal(packed).Length();
                var scale = MathF.Abs(1f + .01f * packed.W);
                if ((_grass.Flags & 2) == 0) scale = Math.Max(1f, scale);
                var radius = model.Radius * scale * (((_grass.Flags & 4) != 0) ? Math.Max(1f, normalLength) : 1f)
                    + maximumWindDisplacement * model.MaximumAlphaSquared;
                var center = new Vector3(packed.X, packed.Z, -packed.Y) * units;
                var extent = Vector3.One * (radius * units);
                var next = new Aabb(center - extent, extent * 2);
                bounds = bounds is { } prior ? prior.Merge(next) : next;
            }
            _instances.CustomAabb = bounds!.Value;
            _visual.Multimesh = _instances;
            _visual.MaterialOverride = _material;
            _visual.Visible = false; // First authoritative frame must bind lighting/time before drawing.
            parent.AddChild(_visual);
        }
        catch { Dispose(); throw; }
    }

    internal void Sample(RuntimeNativeGrassFrame frame)
    {
        if (frame.Fade.Y <= 0 || frame.Fog.Y <= 0 || frame.Fog.Z <= 0 ||
            frame.Clip.X <= 0 || frame.Clip.Y <= frame.Clip.X ||
            MathF.Abs(frame.WindMagnitude) * frame.WindDirection.Length() > _maximumWindDisplacement)
            throw new InvalidDataException("Grass dynamic source input exceeds its qualified projection/environment bounds.");
        _material.SetShaderParameter("source_wind", new Vector4(frame.WindDirection.X, frame.WindDirection.Y,
            frame.WindMagnitude, FalloutGrassInstanceMath.GameHourPhase(frame.GameHour, _grass.WavePeriod)));
        _material.SetShaderParameter("source_ambient", frame.Ambient);
        _material.SetShaderParameter("source_diffuse_direction", frame.DiffuseDirection);
        _material.SetShaderParameter("source_diffuse_color", frame.DiffuseColor);
        _material.SetShaderParameter("source_directional_scale", frame.DirectionalScale);
        _material.SetShaderParameter("source_fog_color", frame.FogColor);
        _material.SetShaderParameter("source_fog", frame.Fog);
        _material.SetShaderParameter("source_fade", frame.Fade);
        _material.SetShaderParameter("source_clip", frame.Clip);
        _visual.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_visual))
        {
            _visual.Visible = false;
            _visual.Multimesh = null; _visual.MaterialOverride = null;
            // LAND/scene TreeExiting can retire this owner while its parent is
            // already walking its child list. Detach render resources now, but
            // leave structural removal to Godot's normal deletion boundary.
            if (_visual.GetParent() is null) _visual.Free();
            else if (!_visual.IsQueuedForDeletion()) _visual.QueueFree();
        }
        _instances.Mesh = null; _instances.Dispose();
        if (_ownsMaterial) { _material.Shader = null; _material.Dispose(); }
    }

    internal void Show() => _visual.Visible = true;
}
