using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    // The ordinary builder remains the fallback for hierarchies with owners
    // whose construction depends on recursive child order. No partial tree is
    // returned or attached to gameplay.
    internal sealed class StaticAssembly : IDisposable
    {
        private readonly Queue<(Node3D Parent, int Block, bool OmitMarkers)> _pending = [];
        private readonly BuildState _state;
        private Node3D? _root;
        private bool _complete;

        private StaticAssembly(FalloutNifFile source, float units, RuntimeLiveContentSource content)
        {
            _state = new(source, units, null, contentSource: content)
            { DeferStaticChild = (parent, block, markers) => _pending.Enqueue((parent, block, markers)) };
            _root = new Node3D { Name = "NativeNif" };
            _root.SetMeta("opennv_nif_fixed_strings", source.Strings.ToArray());
            foreach (var block in source.Roots) _pending.Enqueue((_root, block, false));
        }

        internal static StaticAssembly? TryCreate(FalloutNifFile source, float units, RuntimeLiveContentSource content)
            => TryCreate(source, units, content, out _);

        internal static StaticAssembly? TryCreate(FalloutNifFile source, float units, RuntimeLiveContentSource content, out string? refusal)
        {
            refusal = null;
            ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(content);
            if (!float.IsFinite(units) || units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
            var particleCount = 0;
            foreach (var block in source.Blocks)
            {
                if (block.TypeName is "NiNode" or "NiBone" or "BSFadeNode" or "BSMultiBoundNode" or
                    "BSRangeNode" or "BSBlastNode" or "BSDamageStage" or "BSValueNode" or "BSOrderedNode")
                {
                    var node = source.ReadNode(block.Index);
                    if (node.Controller != -1 || node.ParticleMaster is not null || node.SortBound is not null ||
                        node.Value is not null && node.Children.Any(child => child != -1) || node.Billboard is not null)
                    { refusal = $"Node {block.Index} has order-dependent owners."; return null; }
                }
                else if (block.TypeName is "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape")
                {
                    var geometry = source.ReadGeometry(block.Index);
                    if (geometry.Controller != -1 || geometry.SkinInstance != -1)
                    { refusal = $"Geometry {block.Index} has a controller or skin."; return null; }
                }
                else if (block.TypeName == "NiParticleSystem")
                {
                    // Its runtime owner is configured only after every source
                    // node has been staged. Multiple systems can depend on
                    // construction order and remain on the ordinary path.
                    if (++particleCount > 1) { refusal = "Multiple particle owners require source ordering."; return null; }
                }
                else if (block.TypeName.EndsWith("Node", StringComparison.Ordinal) || block.TypeName == "BSMasterParticleSystem")
                { refusal = $"Block {block.Index} type {block.TypeName} needs its hierarchy owner."; return null; }
            }
            return new(source, units, content);
        }

        internal bool Advance()
        {
            ObjectDisposedException.ThrowIf(_root is null, this);
            if (_complete) return true;
            if (_pending.TryDequeue(out var next))
            {
                var child = _state.BuildStaticChild(next.Block, next.OmitMarkers);
                try { next.Parent.AddChild(child); }
                catch { if (GodotObject.IsInstanceValid(child) && child.GetParent() is null) child.Free(); throw; }
            }
            if (_pending.Count != 0) return false;
            _state.BuildModelConstraints(_root); _state.BuildControllerPlayers(_root); _state.BindEmbeddedSkins();
            return _complete = true;
        }

        internal RuntimeNativeNifScene Take()
        {
            if (!_complete || _root is null) throw new InvalidOperationException("Static NIF assembly is incomplete.");
            var root = _root; _root = null;
            return new(root, _state.NodeCount, _state.SurfaceCount, _state.VertexCount, _state.TriangleCount,
                _state.CollisionBodyCount, _state.CollisionShapeCount, _state.CollisionTriangleCount);
        }

        public void Dispose() { _pending.Clear(); _root?.Free(); _root = null; }
    }
}
