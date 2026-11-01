using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        private void BindAddon(Node3D node, FalloutNifNodeValue value, Node3D? presentationParent = null)
        {
            _ = NativeNifWorldAxes.Validate(value.Flags);
            var content = _contentSource ?? RuntimeLiveContentSource.Current ??
                throw new NotSupportedException("BSValueNode has no owned ADDN catalog.");
            var addon = FalloutAddonNodes.For(content).Get(value.Value);
            if (addon.Flags > 1) throw new NotSupportedException($"ADDN {addon.Form} configuration flags are unbound.");
            if (!AddonAncestors.Add(value.Value)) throw new InvalidDataException("ADDN model graph contains a cycle.");
            try
            {
                var model = NativeOwnedNifCache.Read(content, addon.Model, out var identity);
                var scene = RuntimeNativeNifMeshBuilder.Build(model, _unitsToMetres, contentSource: content, addonAncestors: AddonAncestors);
                (presentationParent ?? node).AddChild(scene.Root);
                node.SetMeta("opennv_addon_form", addon.Form.ToString());
                node.SetMeta("opennv_addon_index", value.Value);
                node.SetMeta("opennv_addon_resource", identity);
                node.SetMeta("opennv_addon_particle_cap", addon.ParticleCap);
                if (addon.Sound is { } sound)
                {
                    node.SetMeta("opennv_addon_sound_plugin", sound.OwnerPlugin);
                    node.SetMeta("opennv_addon_sound_object", sound.ObjectId);
                    node.SetMeta("opennv_addon_audio_unbound", sound.ToString());
                }
                NodeCount += scene.Nodes; SurfaceCount += scene.Surfaces; VertexCount += scene.Vertices; TriangleCount += scene.Triangles;
                CollisionBodyCount += scene.CollisionBodies; CollisionShapeCount += scene.CollisionShapes; CollisionTriangleCount += scene.CollisionTriangles;
            }
            finally { AddonAncestors.Remove(value.Value); }
        }
    }
}
