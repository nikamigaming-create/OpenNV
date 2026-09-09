using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Presentation.OpenXR;

public partial class NativePlayerBodyAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 2) throw new ArgumentException("Expected owned Data and an ordinary native save.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(args[1]))!;
            var player = records.RuntimeFormKey(7);
            var appearance = FalloutNpcAppearanceResolver.Resolve(records, player,
                equippedArmor: saved.EquippedRuntimeFormIds.Select(records.RuntimeFormKey).Where(key => records.GetEffective(key).Signature == "ARMO").ToArray(),
                appearanceState: FalloutNativeCharacterCreation.ActorState(records, player, saved.Character));
            var weapon = saved.EquippedRuntimeFormIds.Select(records.RuntimeFormKey).Where(key => records.GetEffective(key).Signature == "WEAP")
                .Select(key => (FalloutFormKey?)key).SingleOrDefault();
            var first = new RuntimeNativePlayerActor(records, content, appearance, weapon, true, .0142875f, Colors.White);
            var body = new RuntimeNativePlayerActor(records, content, appearance, weapon, false, .0142875f, Colors.White);
            AddChild(first); AddChild(body);
            body.ConfigureBodyContacts(2);
            first.SetViewPolicy(true, false);
            if (body.BodyContacts.Count == 0) throw new InvalidOperationException("Complete source body has no contacts.");
            var sourceBodies = body.Skeleton.Source.Blocks.Where(block => block.TypeName is "NiNode" or "NiBone" or "BSFadeNode")
                .Select(block => body.Skeleton.Source.ReadNode(block.Index)).Count(node => node.CollisionObject >= 0);
            if (body.BodyContacts.Count != sourceBodies) throw new InvalidOperationException("Source body collision was culled with geometry.");
            foreach (var (visible, shadows) in new[] { (true, true), (false, true), (false, false), (true, false), (false, true) })
            {
                body.SetViewPolicy(visible, shadows);
                body.Advance(.1, Vector3.Zero, true, false);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                CheckContacts(body);
                var meshes = body.FindChildren("*", "", true, false).OfType<GeometryInstance3D>().Where(mesh => mesh.IsVisibleInTree() && mesh.Layers != 0).ToArray();
                if ((visible || shadows) != (meshes.Length > 0) || !visible && meshes.Any(mesh => mesh.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly) ||
                    !shadows && meshes.Any(mesh => mesh.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off))
                    throw new InvalidOperationException("Hidden world body lost its shadow or leaked into the eye pass.");
            }
            first.EnableTrackedArms(); body.EnableTrackedArms(first); body.UseTrackedWristShadow(first);
            CheckTrackedHands(first); CheckTrackedHands(body);
            foreach (var angle in new[] { 0f, .7f, -.8f })
            {
                var head = new Transform3D(new Basis(Vector3.Up, angle) * new Basis(Vector3.Right, angle / 2) *
                    new Basis(Vector3.Forward, angle / 3), new Vector3(.12f, 1.7f, -.08f));
                var left = new Transform3D(new Basis(Vector3.Forward, angle), new(-.23f, 1.4f, -.4f));
                var right = new Transform3D(new Basis(Vector3.Up, -angle), new(.23f, 1.4f, -.4f));
                foreach (var actor in new[] { first, body })
                {
                    actor.Advance(1.0 / 90, Vector3.Zero, true, false);
                    actor.PoseTrackedArms(1.0 / 90, head, left, right, true, true, 1, 1, 1, 1, true, true, true, right.Basis);
                    using var state = JsonDocument.Parse(JsonSerializer.Serialize(actor.XrHandState));
                    foreach (var side in new[] { "left", "right" })
                        if (state.RootElement.GetProperty(side).GetProperty("wristErrorMeters").GetSingle() > .001f)
                            throw new InvalidOperationException("Shadow/contact wrist left its tracked target.");
                }
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (body.TrackedHeadEyeFrame is not { } eye || eye.Origin.DistanceTo(head.Origin) > .0001f ||
                    eye.Basis.X.Normalized().Dot(head.Basis.X) < .99999f || eye.Basis.Y.Normalized().Dot(head.Basis.Y) < .99999f ||
                    eye.Basis.Z.Normalized().Dot(head.Basis.Z) < .99999f)
                    throw new InvalidOperationException("Hidden head/shadow did not follow headset position and tilt.");
                CheckContacts(body);
                if (body.WristDevice() is { } hiddenDevice && hiddenDevice.Root.IsVisibleInTree())
                    throw new InvalidOperationException("Two world Pip-Boy casters are active.");
                if (first.WristDevice() is not { } liveDevice || !liveDevice.Root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>()
                    .Any(mesh => mesh.IsVisibleInTree() && mesh.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off))
                    throw new InvalidOperationException("Live cuff no longer casts its own enlarged shadow.");
            }
            var negative = body.BodyContacts[0]; negative.CollisionLayer = 0;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (Hit(body, negative)) throw new InvalidOperationException("Collision negative fixture did not remove the target.");
            negative.CollisionLayer = 2;
            body.ShowTrackedBody(first);
            var heading = new NativeXrBodyHeading();
            var neutral = heading.Advance(new(Basis.Identity, new(0, 1.68f, 0)), false, 1.0 / 90);
            var turned = heading.Advance(new(new Basis(Vector3.Up, .5f), new(0, 1.68f, 0)), false, 1.0 / 90);
            if (!turned.IsEqualApprox(neutral)) throw new InvalidOperationException("A small head turn dragged the torso.");
            var footBones = new[] { "Bip01 L Foot", "Bip01 R Foot" }.Select(body.Skeleton.BoneIndex).ToArray();
            var headFromEye = NativeXrEyeFrame.InHead(body.Actor);
            var sourceEye = body.Skeleton.Node.Transform * (body.Skeleton.Node.GetBoneGlobalRest(body.Skeleton.BoneIndex("Bip01 Head")) * headFromEye).Origin;
            var footFrame = new Transform3D(Basis.Identity, new(-sourceEye.X, 0, -sourceEye.Z));
            var spineBones = new[] { "Bip01 Spine1", "Bip01 Spine2", "Bip01 Neck", "Bip01 Head" }.Select(body.Skeleton.BoneIndex).ToArray();
            foreach (var height in new[] { 1.68f, 1.1f, 1.5f, 1.68f })
            {
                body.Advance(0, Vector3.Zero, true, false);
                var feet = footBones.Select(bone => footFrame * body.Skeleton.Node.Transform * body.Skeleton.Node.GetBoneGlobalPose(bone)).ToArray();
                body.SetTrackedBodyFrame(Basis.Identity, Vector3.Zero);
                var headBone = new Transform3D(Basis.Identity, new(0, height, 0)) * headFromEye.AffineInverse();
                headBone.Basis = new Basis(Vector3.Up, .5f) * new Basis(Vector3.Right, -.7f) * headBone.Basis;
                var head = headBone * headFromEye;
                body.PrepareTrackedBody(head);
                first.Advance(0, Vector3.Zero, true, false);
                first.AdoptTrackedTorso(body);
                body.PoseTrackedArms(1.0 / 90, head, new(Basis.Identity, new(-.23f, height - .3f, -.3f)),
                    new(Basis.Identity, new(.23f, height - .3f, -.3f)), true, true, 0, 0, 0, 0, false, false, false, bodyPrepared: true);
                if (body.TrackedHeadEyeFrame is not { } trackedEye || trackedEye.Origin.DistanceTo(head.Origin) > .003f)
                    throw new InvalidOperationException($"Spine lost anatomical eyes at height {height:R}: {JsonSerializer.Serialize(body.XrHandState)}");
                foreach (var bone in spineBones)
                    if (Math.Abs(body.Skeleton.Node.GetBonePose(bone).Origin.Length() - body.Skeleton.Node.GetBoneRest(bone).Origin.Length()) > .0001f)
                        throw new InvalidOperationException("Spine length changed to accommodate a tracked target.");
                foreach (var name in new[] { "Bip01 L Clavicle", "Bip01 R Clavicle" })
                    if ((body.Skeleton.Node.GlobalTransform * body.Skeleton.Node.GetBoneGlobalPose(body.Skeleton.BoneIndex(name))).Origin.DistanceTo(
                        (first.Skeleton.Node.GlobalTransform * first.Skeleton.Node.GetBoneGlobalPose(first.Skeleton.BoneIndex(name))).Origin) > .0001f)
                        throw new InvalidOperationException("First-person attachments have a different shoulder frame.");
                for (var index = 0; index < footBones.Length; index++)
                {
                    var actual = body.Skeleton.Node.GlobalTransform * body.Skeleton.Node.GetBoneGlobalPose(footBones[index]);
                    if (actual.Origin.DistanceTo(feet[index].Origin) > .003f)
                        throw new InvalidOperationException($"Head height {height:R}, foot {index}: source foot target displaced by {actual.Origin.DistanceTo(feet[index].Origin):R} metres.");
                }
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                CheckContacts(body);
            }
            body.Advance(0, Vector3.Zero, true, false);
            body.PrepareTrackedBody(new(Basis.Identity, new(2, 1.68f, 0)));
            if (body.XrBodyPoseError is null) throw new InvalidOperationException("Unreachable full-body target was silently accepted.");
            var visibleBody = body.Actor.FindChildren("*", "", true, false).OfType<GeometryInstance3D>()
                .Where(mesh => mesh.IsVisibleInTree() && (mesh.Layers & ~RuntimeNativePlayerActor.SelfHeadLayer) != 0).ToArray();
            var hiddenHead = body.Actor.FindChildren("*", "", true, false).OfType<GeometryInstance3D>()
                .Where(mesh => mesh.IsVisibleInTree() && mesh.Layers == RuntimeNativePlayerActor.SelfHeadLayer).ToArray();
            if (visibleBody.Length == 0 || hiddenHead.Length == 0 || hiddenHead.All(mesh => mesh.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off))
                throw new InvalidOperationException("Head-only eye exclusion removed the body or the head shadow.");
            foreach (var part in first.Actor.Parts)
                if (first.WristDevice() is not { } device || part.Root != device.Root)
                    if (part.Root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>().Any(mesh => mesh.Layers != 0))
                        throw new InvalidOperationException("Duplicate first-person body remains in the eye pass.");
            GD.Print("OPENNV_PLAYER_BODY_AUDIT_PASS " + JsonSerializer.Serialize(new
            {
                sourceBodies,
                modes = "flat-third,flat-first,tracked-hidden",
                visibilityIndependentContacts = true,
                anatomicalWristTargets = true,
                trackedHeadTiltAndRoll = true,
                singleLiveWristCaster = true,
                collisionNegative = true,
                fullBodyWithHeadEyeExclusion = true,
                crouchRetainsSourceFootTargets = true,
                headTurnsIndependently = true,
                anatomicalSourceEyes = true,
                sharedTorsoForAttachments = true,
                unreachableBodyTargetReported = true,
                boundary = "query-shapes-and-render-policy;ordinary-flat-and-SIM-final-pixels-required"
            }));
            GetTree().Quit(0);
        }
        catch (Exception error) { GD.PushError("OPENNV_PLAYER_BODY_AUDIT_FAIL " + error); GetTree().Quit(1); }
    }

    private void CheckContacts(RuntimeNativePlayerActor body)
    {
        foreach (var area in body.BodyContacts)
            if (!Hit(body, area)) throw new InvalidOperationException("Unhittable body bone: " + area.GetMeta("opennv_nif_collision_bone"));
    }
    private bool Hit(RuntimeNativePlayerActor body, Area3D target)
    {
        var shape = target.GetChildren().OfType<CollisionShape3D>().First(value => !value.Disabled);
        using var ray = PhysicsRayQueryParameters3D.Create(shape.GlobalPosition + Vector3.Back * 5, shape.GlobalPosition, 2);
        ray.CollideWithAreas = true; ray.CollideWithBodies = false;
        ray.Exclude = new(body.BodyContacts.Where(area => area != target).Select(area => area.GetRid()));
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        return hit.TryGetValue("collider", out var collider) && collider.AsGodotObject() == target;
    }
}
