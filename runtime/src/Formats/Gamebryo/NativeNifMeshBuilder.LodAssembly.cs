using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    internal enum TerrainStreamRole { Unknown, Terrain, OrdinaryWater, ReflectiveWater }

    // The terrain stream has separate terrain/water owners. This binding is
    // qualified for its single multibound container, not arbitrary property-free
    // meshes, geometry block numbers, names or flattened descendant order.
    internal static IReadOnlyDictionary<int, TerrainStreamRole> TerrainStreamRoles(FalloutNifFile source)
    {
        if (source.Roots.Count != 1 || source.Blocks[source.Roots[0]].TypeName != "BSMultiBoundNode") return new Dictionary<int, TerrainStreamRole>();
        IReadOnlyList<int> children = source.ReadNode(source.Roots[0]).Children;
        if (children.Count is < 1 or > 3 || children[0] < 0 ||
            children.Where(child => child >= 0).Distinct().Count() != children.Count(child => child >= 0) ||
            children.Any(child => child >= 0 && source.Blocks[child].TypeName is not ("NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape")))
            return new Dictionary<int, TerrainStreamRole>();
        var result = new Dictionary<int, TerrainStreamRole>();
        for (var slot = 0; slot < children.Count; ++slot)
            if (children[slot] >= 0)
                result.Add(children[slot], slot switch
                {
                    0 => TerrainStreamRole.Terrain,
                    1 => TerrainStreamRole.OrdinaryWater,
                    2 => TerrainStreamRole.ReflectiveWater,
                    _ => throw new InvalidOperationException("Unqualified terrain stream child slot.")
                });
        return result;
    }

    internal sealed class TerrainStreamCoverage(FalloutNifFile source)
    {
        private readonly IReadOnlyDictionary<int, TerrainStreamRole> _roles = TerrainStreamRoles(source);
        private readonly HashSet<int> _omitted = [];
        internal bool TerrainComplete { get; private set; } = true;
        internal bool OrdinaryWaterComplete { get; private set; } = true;
        internal bool ReflectiveWaterComplete { get; private set; } = true;
        internal int MissingOrdinaryWaterGeometries { get; private set; }
        internal int MissingReflectiveWaterGeometries { get; private set; }

        internal TerrainStreamRole Omit(int block)
        {
            var role = _roles.GetValueOrDefault(block);
            if (!_omitted.Add(block)) return role;
            switch (role)
            {
                case TerrainStreamRole.OrdinaryWater:
                    OrdinaryWaterComplete = false; ++MissingOrdinaryWaterGeometries;
                    break;
                case TerrainStreamRole.ReflectiveWater:
                    ReflectiveWaterComplete = false; ++MissingReflectiveWaterGeometries;
                    break;
                default:
                    TerrainComplete = false;
                    if (role == TerrainStreamRole.Unknown) OrdinaryWaterComplete = ReflectiveWaterComplete = false;
                    break;
            }
            return role;
        }
    }

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
