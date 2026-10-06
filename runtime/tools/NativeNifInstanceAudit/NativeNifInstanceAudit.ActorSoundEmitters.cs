using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseActorSoundEmitters()
    {
        foreach (var joint in new[] { "Joint", "AttachSound" })
        {
            var actor = new Node3D
            {
                Transform = new(new Basis(Vector3.Up, .7f).Scaled(Vector3.One * 1.2f), new(7, 4, -3))
            };
            var skeleton = NativeNifMeshBuilder.BuildActorSkeleton(ActorSkinFixture("ActorRoot", false, joint), 1);
            using var material = new StandardMaterial3D();
            actor.AddChild(skeleton.Node); AddChild(actor);
            try
            {
                skeleton.Node.Scale = Vector3.One * 1.3f;
                var source = FalloutNifFile.Read(ActorSkinFixture("EquipmentRoot", true, joint));
                var part = NativeNifMeshBuilder.AddActorPart(source, skeleton, materialOverride: (_, _) => material);
                part.Root.SetMeta("opennv_source_model", "synthetic/equipment.nif");
                skeleton.BindSoundSource(actor);
                var rootBone = skeleton.BoneIndex("ActorRoot"); var bone = skeleton.BoneIndex(joint);
                skeleton.Node.SetBonePosePosition(rootBone, new(2, 1, -4));
                skeleton.Node.SetBonePosePosition(bone, new(1, 3, 2));
                skeleton.Node.SetBonePoseRotation(bone, new(Vector3.Up, .3f));
                var before = Poses(); var placement = actor.Transform;
                var cache = new Dictionary<string, Node3D>(StringComparer.Ordinal);
                var named = RuntimeNativeNifSoundEmitters.Resolve(actor, joint, false, cache);
                var expected = skeleton.Node.GlobalTransform * skeleton.Node.GetBoneGlobalPose(bone);
                Require(named.FollowEmitter && named.Emitter is BoneAttachment3D { OverridePose: false } attachment &&
                    attachment.BoneIdx == bone && named.Emitter.GlobalTransform.IsEqualApprox(expected),
                    "Source bone emitter did not bind its actual same-frame pose.");
                skeleton.Node.SetBonePosePosition(bone, new(-2, 4, 5));
                var next = RuntimeNativeNifSoundEmitters.Resolve(actor, joint, false, cache);
                Require(next.Emitter == named.Emitter && next.Emitter.GlobalTransform.IsEqualApprox(
                    skeleton.Node.GlobalTransform * skeleton.Node.GetBoneGlobalPose(bone)),
                    "Source bone emitter reused a stale KF pose or duplicated its adapter.");
                var independent = RuntimeNativeNifSoundEmitters.Resolve(actor, joint, false,
                    new Dictionary<string, Node3D>(StringComparer.Ordinal));
                Require(independent.Emitter == named.Emitter, "Independent sound players duplicated one actual source bone.");
                skeleton.Node.SetBonePosePosition(bone, new(1, 3, 2));
                var finite = RuntimeNativeNifSoundEmitters.Resolve(actor, "SourceAbsent", false, cache);
                var loop = RuntimeNativeNifSoundEmitters.Resolve(actor, "SourceAbsent", true, cache);
                Require(!finite.FollowEmitter && loop.FollowEmitter && finite.Emitter == loop.Emitter &&
                    finite.Emitter is BoneAttachment3D root && root.BoneIdx == rootBone &&
                    finite.Emitter.GlobalTransform.IsEqualApprox(skeleton.Node.GlobalTransform * skeleton.Node.GetBoneGlobalPose(rootBone)),
                    "Null lookup did not retain the actual posed Get3D root and finite/Loop distinction.");
                var defaultEmitter = RuntimeNativeNifSoundEmitters.Resolve(actor, "", false, cache);
                Require(joint == "AttachSound" ? defaultEmitter.Emitter == named.Emitter && defaultEmitter.FollowEmitter :
                    defaultEmitter.Emitter == finite.Emitter && !defaultEmitter.FollowEmitter,
                    "Default AttachSound lookup guessed a bone or lost a declared source attachment.");
                Refuse("EquipmentRoot"); // Declared flattened part node, not an absent name.
                var domain = part.Root.GetMeta("opennv_nif_fixed_strings");
                part.Root.RemoveMeta("opennv_nif_fixed_strings");
                try { Refuse("SourceAbsent"); }
                finally { part.Root.SetMeta("opennv_nif_fixed_strings", domain); }
                var rootDomain = actor.GetMeta("opennv_nif_fixed_strings"); actor.RemoveMeta("opennv_nif_fixed_strings");
                try { Refuse("SourceAbsent"); }
                finally { actor.SetMeta("opennv_nif_fixed_strings", rootDomain); }
                var duplicate = new Node3D(); duplicate.SetMeta("opennv_nif_source_name", joint); actor.AddChild(duplicate);
                try { Refuse(joint); }
                finally { duplicate.Free(); }
                var equipped = NativeNifMeshBuilder.Build(ActorSkinFixture("EquippedRoot", false, "NamedAux"), 1);
                equipped.Root.SetMeta("opennv_source_model", "synthetic/auxiliary.nif"); actor.AddChild(equipped.Root);
                var auxiliary = RuntimeNativeNifSoundEmitters.Resolve(actor, "NamedAux", false, cache);
                Require(auxiliary.FollowEmitter, "Current equipped source name was absent from the actual scene.");
                equipped.Root.Free();
                var removed = RuntimeNativeNifSoundEmitters.Resolve(actor, "NamedAux", false, cache);
                Require(!removed.FollowEmitter && removed.Emitter == finite.Emitter &&
                    !GodotObject.IsInstanceValid(auxiliary.Emitter), "Removed equipment retained a stale source name or emitter.");
                Require(actor.Transform == placement && Poses().SequenceEqual(before) && part.Surfaces == 1,
                    "Sound binding changed source actor placement, raw poses or skin assembly.");

                Transform3D[] Poses() => Enumerable.Range(0, skeleton.Node.GetBoneCount()).Select(skeleton.Node.GetBonePose).ToArray();
                void Refuse(string name)
                {
                    try { _ = RuntimeNativeNifSoundEmitters.Resolve(actor, name, false, cache); }
                    catch (NotSupportedException) { return; }
                    throw new InvalidDataException("Unknown, unadapted or ambiguous source emitter was admitted.");
                }
            }
            finally { actor.Free(); }
        }
        GD.Print("OPENNV_NATIVE_ACTOR_SOUND_EMITTERS_PASS sourceBone=true sameFrame=true oneAdapter=true actualRoot=true finiteLoopDistinct=true unknownRefused=true sourcePoseUnchanged=true");
    }
}
