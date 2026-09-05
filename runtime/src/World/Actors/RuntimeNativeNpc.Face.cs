using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private readonly Dictionary<string, List<(MeshInstance3D Mesh, int Index)>> _faceTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _faceWeights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _speechFaceWeights = new(StringComparer.Ordinal);
    private FalloutFaceBlink? _blink;
    internal float BlinkWeight => _blink?.Weight ?? 0;
    internal float FaceWeight(string name) => _faceWeights.GetValueOrDefault(name);

    internal object FaceState => new
    {
        owner = _faceTargets.Count == 0 ? "absent" : "owned-tri",
        targets = _faceTargets.Count,
        surfaces = _faceTargets.Values.SelectMany(value => value).Select(value => value.Mesh).Distinct().Count(),
        active = _faceWeights.Where(value => value.Value != 0).Select(value => new { target = value.Key, weight = value.Value }).ToArray(),
        blink = _blink is null ? null : new
        {
            _blink.Weight,
            _blink.Cycles,
            _blink.DelaySeconds,
            _blink.ElapsedSeconds,
            _blink.PendingTargets,
            _blink.Settings,
            owner = "owned-settings-facegen-queue",
            randomOwner = "opennv-stream-retail-phase-unmatched"
        },
        unbound = new[] { "expression-normal-updates", "head-eye-aiming", "matched-face-blend-timing" },
    };

    internal void ConfigureFaceAnimation(FalloutPluginStack records)
    {
        if (_blink is not null) throw new InvalidOperationException("Face animation is already configured.");
        if (!_faceTargets.ContainsKey("BlinkLeft") && !_faceTargets.ContainsKey("BlinkRight")) return;
        if (!_faceTargets.ContainsKey("BlinkLeft") || !_faceTargets.ContainsKey("BlinkRight"))
            throw new NotSupportedException("Bilateral blinking requires both source eyelid morphs.");
        _blink = new(FalloutFaceBlinkSettings.Read(records), _aiRandom.NextUnitFloat);
    }

    private void AdvanceFaceAnimation(double delta)
    {
        var lookDown = Skeleton.NamedMorphWeights.Where(value => value.Name == "LookDown")
            .Select(value => value.Weight).DefaultIfEmpty(0).Max();
        _blink?.Advance(delta, lookDown);
        PublishFace();
    }

    private void PublishFace()
    {
        var values = new Dictionary<string, float>(_speechFaceWeights, StringComparer.Ordinal);
        if (_blink is not null) { values["BlinkLeft"] = _blink.Weight; values["BlinkRight"] = _blink.Weight; }
        // Native FaceGen copies the procedural channel group, then applies
        // nonzero authored animation channels at their full selected weight.
        foreach (var group in Skeleton.NamedMorphWeights.Where(value => value.Weight != 0 && _faceTargets.ContainsKey(value.Name))
            .GroupBy(value => value.Name, StringComparer.Ordinal))
        {
            var weights = group.Select(value => value.Weight).Distinct().ToArray();
            if (weights.Length != 1) throw new NotSupportedException($"Face target {group.Key} has competing source morph owners.");
            values[group.Key] = weights[0];
        }
        foreach (var name in _faceWeights.Keys.Concat(values.Keys).Distinct(StringComparer.Ordinal).ToArray())
        {
            var weight = values.GetValueOrDefault(name);
            if (!float.IsFinite(weight)) throw new InvalidDataException("Face publication contains a non-finite weight.");
            foreach (var (mesh, target) in _faceTargets[name]) mesh.SetBlendShapeValue(target, weight);
            _faceWeights[name] = weight;
        }
    }

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
            _speechFaceWeights[name] = value;
        }
        PublishFace();
    }

    internal void ClearSpeechFace()
    {
        _speechFaceWeights.Clear();
        PublishFace();
    }
}
