using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    // Query volumes use the actual authored Havok shapes and the published bone
    // pose. They do not claim the absent character-controller/ragdoll dynamics.
    internal void ConfigureContactShapes(uint collisionLayer)
        => RuntimeNativeActorContacts.Configure(this, Skeleton, collisionLayer);
}

internal static class RuntimeNativeActorContacts
{
    internal static IReadOnlyList<Area3D> Configure(Node3D actor, RuntimeNativeNifSkeleton skeleton, uint collisionLayer)
    {
        var source = skeleton.Source;
        var created = new List<Node>();
        var contacts = new List<Area3D>();
        try
        {
            foreach (var block in source.Blocks.Where(block => block.TypeName is "NiNode" or "NiBone" or "BSFadeNode"))
            {
                var node = source.ReadNode(block.Index);
                if (node.CollisionObject < 0) continue;
                if (source.ReadObject(node.CollisionObject) is not FalloutNifCollisionObject collision || collision.Target != node.Block.Index)
                    throw new NotSupportedException("Actor contact attachment has no matching source bone.");
                var built = NativeNifCollisionBuilder.Build(source, collision, skeleton.UnitsToMetres, collisionLayer);
                try
                {
                    var attachment = new BoneAttachment3D { BoneIdx = skeleton.BoneIndex(node.Name), Name = $"ContactBone_{node.Block.Index}" };
                    created.Add(attachment);
                    var area = new Area3D
                    {
                        CollisionLayer = collisionLayer,
                        CollisionMask = 0,
                        Monitoring = false,
                        Monitorable = true,
                        Transform = built.Body.Transform,
                        Name = $"SourceContact_{collision.Block.Index}"
                    };
                    attachment.AddChild(area);
                    foreach (var shape in built.Body.GetChildren().OfType<CollisionShape3D>().ToArray())
                    {
                        built.Body.RemoveChild(shape);
                        area.AddChild(shape);
                    }
                    area.SetMeta("opennv_nif_collision_body", collision.Body);
                    area.SetMeta("opennv_nif_collision_bone", node.Name);
                    contacts.Add(area);
                }
                finally { built.Body.Free(); }
            }
            if (created.Count == 0) throw new NotSupportedException("Actor skeleton has no authored contact shapes.");
            foreach (var attachment in created) skeleton.Node.AddChild(attachment);
            actor.SetMeta("opennv_actor_contacts", "posed-owned-havok-query-shapes; controller-filter-and-ragdoll-dynamics-unverified");
            return contacts;
        }
        catch
        {
            foreach (var attachment in created) attachment.Free();
            throw;
        }
    }
}
