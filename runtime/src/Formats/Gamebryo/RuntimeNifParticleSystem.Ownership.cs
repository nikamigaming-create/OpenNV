using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifParticleSystem
{
    private QuadMesh? _particleQuad;
    private FalloutNifFile? _sourceFile;
    private bool _configured;
    private bool _retired;

    internal void Configure(FalloutNifFile file, FalloutNifParticleSystem source,
        IReadOnlyDictionary<int, Node3D> nodes, Material material, float units)
    {
        if (_configured || _retired) throw new InvalidOperationException("Particle instance already owns a source lifetime.");
        if (!float.IsFinite(units) || units <= 0) throw new InvalidDataException("Particle source units must be finite and positive.");
        if (!ReferenceEquals(file.ReadObject(source.Block.Index), source))
            throw new InvalidDataException("Particle declaration is not owned by its selected NIF reader.");
        try
        {
            _sourceFile = file;
            ConfigureSource(file, source, nodes, material, units);
            _configured = true;
        }
        catch
        {
            // Construction may fail before this node enters a tree. Dispose
            // only our generated draw/noise owners; the supplied material,
            // source and sibling transform nodes belong to their callers.
            ReleaseParticleDraw(freeVisual: true);
            _turbulence?.Dispose(); _turbulence = null;
            _active.Clear(); _rates.Clear(); _speeds.Clear(); _lifespans.Clear(); _remainders.Clear();
            _meshes.Clear(); _colliders.Clear(); _keyColors.Clear(); _spawnOwners.Clear(); _particleIndices.Clear();
            _particles = []; _drawBuffer = []; _modifiers = [];
            _source = null!; _data = null!; _nodes = null!;
            _sourceFile = null;
            throw;
        }
    }

    private void RequireConfigured()
    {
        if (!_configured || _retired) throw new InvalidOperationException("Particle instance has no live source lifetime.");
    }

    private void ConfigureParticleDraw(ShaderMaterial shader)
    {
        var quad = new QuadMesh();
        MultiMesh? draw = null;
        MultiMeshInstance3D? visual = null;
        try
        {
            quad.Size = Vector2.One * 2;
            quad.Material = shader;
            draw = new MultiMesh();
            draw.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            draw.UseColors = true;
            draw.UseCustomData = true;
            draw.Mesh = quad;
            draw.InstanceCount = _data.Maximum;
            draw.VisibleInstanceCount = 0;
            visual = new MultiMeshInstance3D();
            visual.Name = "SourceParticles";
            visual.Multimesh = draw;
            visual.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            AddChild(visual);
            if (visual.GetParent() != this) throw new InvalidOperationException("Particle draw was not attached to its lifetime owner.");
            _particleQuad = quad; _draw = draw; _visual = visual;
        }
        catch
        {
            if (visual is not null) { visual.Multimesh = null; visual.Free(); }
            if (draw is not null) { draw.Mesh = null; draw.Dispose(); }
            quad.Material = null; quad.Dispose();
            throw;
        }
    }

    private FastNoiseLite CreateParticleTurbulence()
    {
        var noise = new FastNoiseLite();
        try { noise.Seed = _random.Next(); return noise; }
        catch { noise.Dispose(); throw; }
    }

    private void ReleaseParticleDraw(bool freeVisual)
    {
        if (_visual is not null && GodotObject.IsInstanceValid(_visual))
        {
            _visual.Multimesh = null;
            if (freeVisual) _visual.Free();
        }
        if (_draw is not null) { _draw.Mesh = null; _draw.Dispose(); }
        if (_particleQuad is not null) { _particleQuad.Material = null; _particleQuad.Dispose(); }
        _draw = null!; _visual = null!; _particleQuad = null;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _retired) return;
        _retired = true;
        Exception? callbackFailure = null;
        try
        {
            for (var index = ActiveCount - 1; index >= 0; index--)
            {
                try { RetireParticle(index, _source.Block.Index, "native-owner-retirement"); }
                catch (Exception error)
                {
                    // Close the remaining identities even if one external
                    // event sink fails; every failure is returned afterwards.
                    callbackFailure = callbackFailure is null ? error : new AggregateException(callbackFailure, error);
                }
            }
        }
        finally
        {
            // Removing and reattaching a scene is not destruction. Predelete,
            // rather than ExitTree, closes this object's native capability.
            ReleaseParticleDraw(freeVisual: false);
            _turbulence?.Dispose(); _turbulence = null;
            _collisionWork.Clear(); _particleIndices.Clear();
            _active.Clear(); _rates.Clear(); _speeds.Clear(); _lifespans.Clear(); _remainders.Clear();
            _meshes.Clear(); _colliders.Clear(); _keyColors.Clear(); _spawnOwners.Clear();
            _particles = []; _drawBuffer = []; _modifiers = [];
            _nodes = null!; LifecycleEvent = null; _configured = false;
            _sourceFile = null;
        }
        if (callbackFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackFailure).Throw();
    }
}
