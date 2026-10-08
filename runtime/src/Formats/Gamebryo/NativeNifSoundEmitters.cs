using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Actor source NiNodes are bones rather than independently transformed Godot
// nodes. A sound attachment reads the same real pose as the skin/contact owners.
internal static class RuntimeNativeNifSoundEmitters
{
    internal static FalloutAnimationSoundBoneEmitterSnapshot? CaptureBone(Node3D actor, Node3D emitter,
        FalloutFormKey? reference)
    {
        RequireNoAnimationObject(actor, emitter);
        if (emitter is not BoneAttachment3D bone || !bone.HasMeta("opennv_nif_sound_bone_emitter")) return null;
        if (reference is not { } owner)
            throw new NotSupportedException("Source sound bone has no original reference history owner.");
        var skeleton = PrimarySkeleton(actor, owner);
        var index = bone.BoneIdx;
        if (index < 0 || index >= skeleton.GetBoneCount() || !skeleton.HasBoneMeta(index, "opennv_nif_block"))
            throw new NotSupportedException("Source sound bone lost its original primary skeleton block.");
        var snapshot = new FalloutAnimationSoundBoneEmitterSnapshot(owner,
            FalloutBsaArchive.CanonicalPath(skeleton.GetMeta("opennv_source_model", "").AsString()),
            skeleton.GetMeta("opennv_nif_source_sha256", "").AsString(),
            skeleton.GetBoneMeta(index, "opennv_nif_block").AsInt32(), skeleton.GetBoneName(index).ToString());
        snapshot.Validate(owner);
        RequireBoneBinding(bone, skeleton, snapshot);
        return snapshot;
    }

