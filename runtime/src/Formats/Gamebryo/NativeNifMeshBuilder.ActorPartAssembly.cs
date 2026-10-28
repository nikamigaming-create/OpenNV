using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    // Skinned, non-Prn parts have no intermediate presentation owner: retain
    // the ordinary builder's depth-first source order until final publication.
    internal sealed class ActorPartAssembly : IDisposable
    {
        private readonly record struct Work(int Block, Transform3D Parent, bool Visible, HashSet<int> Visited, Action? Deferred = null, string? Stage = null);
        private readonly Stack<Work> _pending = new();
        private readonly BuildState _state;
        private readonly List<IDisposable> _partitionResources = [];
        private readonly FalloutNifFile _source;
        private readonly RuntimeNativeNifSkeleton _skeleton;
        private Node3D? _root;
        private bool _complete;
        internal string NextStage => _pending.TryPeek(out var next)
            ? next.Deferred is not null ? $"{next.Stage}:{next.Block}" : $"block:{next.Block}:{_source.Blocks[next.Block].TypeName}" : "finalize";

        private ActorPartAssembly(FalloutNifFile source, RuntimeNativeNifSkeleton skeleton,
            RuntimeLiveContentSource content, Func<FalloutNifFile, FalloutNifGeometry, Material?>? material,
            Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, FalloutNifMeshData>? geometry,
            string? selectedShape, IReadOnlySet<string>? externalTransformTargets,
            Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, IReadOnlyDictionary<string, System.Numerics.Vector3[]>>? morph,
            IReadOnlyDictionary<int, IReadOnlyList<FalloutNifHardwareSkinPartition>>? preparedPartitions)
        {
            _skeleton = skeleton; _source = source;
            _state = new(source, skeleton.UnitsToMetres, null, externalSkeleton: true, material, geometry, content)
            {
                SelectedGeometryName = selectedShape,
                MorphOwner = morph,
                ExternalTransformTargets = externalTransformTargets,
                PreparedHardwarePartitions = preparedPartitions
            };
            if (selectedShape is not null && source.Blocks
                .Where(block => block.TypeName is "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape")
                .Count(block => source.ReadGeometry(block.Index).Name.Equals(selectedShape, StringComparison.OrdinalIgnoreCase)) != 1)
                throw new InvalidDataException($"Source model has no unique equipped geometry named {selectedShape}.");
            _root = new Node3D { Name = $"NativePart{skeleton.Node.GetChildCount()}" };
            _root.SetMeta("opennv_nif_fixed_strings", source.Strings.ToArray());
            foreach (var block in source.Roots.Reverse())
                _pending.Push(new(block, Transform3D.Identity, true, []));
        }

        internal static ActorPartAssembly? TryCreate(FalloutNifFile source, RuntimeNativeNifSkeleton skeleton,
            RuntimeLiveContentSource content, Func<FalloutNifFile, FalloutNifGeometry, Material?>? material,
            Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, FalloutNifMeshData>? geometry,
            IReadOnlyDictionary<string, FalloutNifTransform>? rigidFaceBinds, string? selectedShape,
            Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, IReadOnlyDictionary<string, System.Numerics.Vector3[]>>? morph,
            uint bipedSlots, IReadOnlySet<string>? externalTransformTargets = null,
            IReadOnlyDictionary<int, IReadOnlyList<FalloutNifHardwareSkinPartition>>? preparedPartitions = null)
        {
            if (rigidFaceBinds is not null || FalloutNpcFaceAttachment.IsRigidHeadEquipment(bipedSlots)) return null;
            foreach (var block in source.Roots)
            {
                var root = source.ReadNode(block);
                if (root.ExtraData.Where(reference => reference >= 0).Select(source.ReadObject)
                    .OfType<FalloutNifStringExtraData>().Any(extra => extra.Name == "Prn")) return null;
            }
            if (preparedPartitions is not null && geometry is not null)
                throw new InvalidOperationException("Prepared creature partitions cannot be used with a geometry override.");
            return new(source, skeleton, content, material, geometry, selectedShape, externalTransformTargets, morph, preparedPartitions);
        }

        internal bool Advance()
        {
            ObjectDisposedException.ThrowIf(_root is null, this);
            if (_complete) return true;
            if (_pending.TryPop(out var next))
            {
                if (next.Deferred is { } deferred) deferred();
                else _state.BuildHardwareSkinTree(next.Block, next.Parent, next.Visible, _root, _skeleton, next.Visited,
                    (block, parent, visible) => _pending.Push(new(block, parent, visible, next.Visited)),
                    (stage, work) => _pending.Push(new(next.Block, next.Parent, next.Visible, next.Visited, work, stage)),
                    resource => _partitionResources.Add(resource));
                if (_pending.Count != 0) return false;
            }
            _state.BuildModelConstraints(_root);
            _state.BuildControllerPlayers(_root);
            _state.RegisterActorMaterialChannels(_skeleton.MaterialChannels, _root);
            if (_state.SurfaceCount == 0) throw new InvalidDataException("Actor part contains no presented source surfaces.");
            return _complete = true;
        }

        internal RuntimeNativeNifScene Take()
        {
            if (!_complete || _root is null) throw new InvalidOperationException("Actor part assembly is incomplete.");
            var root = _root;
            _skeleton.Node.AddChild(root);
            _root = null;
            foreach (var resource in _partitionResources) resource.Dispose();
            _partitionResources.Clear();
            return new(root, _state.NodeCount, _state.SurfaceCount, _state.VertexCount, _state.TriangleCount,
                _state.CollisionBodyCount, _state.CollisionShapeCount, _state.CollisionTriangleCount);
        }

        public void Dispose()
        {
            _pending.Clear();
            foreach (var resource in _partitionResources) resource.Dispose();
            _partitionResources.Clear();
            _root?.Free(); _root = null;
        }
    }
}
