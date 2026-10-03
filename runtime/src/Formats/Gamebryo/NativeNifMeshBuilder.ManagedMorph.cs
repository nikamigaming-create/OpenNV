using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        private readonly Dictionary<int, (FalloutNifMorphGeometry Source, MeshInstance3D Mesh)> _managedMorphs = [];

        private FalloutNifMorphGeometry? ReadManagedMorph(FalloutNifGeometry geometry)
        {
            if (geometry.Controller < 0 || ExternalControllerBlocks?.Contains(geometry.Controller) == true ||
                _source.Blocks[geometry.Controller].TypeName != "NiGeomMorpherController") return null;
            var morph = new FalloutNifMorphGeometry(_source, geometry);
            if (MorphOwner is not null || geometry.SkinInstance >= 0 ||
                (morph.Controller.Time.Flags & 0x60) != 0x60 || morph.Controller.Time.UnknownInteger != 0 ||
                morph.Controller.AlwaysUpdate > 1 ||
                morph.Controller.Weights.Any(weight => weight.Interpolator < 0 ||
                    _source.ReadObject(weight.Interpolator) is not FalloutNifBlendFloatInterpolator { Flags: 1 }))
                throw new NotSupportedException("Managed geometry morph has additional unowned skin, clock or blend inputs.");
            return morph;
        }

        private void RegisterManagedMorph(FalloutNifGeometry geometry, FalloutNifMorphGeometry morph, MeshInstance3D mesh)
        {
            if (!_managedMorphs.TryAdd(geometry.Block.Index, (morph, mesh)))
                throw new InvalidDataException("Managed morph geometry has another instance owner.");
            for (var index = 1; index < morph.Data.Morphs.Length; index++)
                mesh.SetBlendShapeValue(index - 1, morph.EffectiveWeight(index, morph.Controller.Weights[index].Weight));
            mesh.SetMeta("opennv_nif_morph_controller", morph.Controller.Block.Index);
            mesh.SetMeta("opennv_nif_morph_targets", morph.Data.Morphs.Select(value => value.Name).ToArray());
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
                if (index > 0) owner.Mesh.SetBlendShapeValue(index - 1, value);
            });
        }
    }
}
