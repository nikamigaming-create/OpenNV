using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed class RuntimeNativeNifSkeleton
{
    private readonly Dictionary<string, int> _boneIndices = new(StringComparer.Ordinal);
    private readonly List<(int ParentBone, FalloutNifGeometry Geometry)> _geometryAttachments = [];
    private sealed record Attachment(int ParentBone, FalloutNifGeometry Source, MeshInstance3D Mesh,
        BoneAttachment3D Parent, FalloutNifMorphGeometry? Morph);
    private readonly Dictionary<string, Attachment> _attachments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _visibility = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Node, int Index), float> _morphWeights = [];

    internal RuntimeNativeNifSkeleton(FalloutNifFile source, float unitsToMetres)
    {
        Source = source;
        UnitsToMetres = unitsToMetres;
        Node = new Skeleton3D { Name = "NativeSkeleton" };
        try
        {
            var visited = new HashSet<int>();
            foreach (var root in source.Roots)
                AddBone(root, -1, visited);
            Node.ResetBonePoses();
            BuildAttachments();
            Node.SetMeta("opennv_nif_source_bones", _boneIndices.Count);
            Node.SetMeta("opennv_nif_controller_owner", "external-gameplay-animation");
            Node.SetMeta("opennv_nif_geometry_attachments",
                _geometryAttachments.Select(value => value.Geometry.Block.Index).ToArray());
        }
        catch
        {
            Node.Free();
            throw;
        }
    }

    internal FalloutNifFile Source { get; }
    internal Skeleton3D Node { get; }
    internal float UnitsToMetres { get; }
    internal FalloutNifFloatExtraDataState FloatExtraData { get; } = new();
    internal IReadOnlyList<(int ParentBone, FalloutNifGeometry Geometry)> GeometryAttachments => _geometryAttachments;
    internal object VisualChannelState => new
    {
        visibility = _visibility.Select(row => new { node = row.Key, visible = row.Value }).ToArray(),
        morphs = _morphWeights.Select(row => new { node = row.Key.Node, target = row.Key.Index, weight = row.Value }).ToArray(),
        surfaces = _attachments.Count,
    };

    internal bool HasSourceTarget(string name) => _boneIndices.ContainsKey(name) || _attachments.ContainsKey(name);

    internal Action<float>? BindVisualChannel(FalloutNifFile source, FalloutNifControllerLink link)
    {
        if (link.PropertyType.Length != 0 || link.Variable1.Length != 0) return null;
        if (link.ControllerType == "NiVisController" && link.Variable2.Length == 0 && HasSourceTarget(link.NodeName))
        {
            var block = _boneIndices.TryGetValue(link.NodeName, out var bone)
                ? Node.GetBoneMeta(bone, "opennv_nif_block").AsInt32() : _attachments[link.NodeName].Source.Block.Index;
            var controllers = Source.Blocks.Where(value => value.TypeName == "NiVisController")
                .Select(value => (FalloutNifVisibilityController)Source.ReadObject(value.Index)).Where(value => value.Time.Target == block).ToArray();
            if (controllers.Length != 1) throw new InvalidDataException("Visibility channel has no unique declared source controller.");
            var sampler = new FalloutNifBoolAnimation(source, link.Interpolator);
            return time => { _visibility[link.NodeName] = sampler.Sample(time); RefreshVisibility(); };
        }
        if (link.ControllerType == "NiGeomMorpherController" && _attachments.TryGetValue(link.NodeName, out var attachment) && attachment.Morph is { } morph)
        {
            var index = morph.Index(link.Variable2);
            var sampler = new FalloutNifFloatAnimation(source, link.Interpolator);
            return time =>
            {
                var value = morph.EffectiveWeight(index, sampler.Sample(time));
                _morphWeights[(link.NodeName, index)] = value;
                if (index > 0) attachment.Mesh.SetBlendShapeValue(index - 1, value);
            };
        }
        return null;
    }

    private void BuildAttachments()
    {
        foreach (var (parentBone, geometry) in _geometryAttachments)
        {
            if (_boneIndices.ContainsKey(geometry.Name) || _attachments.ContainsKey(geometry.Name))
                throw new InvalidDataException("Skeleton geometry has an ambiguous source name.");
            var morph = Source.Blocks.Any(block => block.TypeName == "NiGeomMorpherController" &&
                ((FalloutNifMorphController)Source.ReadObject(block.Index)).Time.Target == geometry.Block.Index)
                ? new FalloutNifMorphGeometry(Source, geometry) : null;
            var parent = new BoneAttachment3D { Name = $"SourceGeometry_{geometry.Block.Index}", BoneIdx = parentBone };
            Node.AddChild(parent);
            var mesh = RuntimeNativeNifMeshBuilder.BuildSkeletonAttachment(this, geometry, morph);
            parent.AddChild(mesh);
            _attachments.Add(geometry.Name, new(parentBone, geometry, mesh, parent, morph));
            _visibility.Add(geometry.Name, (geometry.Flags & 1) == 0);
            if (morph is not null)
                for (var index = 0; index < morph.Data.Morphs.Length; index++)
                {
                    var input = morph.Controller.Weights[index];
                    var weight = morph.EffectiveWeight(index, input.Interpolator < 0 ? input.Weight :
                        new FalloutNifFloatAnimation(Source, input.Interpolator).Sample(morph.Controller.Time.StartTime));
                    _morphWeights.Add((geometry.Name, index), weight);
                    if (index > 0) mesh.SetBlendShapeValue(index - 1, weight);
                }
        }
        foreach (var block in Source.Blocks.Where(block => block.TypeName == "NiVisController"))
        {
            var controller = (FalloutNifVisibilityController)Source.ReadObject(block.Index);
            var name = Source.ReadObject(controller.Time.Target) switch { FalloutNifNode node => node.Name, FalloutNifGeometry mesh => mesh.Name, _ => "" };
            if (!_visibility.ContainsKey(name) || controller.Interpolator < 0 || (controller.Time.Flags & 8) == 0) continue;
            _visibility[name] = new FalloutNifBoolAnimation(Source, controller.Interpolator).Sample(controller.Time.StartTime);
        }
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        foreach (var attachment in _attachments.Values)
        {
            var visible = _visibility[attachment.Source.Name];
            for (var bone = attachment.ParentBone; visible && bone >= 0; bone = Node.GetBoneParent(bone))
                visible &= _visibility[Node.GetBoneName(bone).ToString()];
            attachment.Parent.Visible = visible;
        }
    }

    internal int BoneIndex(string sourceName) => _boneIndices.TryGetValue(sourceName, out var index)
        ? index : throw new InvalidDataException($"Actor skeleton has no source bone '{sourceName}'.");
    internal bool TryBoneIndex(string sourceName, out int index) => _boneIndices.TryGetValue(sourceName, out index);

    internal Transform3D Convert(FalloutNifTransform source) => new(
        GamebryoCoordinate.ConvertBasis(source.RotationRowMajor, source.Scale, "NIF skin transform"),
        GamebryoCoordinate.ConvertVector(new Vector3(source.Translation.X,
            source.Translation.Y, source.Translation.Z)) * UnitsToMetres);

    private void AddBone(int blockIndex, int parentIndex, HashSet<int> visited)
    {
        if (!visited.Add(blockIndex))
            throw new InvalidDataException("NIF skeleton contains a cycle or multiply owned bone.");
        if (Source.ReadObject(blockIndex) is FalloutNifGeometry geometry)
        {
            if (parentIndex < 0)
                throw new InvalidDataException("NIF skeleton geometry has no parent bone.");
            _geometryAttachments.Add((parentIndex, geometry));
            return;
        }
        var source = Source.ReadNode(blockIndex);
        if (string.IsNullOrWhiteSpace(source.Name) || !_boneIndices.TryAdd(source.Name, _boneIndices.Count))
            throw new InvalidDataException("NIF skeleton has an unnamed or duplicate bone identity.");
        var index = Node.AddBone(source.Name);
        _visibility.Add(source.Name, (source.Flags & 1) == 0);
        if (parentIndex >= 0)
            Node.SetBoneParent(index, parentIndex);
        Node.SetBoneRest(index, Convert(source.Transform));
        Node.SetBoneMeta(index, "opennv_nif_block", blockIndex);
        Node.SetBoneMeta(index, "opennv_nif_controller", source.Controller);
        Node.SetBoneMeta(index, "opennv_nif_collision", source.CollisionObject);
        Node.SetBoneMeta(index, "opennv_nif_flags", source.Flags);
        foreach (var extra in source.ExtraData.Where(reference => reference >= 0)
            .Select(Source.ReadObject).OfType<FalloutNifFloatExtraData>())
            FloatExtraData.Add(source.Name, extra.Name, extra.Value);
        foreach (var child in source.Children.Where(child => child >= 0))
            AddBone(child, index, visited);
    }
}
