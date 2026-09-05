using Godot;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private readonly Dictionary<string, List<(MeshInstance3D Mesh, int Index)>> _faceTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _faceWeights = new(StringComparer.Ordinal);

    internal object FaceState => new
    {
        owner = _faceTargets.Count == 0 ? "absent" : "owned-tri",
        targets = _faceTargets.Count,
        surfaces = _faceTargets.Values.SelectMany(value => value).Select(value => value.Mesh).Distinct().Count(),
        active = _faceWeights.Where(value => value.Value != 0).Select(value => new { target = value.Key, weight = value.Value }).ToArray(),
        unbound = new[] { "expression-normal-updates", "head-eye-aiming", "speech-idle-face-blending" },
    };

    private void BindFaceTargets()
    {
        foreach (var surface in Parts.SelectMany(part => part.Root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()))
        {
            if (surface.Mesh is not ArrayMesh mesh) continue;
            for (var index = 0; index < mesh.GetBlendShapeCount(); index++)
            {
                var name = mesh.GetBlendShapeName(index).ToString();
                if (!_faceTargets.TryGetValue(name, out var bindings)) _faceTargets.Add(name, bindings = []);
                bindings.Add((surface, index));
            }
        }
    }

    internal void ValidateSpeechFace(FaceGenLipConfiguration configuration)
    {
        foreach (var name in configuration.MorphTargetNames.OfType<string>().Distinct(StringComparer.Ordinal))
            if (!_faceTargets.ContainsKey(name)) throw new NotSupportedException($"Source speaker {Appearance.Reference} has no owned TRI target {name}.");
    }

    internal void ApplySpeechFace(FaceGenLipConfiguration configuration, IReadOnlyList<float> values)
    {
        if (values.Count != configuration.TargetNames.Length) throw new InvalidDataException("LIP target count changed after binding.");
        for (var index = 0; index < values.Count; index++)
        {
            if (configuration.MorphTargetNames[index] is not { } name) continue;
            var value = values[index];
            if (!float.IsFinite(value)) throw new InvalidDataException("LIP face weight is non-finite.");
            foreach (var (mesh, target) in _faceTargets[name]) mesh.SetBlendShapeValue(target, value);
            _faceWeights[name] = value;
        }
    }

    internal void ClearSpeechFace()
    {
        foreach (var name in _faceWeights.Keys.ToArray())
        {
            foreach (var (mesh, target) in _faceTargets[name]) mesh.SetBlendShapeValue(target, 0);
            _faceWeights[name] = 0;
        }
    }
}
