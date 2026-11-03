using Godot;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    // Explicit, read-only inspection of one resident actor. Never part of the
    // periodic publication or a producer of gameplay/render state.
    private object CaptureReferenceRender(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("A source reference is required.");
        var actor = GetTree().Root.FindChildren("*", "Node3D", true, false).OfType<Node3D>()
            .Single(node => (node is RuntimeNativeNpc or RuntimeNativeCreature) &&
                node.GetMeta("opennv_reference_form_key", "").AsString() == reference);
        return new
        {
            schema = "opennv-reference-render-observation/v1",
            scope = "reference",
            process = System.Environment.ProcessId,
            reference,
            combat = actor is RuntimeNativeNpc npc ? npc.Combat?.Observation : ((RuntimeNativeCreature)actor).Combat?.Observation,
            lastIdleRequest = actor is RuntimeNativeCreature creature ? creature.LastIdleRequestObservation : null,
            path = actor.GetPath().ToString(),
            visible = actor.Visible,
            visibleInTree = actor.IsVisibleInTree(),
            transform = RenderTransform(actor.GlobalTransform),
            meshes = actor.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Select(mesh => new
            {
                path = mesh.GetPath().ToString(),
                visible = mesh.Visible,
                visibleInTree = mesh.IsVisibleInTree(),
                layers = mesh.Layers,
                transform = RenderTransform(mesh.GlobalTransform),
                localBounds = RenderBounds(mesh.GetAabb()),
                customBounds = RenderBounds(mesh.CustomAabb),
                extraCullMargin = mesh.ExtraCullMargin,
                surfaceCount = mesh.Mesh?.GetSurfaceCount(),
                bodyPart = mesh.HasMeta("opennv_nif_body_part") ? (int?)mesh.GetMeta("opennv_nif_body_part").AsInt32() : null,
                skinBindCount = mesh.Skin?.GetBindCount(),
                skeletonPath = mesh.Skeleton.ToString(),
                skeleton = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton) is { } skeleton ? new
                {
                    path = skeleton.GetPath().ToString(),
                    boneCount = skeleton.GetBoneCount(),
                    transform = RenderTransform(skeleton.GlobalTransform)
                } : null
            }).ToArray()
        };
    }

    private static float[] RenderVector(Vector3 value) => [value.X, value.Y, value.Z];
    private static object RenderTransform(Transform3D value) => new
    {
        position = RenderVector(value.Origin),
        x = RenderVector(value.Basis.X),
        y = RenderVector(value.Basis.Y),
        z = RenderVector(value.Basis.Z)
    };
    private static object RenderBounds(Aabb value) => new { position = RenderVector(value.Position), size = RenderVector(value.Size) };
}
