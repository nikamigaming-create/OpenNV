using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    // Deferred children are pushed in reverse source order, preserving the
    // synchronous builder's depth-first ownership and sibling order.
    internal sealed class ActorAttachmentAssembly : IDisposable
    {
        private readonly Stack<(Node3D Parent, int Block, bool OmitMarkers)> _pending = [];
        private readonly List<(Node3D Parent, int Block, bool OmitMarkers)> _children = [];
        private readonly FalloutNifFile _source;
        private readonly RuntimeNativeNifSkeleton _skeleton;
        private readonly BuildState _state;
        private Node3D? _root;
        private int _finalization;
        private BuildState.ParticleSetup? _particleSetup;
        private Material? _particleMaterial;
        private bool _complete;

        private ActorAttachmentAssembly(FalloutNifFile source, RuntimeNativeNifSkeleton skeleton,
            RuntimeLiveContentSource content, IReadOnlySet<string>? externalTargets, int rootBlock, string bone)
        {
            _source = source; _skeleton = skeleton;
            _state = new(source, skeleton.UnitsToMetres, null, externalSkeleton: true, contentSource: content)
            {
                ExternalTransformTargets = externalTargets,
                DeferStaticChild = (parent, block, markers) => _children.Add((parent, block, markers)),
            };
            _ = skeleton.BoneIndex(bone);
            _root = new Node3D { Name = $"NativePart{skeleton.Node.GetChildCount()}" };
            try
            {
                _root.SetMeta("opennv_nif_fixed_strings", source.Strings.ToArray());
                var attachment = new BoneAttachment3D
                {
                    Name = $"SourceAttachment{rootBlock}",
                    BoneName = bone,
                    UseExternalSkeleton = true,
                    ExternalSkeleton = new NodePath("../.."),
                };
                try
                {
                    attachment.SetMeta("opennv_nif_parent_bone", bone);
                    _root.AddChild(attachment);
                }
                catch
                {
                    if (GodotObject.IsInstanceValid(attachment) && attachment.GetParent() is null) attachment.Free();
                    throw;
                }
                _pending.Push((attachment, rootBlock, false));
            }
            catch { _root.Free(); _root = null; throw; }
        }

        internal static ActorAttachmentAssembly? TryCreate(FalloutNifFile source, RuntimeNativeNifSkeleton skeleton,
            RuntimeLiveContentSource content, IReadOnlySet<string>? externalTargets)
        {
            if (source.Roots.Count != 1) return null;
            var root = source.ReadNode(source.Roots[0]);
            var parents = root.ExtraData.Where(index => index >= 0).Select(source.ReadObject)
                .OfType<FalloutNifStringExtraData>().Where(value => value.Name == "Prn").Select(value => value.Value).ToArray();
            if (parents.Length != 1) return null;
            var particles = 0;
            try
            {
                foreach (var block in source.Blocks)
                {
                    if (block.TypeName is "NiNode" or "NiBone" or "BSFadeNode" or "BSMultiBoundNode" or "BSRangeNode" or
                        "BSBlastNode" or "BSDamageStage" or "BSValueNode" or "BSOrderedNode")
                    {
                        var node = source.ReadNode(block.Index);
                        if (node.ParticleMaster is not null ||
                            node.SortBound is not null || node.Value is not null || node.Billboard is not null ||
                            node.CollisionObject >= 0 || node.Effects.Any(value => value >= 0)) return null;
                        if (FalloutNifNodeControllerChain.Read(source, node).Any(value => value is not FalloutNifTransformController)) return null;
                    }
                    else if (block.TypeName is "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape")
                    {
                        var geometry = source.ReadGeometry(block.Index);
                        if (geometry.SkinInstance >= 0 || geometry.Controller >= 0 || geometry.CollisionObject >= 0) return null;
                    }
                    else if (block.TypeName == "NiParticleSystem") { if (++particles > 1) return null; }
                    else if (block.TypeName.EndsWith("Node", StringComparison.Ordinal) || block.TypeName == "BSMasterParticleSystem") return null;
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
            {
                // Let the ordinary builder decide whether a failed preflight
                // block is reachable and must reject this model.
                return null;
            }
            return new(source, skeleton, content, externalTargets, root.Block.Index, parents[0]);
        }

        internal string NextStage => _pending.TryPeek(out var next)
            ? $"block:{next.Block}:{_source.Blocks[next.Block].TypeName}" : $"finalize:{_finalization}";

        internal bool Advance()
        {
            ObjectDisposedException.ThrowIf(_root is null, this);
            if (_complete) return true;
            if (_pending.TryPop(out var next))
            {
                _children.Clear();
                var child = _state.BuildStaticChild(next.Block, next.OmitMarkers);
                try { next.Parent.AddChild(child); }
                catch { if (GodotObject.IsInstanceValid(child) && child.GetParent() is null) child.Free(); throw; }
                for (var index = _children.Count - 1; index >= 0; index--) _pending.Push(_children[index]);
                _children.Clear();
                return false;
            }
            switch (_finalization++)
            {
                case 0: _state.BuildModelConstraints(_root); return false;
                case 1: _particleSetup = _state.PrepareSingleParticleSystem(); return false;
                case 2:
                    if (_particleSetup is not null) _particleMaterial = _state.PrepareParticleMaterial(_particleSetup);
                    return false;
                case 3:
                    if (_particleSetup is not null) _state.BeginParticleSystem(_particleSetup, _particleMaterial!);
                    return false;
                case 4:
                    if (_particleSetup is not null) _state.FinishParticleSystem(_particleSetup);
                    _particleSetup = null; _particleMaterial = null;
                    return false;
                case 5: _state.BuildControllerPlayers(_root, particlesAlreadyConfigured: true); return false;
                case 6: _state.RegisterActorMaterialChannels(_skeleton.MaterialChannels, _root); return false;
                default:
                    if (_state.SurfaceCount == 0) throw new InvalidDataException("Actor attachment contains no presented source surfaces.");
                    return _complete = true;
            }
        }

        internal RuntimeNativeNifScene Take()
        {
            if (!_complete || _root is null) throw new InvalidOperationException("Actor attachment assembly is incomplete.");
            var root = _root;
            _skeleton.Node.AddChild(root);
            _root = null;
            return new(root, _state.NodeCount, _state.SurfaceCount, _state.VertexCount, _state.TriangleCount,
                _state.CollisionBodyCount, _state.CollisionShapeCount, _state.CollisionTriangleCount);
        }

        public void Dispose() { _pending.Clear(); _children.Clear(); _particleSetup = null; _particleMaterial = null; _root?.Free(); _root = null; }
    }
}
