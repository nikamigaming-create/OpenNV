using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record RuntimeNativeNifPointLightOwner(FalloutModeledLightSource Source,
    Func<Transform3D, RuntimeNativePlacedLight> Create);

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        internal RuntimeNativeNifPointLightOwner? PointLightOwner { get; init; }
        private readonly HashSet<int> _requiredLightChannels = [];
        private readonly HashSet<int> _boundLightChannels = [];

        private Node3D BuildOwnedPointLight(FalloutNifPointLight source)
        {
            var owner = PointLightOwner!;
            if (!ReferenceEquals(owner.Source.Model, _source) || owner.Source.Point.Block.Index != source.Block.Index)
                throw new InvalidDataException("Native point owner differs from its decoded source.");
            var controlled = new HashSet<int>();
            if (source.Light.Controller >= 0)
            {
                var links = _source.Blocks.Where(block => block.TypeName == "NiControllerSequence")
                    .SelectMany(block => _source.ReadControllerSequence(block.Index).ControlledBlocks)
                    .Where(link => link.Controller == source.Light.Controller).ToArray();
                if (links.Length == 0) throw new NotSupportedException("Source point controller has no managed sequence declaration.");
                foreach (var link in links) _ = new FalloutNifLightColorAnimation(_source, link, source.Block.Index);
                controlled.Add(source.Light.Controller);
                _requiredLightChannels.Add(source.Light.Controller);
            }
            RequirePlainVisualState(source.Block, source.Light.Controller, source.Light.ExtraData,
                source.Light.Properties, source.Light.CollisionObject, controlled);
            var result = owner.Create(ConvertTransform(source.Light.Transform));
            try
            {
                result.Name = SourceName(source.Light.Name, source.Block.Index);
                result.Visible = (source.Light.Flags & HiddenFlag) == 0;
                result.SetMeta("opennv_nif_block", source.Block.Index);
                result.SetMeta("opennv_nif_source_name", source.Light.Name);
                result.SetMeta("opennv_nif_flags", source.Light.Flags);
                result.SetMeta("opennv_nif_point_source_diffuse", new Vector3(source.Light.Diffuse.R, source.Light.Diffuse.G, source.Light.Diffuse.B));
                result.SetMeta("opennv_nif_point_source_dimmer", source.Light.Dimmer);
                result.SetMeta("opennv_ligh_current_ambient", Vector3.Zero);
                result.SetMeta("opennv_ligh_current_radius_vector", Vector3.One * result.GetMeta("opennv_ligh_radius_game_units").AsSingle());
                result.SetMeta("opennv_nif_point_attenuation", new Vector3(source.ConstantAttenuation,
                    source.LinearAttenuation, source.QuadraticAttenuation));
                result.SetMeta("opennv_nif_point_light_owner", "placed-LIGH");
                _nodes.Add(source.Block.Index, result);
                NodeCount++;
                return result;
            }
            catch (Exception primary)
            {
                try { result.Free(); }
                catch (Exception retirement) { throw new AggregateException("Point construction and detached retirement failed.", primary, retirement); }
                throw;
            }
        }

        private RuntimeNifControllerChannel BuildLightDiffuseChannel(FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link, int targetBlock, Node3D target)
        {
            if (PointLightOwner?.Source.Point.Block.Index != targetBlock || target is not RuntimeNativePlacedLight light)
                throw new NotSupportedException("Managed light diffuse has no selected placed point owner.");
            var curve = new FalloutNifLightColorAnimation(_source, link, targetBlock);
            _ = curve.Sample(sequence.StartTime); _ = curve.Sample(sequence.StopTime);
            _boundLightChannels.Add(link.Controller);
            return new(time => light.ApplySourceDiffuse(curve.Sample(time)), curve.Boundaries);
        }

        internal void RequireModeledLightChannels()
        {
            if (PointLightOwner is null) return;
            if (!_nodes.TryGetValue(PointLightOwner.Source.Point.Block.Index, out var node) || node is not RuntimeNativePlacedLight ||
                !_requiredLightChannels.IsSubsetOf(_boundLightChannels))
                throw new NotSupportedException("Modeled light has a point/channel without its actual native manager owner.");
        }

        private void RequireModeledTransformTarget(int targetBlock)
        {
            if (PointLightOwner is { Source.ReplaceNodeRootTransform: true } && targetBlock == _source.Roots[0])
                throw new NotSupportedException("Modeled light animates its placed source root without a reference/controller transform owner.");
        }
    }
}
