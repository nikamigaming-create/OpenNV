using Godot;


namespace OpenNV.Runtime.World.Actors;

internal sealed class FaceGenMorphController
{
    private readonly FaceGenLipConfiguration _configuration;
    private readonly IReadOnlyList<MorphBinding>[] _bindings;
    private readonly float[] _sample;

    internal FaceGenMorphController(
        ActorModelSlice.LoadedActor actor,
        FaceGenLipConfiguration configuration)
    {
        _configuration = configuration;
        _sample = new float[configuration.TargetNames.Length];
        _bindings = Enumerable.Range(0, configuration.TargetNames.Length)
            .Select(_index => (IReadOnlyList<MorphBinding>)Array.Empty<MorphBinding>())
            .ToArray();
        for (var target = 0; target < configuration.TargetNames.Length; target++)
        {
            var morphName = configuration.MorphTargetNames[target];
            if (morphName is null)
                continue;
            var bindings = actor.Surfaces
                .Select(surface =>
                {
                    var index = surface.FaceGenMorphTargets
                        .Select((name, position) => (name, position))
                        .SingleOrDefault(row => row.name == morphName)
                        .position;
                    var found = surface.FaceGenMorphTargets.Contains(
                        morphName,
                        StringComparer.Ordinal);
                    return found
                        ? new MorphBinding(surface.Mesh, index)
                        : (MorphBinding?)null;
                })
                .Where(binding => binding is not null)
                .Select(binding => binding!.Value)
                .ToArray();
            if (bindings.Length == 0)
                throw new InvalidOperationException(
                    "Actor has no exact FaceGen morph binding for authored LIP target " +
                    $"{configuration.TargetNames[target]} -> {morphName}.");
            _bindings[target] = bindings;
        }
    }

    internal DominantSample Apply(FaceGenLipAnimation animation, double seconds)
    {
        if (!animation.TargetNames.SequenceEqual(
                _configuration.TargetNames,
                StringComparer.Ordinal))
            throw new InvalidOperationException(
                "FaceGen LIP target order differs from the runtime contract.");
        animation.Sample(seconds, _sample);
        var dominantTarget = "<neutral>";
        var dominantValue = 0.0f;
        for (var target = 0; target < _sample.Length; target++)
        {
            var value = _sample[target];
            foreach (var binding in _bindings[target])
                binding.Mesh.SetBlendShapeValue(binding.Index, value);
            if (_bindings[target].Count > 0 && MathF.Abs(value) > MathF.Abs(dominantValue))
            {
                dominantTarget = _configuration.TargetNames[target];
                dominantValue = value;
            }
        }
        return new DominantSample(dominantTarget, dominantValue);
    }

    internal void Clear()
    {
        Array.Clear(_sample);
        foreach (var bindings in _bindings)
            foreach (var binding in bindings)
                binding.Mesh.SetBlendShapeValue(binding.Index, 0.0f);
    }

    internal readonly record struct DominantSample(string Target, float Value);

    private readonly record struct MorphBinding(MeshInstance3D Mesh, int Index);
}
