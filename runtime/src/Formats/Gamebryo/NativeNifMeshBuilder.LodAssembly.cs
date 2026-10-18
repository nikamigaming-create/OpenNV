using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    // Keep the ordinary material/geometry builder, but publish at most one
    // source node per step. A detached tile owns all partial native resources;
    // it cannot replace the previous complete cover until Take succeeds.
    internal sealed class LodAssembly : IDisposable
    {
        private readonly Queue<(Node3D Parent, int Block, bool OmitMarkers)> _pending = [];
        private readonly BuildState _state;
        private Node3D? _root;
        private bool _complete;

        internal LodAssembly(FalloutNifFile source, float units, RuntimeLiveContentSource content,
            Action<FalloutNifGeometry>? omitted)
        {
            if (!float.IsFinite(units) || units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
            // Deferred hierarchy construction is valid for static LOD only.
            // Controllers, skin/particle owners and addon dependencies require
            // their ordinary complete hierarchy and must never enter this lane.
            foreach (var block in source.Blocks)
            {
                if (block.TypeName is "NiNode" or "BSMultiBoundNode")
                {
                    var node = source.ReadNode(block.Index);
                    if (node.Controller != -1 || node.CollisionObject != -1)
                        throw new NotSupportedException("Distant hierarchy has an active controller or collision owner.");
                }
                else if (block.TypeName is "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape")
                {
                    var geometry = source.ReadGeometry(block.Index);
                    if (geometry.Controller != -1 || geometry.SkinInstance != -1 || geometry.CollisionObject != -1)
                        throw new NotSupportedException("Distant geometry has an active controller, skin or collision owner.");
                }
                else if (block.TypeName.EndsWith("Node", StringComparison.Ordinal) || block.TypeName == "NiParticleSystem")
                    throw new NotSupportedException($"Distant hierarchy requires a static owner for {block.TypeName}.");
            }
            _state = new(source, units, null, contentSource: content)
            {
                UnboundPropertyFreeLod = omitted,
                DeferStaticChild = (parent, block, markers) => _pending.Enqueue((parent, block, markers))
            };
            _root = new Node3D { Name = "NativeNif" };
            _root.SetMeta("opennv_nif_fixed_strings", source.Strings.ToArray());
            foreach (var block in source.Roots)
                if (omitted is not null && source.Blocks[block].TypeName is ("NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape") &&
                    source.ReadGeometry(block) is { } geometry && geometry.Properties.All(index => index == -1)) omitted(geometry);
                else _pending.Enqueue((_root, block, false));
        }

        internal bool Advance()
        {
            ObjectDisposedException.ThrowIf(_root is null, this);
            if (_complete) return true;
            if (_pending.TryDequeue(out var next))
                next.Parent.AddChild(_state.BuildStaticChild(next.Block, next.OmitMarkers));
            if (_pending.Count != 0) return false;
            _state.BuildModelConstraints(_root);
            _state.BuildControllerPlayers(_root);
            _state.BindEmbeddedSkins();
            return _complete = true;
        }

        internal RuntimeNativeNifScene Take()
        {
            if (!_complete || _root is null) throw new InvalidOperationException("Distant assembly is incomplete.");
            var root = _root; _root = null;
            return new(root, _state.NodeCount, _state.SurfaceCount, _state.VertexCount, _state.TriangleCount,
                _state.CollisionBodyCount, _state.CollisionShapeCount, _state.CollisionTriangleCount);
        }

        public void Dispose() { _pending.Clear(); _root?.Free(); _root = null; }
    }
}
