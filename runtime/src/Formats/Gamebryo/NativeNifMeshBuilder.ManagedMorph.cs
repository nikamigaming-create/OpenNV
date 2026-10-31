using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        private readonly Dictionary<int, (FalloutNifMorphGeometry Source, MeshInstance3D[] Meshes)> _managedMorphs = [];

        private FalloutNifMorphGeometry? ReadManagedMorph(FalloutNifGeometry geometry)
        {
            if (geometry.Controller < 0 || ExternalControllerBlocks?.Contains(geometry.Controller) == true ||
                _source.Blocks[geometry.Controller].TypeName != "NiGeomMorpherController") return null;
            var morph = new FalloutNifMorphGeometry(_source, geometry);
            if (MorphOwner is not null || morph.Controller.Time.UnknownInteger != 0 || morph.Controller.AlwaysUpdate > 1)
                throw new NotSupportedException("Geometry morph has another vertex owner or unknown controller fields.");
            if ((morph.Controller.Time.Flags & 0x20) != 0)
            {
                if ((morph.Controller.Time.Flags & 0x60) != 0x60 || morph.Controller.Weights.Any(weight =>
                    weight.Interpolator < 0 || _source.ReadObject(weight.Interpolator) is not FalloutNifBlendFloatInterpolator { Flags: 1 }))
                    throw new NotSupportedException("Managed geometry morph requires its source blend inputs.");
            }
            else
            {
                FalloutNifControllerClock.Validate(morph.Controller.Time, requireActive: false);
                if ((morph.Controller.Time.Flags & 1) != 0)
                    throw new NotSupportedException("Direct geometry morph APP_INIT clock is not qualified.");
                if (morph.Controller.Weights.Any(weight => weight.Interpolator < 0 ||
                    _source.ReadObject(weight.Interpolator) is not FalloutNifFloatInterpolator))
                    throw new NotSupportedException("Direct geometry morph requires source scalar interpolators.");
            }
            return morph;
        }

        private void RegisterManagedMorph(FalloutNifGeometry geometry, FalloutNifMorphGeometry morph, MeshInstance3D mesh)
            => RegisterGeometryMorph(geometry, morph, [mesh]);

        private void RegisterGeometryMorph(FalloutNifGeometry geometry, FalloutNifMorphGeometry morph, MeshInstance3D[] meshes)
        {
            if (!_managedMorphs.TryAdd(geometry.Block.Index, (morph, meshes)))
                throw new InvalidDataException("Managed morph geometry has another instance owner.");
            foreach (var mesh in meshes)
            {
                for (var index = 1; index < morph.Data.Morphs.Length; index++)
                    mesh.SetBlendShapeValue(index - 1, morph.EffectiveWeight(index, morph.Controller.Weights[index].Weight));
                mesh.SetMeta("opennv_nif_morph_controller", morph.Controller.Block.Index);
                mesh.SetMeta("opennv_nif_morph_targets", morph.Data.Morphs.Select(value => value.Name).ToArray());
            }
            if ((morph.Controller.Time.Flags & 0x20) != 0) return;
            var samplers = morph.Controller.Weights.Select(weight => new FalloutNifFloatAnimation(_source, weight.Interpolator)).ToArray();
            _directControllerSequences.Add(new RuntimeNifControllerSequence(
                $"DirectMorph{morph.Controller.Block.Index}", (uint)(morph.Controller.Time.Flags >> 1) & 3U,
                morph.Controller.Time.Frequency, morph.Controller.Time.StartTime, morph.Controller.Time.StopTime,
                [new RuntimeNifControllerChannel(time =>
                {
                    for (var index = 1; index < samplers.Length; index++)
                    {
                        var weight = morph.EffectiveWeight(index, samplers[index].Sample(time));
                        foreach (var mesh in meshes) mesh.SetBlendShapeValue(index - 1, weight);
                    }
                })])
            { DirectClock = morph.Controller.Time, ApplicationTime = RuntimeNifApplicationClock.Read });
        }

        private RuntimeNifControllerChannel BuildManagedMorphChannel(FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link, int target)
        {
            if (link.PropertyType.Length != 0 || link.Variable1.Length != 0 ||
                !_managedMorphs.TryGetValue(target, out var owner) || link.Controller != owner.Source.Controller.Block.Index)
                throw new NotSupportedException($"Managed morph {sequence.Name}/{link.NodeName} has no declared geometry owner.");
            var index = owner.Source.Index(link.Variable2);
            var sampler = new FalloutNifFloatAnimation(_source, link.Interpolator);
            return new(time =>
            {
                var value = owner.Source.EffectiveWeight(index, sampler.Sample(time));
                if (index > 0)
                    foreach (var mesh in owner.Meshes) mesh.SetBlendShapeValue(index - 1, value);
            });
        }
    }
}
