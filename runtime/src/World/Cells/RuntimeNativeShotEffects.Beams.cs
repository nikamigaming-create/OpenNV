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
            if (markers.Length != 1) throw new NotSupportedException($"Beam {source.Form} has no unique authored BeamEnd.");
            var local = Transform3D.Identity;
            for (var node = markers[0]; node != root; node = node.GetParent() as Node3D ??
                throw new InvalidDataException("Beam endpoint left its model."))
                local = node.Transform * local;
            var sourceEnd = local.Origin;
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
            var playback = new NativeNifEffectPlayback(root, source.FadeSeconds, encoded: false);
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
}
