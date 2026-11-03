using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeShotEffects
{
    private RuntimeNativeNifPrototype? _beamPrototype;
    private string? _beamPath;
    private long _beams;
    private object? _lastBeam;

    // Ray collision already owns the endpoint and damage. Stretch the source
    // model along its authored BeamEnd axis, preserving its cross section,
    // texture controllers and per-eye billboard. This creates no second hit.
    internal void Beam(FalloutProjectile source, Vector3 origin, Vector3 endpoint)
    {
        if (source.Type != 4) return;
        if (!origin.IsFinite() || !endpoint.IsFinite() || source.FadeSeconds <= 0)
            throw new InvalidDataException($"Beam {source.Form} has invalid endpoints or no source lifetime.");
        var distance = origin.DistanceTo(endpoint);
        if (distance < .0001f) return;
        var path = source.Model ?? throw new NotSupportedException($"Beam {source.Form} has no source model.");
        if (_beamPath != path)
        {
            var prototype = new RuntimeNativeNifPrototype(ReadModel(path), _units);
            _beamPrototype?.Scene.Root.Free();
            _beamPrototype = prototype; _beamPath = path;
        }
        var root = _beamPrototype!.Instantiate();
        try
        {
            var markers = root.FindChildren("*", "", true, false).OfType<Node3D>()
                .Where(node => node.HasMeta("opennv_nif_source_name") && node.GetMeta("opennv_nif_source_name").AsString() == "BeamEnd").ToArray();
            if (markers.Length > 1) throw new NotSupportedException($"Beam {source.Form} has ambiguous authored BeamEnd markers.");
            var sourceEnd = markers.Length == 1
                ? BeamLocalTransform(markers[0], root).Origin
                : BeamGeometryEnd(root);
            var length = sourceEnd.Length();
            if (!float.IsFinite(length) || length < .0001f)
                throw new InvalidDataException($"Beam {source.Form} has a degenerate source extent.");
            var sourceAxis = sourceEnd / length;
            var targetAxis = (endpoint - origin) / distance;
            // Align arbitrary source axes rather than assuming a fixed export
            // length or scaling width together with the beam's reach.
            var sourceFrame = Basis.LookingAt(sourceAxis, Mathf.Abs(sourceAxis.Dot(Vector3.Up)) > .99f ? Vector3.Right : Vector3.Up);
            var targetFrame = Basis.LookingAt(targetAxis, Mathf.Abs(targetAxis.Dot(Vector3.Up)) > .99f ? Vector3.Right : Vector3.Up);
            root.Transform = new(targetFrame * Basis.FromScale(new(1, 1, distance / length)) * sourceFrame.Inverse(), origin);
            AddChild(root);
            var playback = new NativeNifEffectPlayback(root, source.FadeSeconds, encoded: false, _sounds);
            playback.Start();
            _effects.Add(new(root, source.FadeSeconds, playback));
            _lastBeam = new
            {
                ordinal = ++_beams,
                projectile = source.Form.ToString(),
                model = path,
                origin = V(origin),
                endpoint = V(endpoint),
                renderedEndpoint = V(root.ToGlobal(sourceEnd)),
                distanceMeters = distance,
                sourceLengthMeters = length,
                lifetimeSeconds = source.FadeSeconds,
                surfaces = root.FindChildren("*", "MeshInstance3D", true, false).Count,
                owner = "source-beam-model-and-ray-contact",
                unbound = "retail-fade-curve-and-pixel-parity"
            };
        }
        catch { root.Free(); throw; }
    }

    private static Transform3D BeamLocalTransform(Node3D node, Node3D root)
    {
        var local = Transform3D.Identity;
        for (; node != root; node = node.GetParent() as Node3D ??
            throw new InvalidDataException("Beam geometry left its model."))
            local = node.Transform * local;
        return local;
    }

    private static Vector3 BeamGeometryEnd(Node3D root)
    {
        // Some owned beam models encode their length entirely in geometry and
        // have no endpoint node. Preserve their root origin and transverse size.
        Aabb? bounds = null;
        foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null) continue;
            var local = BeamLocalTransform(mesh, root) * mesh.GetAabb();
            bounds = bounds is { } current ? current.Merge(local) : local;
        }
        if (bounds is not { } geometry || !geometry.Position.IsFinite() || !geometry.Size.IsFinite())
            throw new NotSupportedException("Beam has neither an endpoint marker nor finite authored geometry.");
        var size = geometry.Size;
        var axis = (int)size.MaxAxisIndex();
        for (var other = 0; other < 3; other++)
            if (other != axis && size[other] >= size[axis])
                throw new NotSupportedException("Beam geometry has no unique longitudinal axis.");
        var near = geometry.Position[axis];
        var far = geometry.End[axis];
        var end = Vector3.Zero;
        end[axis] = Mathf.Abs(near) > Mathf.Abs(far) ? near : far;
        return end;
    }

}
