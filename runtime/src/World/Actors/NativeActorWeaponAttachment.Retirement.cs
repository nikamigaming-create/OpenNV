using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class NativeActorWeaponAttachment
{
    internal static FalloutInventoryChangeLease PrepareRetirement(IReadOnlyList<NativeActorWeaponAttachment> attachments)
    {
        foreach (var attachment in attachments) attachment.RequirePersistenceBoundary();
        var roots = attachments.SelectMany(attachment => new[] { attachment.Attachment }.Concat(attachment._bodyAttachments)).Select(root =>
        {
            var parent = root.GetParent() ?? throw new NotSupportedException("Presented equipment has no native attachment parent.");
            return (Root: root, Parent: parent, Index: root.GetIndex(), Pose: root.Transform);
        }).ToArray();
        return new(() =>
        {
            foreach (var entry in roots)
                if (!GodotObject.IsInstanceValid(entry.Root) || entry.Root.GetParent() != entry.Parent)
                    throw new InvalidOperationException("Presented equipment changed its original parent before transfer commit.");
            foreach (var entry in roots.OrderByDescending(entry => entry.Index)) entry.Parent.RemoveChild(entry.Root);
        }, () =>
        {
            foreach (var entry in roots.OrderBy(entry => entry.Index))
            {
                if (entry.Root.GetParent() == entry.Parent) continue;
                entry.Parent.AddChild(entry.Root);
                entry.Parent.MoveChild(entry.Root, entry.Index);
                entry.Root.Transform = entry.Pose;
            }
        }, () =>
        {
            foreach (var entry in roots)
                if (GodotObject.IsInstanceValid(entry.Root)) entry.Root.Free();
        });
    }
}