    internal static Node3D RestoreBone(Node3D actor, FalloutFormKey reference,
        FalloutAnimationSoundBoneEmitterSnapshot snapshot, Dictionary<string, Node3D> cache)
    {
        snapshot.Validate(reference);
        var skeleton = PrimarySkeleton(actor, reference);
        if (FalloutBsaArchive.CanonicalPath(skeleton.GetMeta("opennv_source_model", "").AsString()) != snapshot.SkeletonPath ||
            !skeleton.GetMeta("opennv_nif_source_sha256", "").AsString().Equals(snapshot.SkeletonSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cold sound bone differs from its winning source skeleton.");
        var matching = Enumerable.Range(0, skeleton.GetBoneCount()).Where(index =>
            skeleton.HasBoneMeta(index, "opennv_nif_block") &&
            skeleton.GetBoneMeta(index, "opennv_nif_block").AsInt32() == snapshot.Block).ToArray();
        if (matching.Length != 1 || skeleton.GetBoneName(matching[0]).ToString() != snapshot.Name)
            throw new InvalidDataException("Cold sound bone has no unique original source block and name.");
        // The real source skeleton and pose already exist. Recreate only the
        // canonical adapter; resolving another key would replay source work.
        var bone = BindBone(skeleton, matching[0], cache);
        RequireNoAnimationObject(actor, bone);
        RequireBoneBinding((BoneAttachment3D)bone, skeleton, snapshot);
        return bone;
    }

    private static Skeleton3D PrimarySkeleton(Node3D actor, FalloutFormKey reference)
    {
        var skeleton = actor.HasMeta("opennv_nif_actor_skeleton")
            ? actor.GetNodeOrNull<Skeleton3D>(actor.GetMeta("opennv_nif_actor_skeleton").AsNodePath()) : null;
        if (actor.GetMeta("opennv_reference_form_key", "").AsString() != reference.ToString() ||
            skeleton is null || skeleton.GetParent() != actor || !actor.HasMeta("opennv_nif_fixed_strings") ||
            !skeleton.HasMeta("opennv_nif_fixed_strings") || !skeleton.HasMeta("opennv_nif_source_root_blocks"))
            throw new NotSupportedException("Source sound bone has no complete original reference/skeleton owner.");
        return skeleton;
    }

    private static void RequireBoneBinding(BoneAttachment3D bone, Skeleton3D skeleton,
        FalloutAnimationSoundBoneEmitterSnapshot snapshot)
    {
        if (bone.OverridePose || bone.GetSkeleton() != skeleton ||
            bone.GetMeta("opennv_nif_block", -1).AsInt32() != snapshot.Block ||
            bone.GetMeta("opennv_nif_source_name", "").AsString() != snapshot.Name ||
            skeleton.GetChildren().OfType<BoneAttachment3D>().Count(node =>
                node.HasMeta("opennv_nif_sound_bone_emitter") && node.BoneIdx == bone.BoneIdx) != 1)
            throw new InvalidDataException("Source sound bone adapter differs from its actual posed block.");
    }

    internal static void RequireNoAnimationObject(Node3D actor, Node3D emitter)
    {
        for (Node? node = emitter; node is not null; node = node.GetParent())
        {
            if (node.HasMeta("opennv_animation_object_form"))
                throw new NotSupportedException("Source ANIO sound emitter requires its independent object/animation continuation.");
            if (node == actor) break;
        }
    }

    internal static bool HasAnonymousPath(string path) => path.Split('/').Any(part => part.StartsWith('@'));

    internal static (Node3D Emitter, bool FollowEmitter) Resolve(Node3D actor, string name,
        bool sourceLoop, Dictionary<string, Node3D> cache)
    {
        if (name.Length == 0) name = "AttachSound";
        var all = actor.FindChildren("*", "", true, false).OfType<Node3D>().Prepend(actor).ToArray();
        var skeleton = actor.HasMeta("opennv_nif_actor_skeleton")
            ? actor.GetNodeOrNull<Skeleton3D>(actor.GetMeta("opennv_nif_actor_skeleton").AsNodePath()) : null;
        if (actor.HasMeta("opennv_nif_actor_skeleton") && (skeleton is null ||
            !skeleton.HasMeta("opennv_nif_fixed_strings") || !skeleton.HasMeta("opennv_nif_source_root_blocks")))
            throw new NotSupportedException("Actor sound has no actual source skeleton scene owner.");
        var bone = skeleton?.FindBone(name) ?? -1;
        var nodes = all.Where(node => node.GetMeta("opennv_nif_source_name", "").AsString() == name).ToArray();
        if (bone >= 0)
        {
            if (!skeleton!.HasBoneMeta(bone, "opennv_nif_block") ||
                !skeleton.GetMeta("opennv_nif_fixed_strings").AsStringArray().Contains(name, StringComparer.Ordinal))
                throw new NotSupportedException($"Source sound emitter {name} has no declared source bone identity.");
            if (nodes.Length == 1 && nodes[0] is BoneAttachment3D bound &&
                bound.HasMeta("opennv_nif_sound_bone_emitter") && bound.GetSkeleton() == skeleton && bound.BoneIdx == bone)
            {
                Refresh(bound); return (bound, true);
            }
            if (nodes.Length != 0)
                throw new NotSupportedException($"Source sound emitter {name} has both bone and native node owners.");
            return (BindBone(skeleton!, bone, cache), true);
        }
        if (nodes.Length == 1) { Refresh(nodes[0]); return (cache[name] = nodes[0], true); }
        if (nodes.Length != 0)
            throw new NotSupportedException($"Source sound emitter {name} has {nodes.Length} native node owners.");
        // All admitted models retain their own fixed-string domain, including
        // flattened hardware-skin trees. Source presence without a real adapter
        // remains different from a proven null GetObjectByName result.
        if (!actor.HasMeta("opennv_nif_fixed_strings") ||
            all.Any(node => node.HasMeta("opennv_source_model") && !node.HasMeta("opennv_nif_fixed_strings")) ||
            all.Where(node => node.HasMeta("opennv_nif_fixed_strings"))
                .Any(node => node.GetMeta("opennv_nif_fixed_strings").AsStringArray().Contains(name, StringComparer.Ordinal)))
            throw new NotSupportedException($"Source sound emitter {name} has no complete native source-name binding.");
        Node3D root = actor;
        if (skeleton is not null)
        {
            var roots = skeleton.GetMeta("opennv_nif_source_root_blocks").AsInt32Array();
            if (roots.Length != 1) throw new NotSupportedException("Actor sound Get3D requires one actual source scene root.");
            var candidates = Enumerable.Range(0, skeleton.GetBoneCount())
                .Where(index => skeleton.GetBoneMeta(index, "opennv_nif_block").AsInt32() == roots[0]).ToArray();
            if (candidates.Length != 1) throw new NotSupportedException("Actor sound Get3D has no unique source root bone.");
            root = BindBone(skeleton, candidates[0], cache);
        }
        // Only authored Loop substitutes/attaches the root after a null lookup.
        // A finite voice holds the root's position at the actual key dispatch.
        return (root, sourceLoop);
    }

    private static BoneAttachment3D BindBone(Skeleton3D skeleton, int bone, Dictionary<string, Node3D> cache)
    {
        var name = skeleton.GetBoneName(bone).ToString();
        if (cache.TryGetValue(name, out var known) && GodotObject.IsInstanceValid(known) &&
            known is BoneAttachment3D existing && existing.GetSkeleton() == skeleton && existing.BoneIdx == bone)
        { Refresh(existing); return existing; }
        var owned = skeleton.GetChildren().OfType<BoneAttachment3D>()
            .Where(node => node.HasMeta("opennv_nif_sound_bone_emitter") && node.BoneIdx == bone).ToArray();
        if (owned.Length > 1) throw new InvalidDataException("Source sound bone has multiple native adapters.");
        if (owned.Length == 1) { Refresh(owned[0]); cache[name] = owned[0]; return owned[0]; }
        var block = skeleton.GetBoneMeta(bone, "opennv_nif_block").AsInt32();
        var node = new BoneAttachment3D { Name = $"SourceSoundBone_{block}", BoneIdx = bone, OverridePose = false };
        node.SetMeta("opennv_nif_sound_bone_emitter", true);
        node.SetMeta("opennv_nif_source_name", name);
        node.SetMeta("opennv_nif_block", block);
        try { skeleton.AddChild(node); Refresh(node); cache[name] = node; return node; }
        catch { node.Free(); throw; }
    }

    internal static void Refresh(Node3D emitter)
    {
        if (emitter is BoneAttachment3D bone && bone.HasMeta("opennv_nif_sound_bone_emitter"))
            bone.OnSkeletonUpdate();
    }
}
