using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.OpenXR;
using OpenNV.Runtime.World.Actors;

public partial class NativeXrContactAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            await CheckSweeps();
            await CheckPush();
            CheckSupport();
            await CheckOwned();
            GD.Print("OPENNV_XR_CONTACT_AUDIT_PASS wall=blocked slide=allowed trackingLoss=retained unloaded=blocked weaponRotation=blocked support=source-bound headset=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("OPENNV_XR_CONTACT_AUDIT_FAIL " + error); GetTree().Quit(1); }
    }

    private async Task CheckSweeps()
    {
        var wall = new StaticBody3D { Position = new(0, 1, 0) };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.02f, 4, 4) } });
        AddChild(wall);
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            using var hand = new NativeXrHandContact(GetWorld3D().Space, [], 1);
            hand.AddShape(new SphereShape3D { Radius = .05f }, Transform3D.Identity);
            hand.AddShape(new BoxShape3D { Size = new(.04f, .08f, .8f) }, new(Basis.Identity, new(0, 0, -.4f)), true);
            var from = new Transform3D(Basis.Identity, new(-.4f, 1, 0));
            hand.Resolve(from, true, true);
            var hit = hand.Resolve(new(Basis.Identity, new(.4f, 1, .3f)), true, true);
            if (!hand.Blocked || hand.Collider != wall.GetInstanceId() || hit.Origin.X > -.059f || hit.Origin.Z < .29f)
                throw new InvalidOperationException("Hand tunneled through a thin wall or failed to slide: " + JsonSerializer.Serialize(hand.State));
            var retained = hand.Resolve(from, false, true);
            if (retained != hit || hand.Valid) throw new InvalidOperationException("Tracking loss moved the physical hand.");
            if (hand.Resolve(from, true, false) != hit || hand.Valid) throw new InvalidOperationException("Unloaded collision admitted hand motion.");
            var returned = hand.Resolve(from, true, true);
            if (returned.Origin.DistanceTo(from.Origin) > .002f || !hand.Valid) throw new InvalidOperationException("Hand failed to pull away from contact.");
            var jump = hand.Resolve(new(Basis.Identity, new(10, 1, 0)), true, true);
            if (jump.Origin.X > -.059f) throw new InvalidOperationException("Fast translation crossed a thin wall.");
            hand.Resolve(from, true, true);
            hand.SetWeaponEnabled(true);
            Transform3D rotated = from;
            for (var step = 0; step < 12; step++) rotated = hand.Resolve(new(new Basis(Vector3.Up, -Mathf.Pi / 2), from.Origin), true, true);
            if (!hand.Blocked || -rotated.Basis.Z.X > .56f)
                throw new InvalidOperationException("Held weapon rotated through the wall: " + JsonSerializer.Serialize(hand.State));
            GD.Print("OPENNV_XR_CONTACT_SWEEP " + JsonSerializer.Serialize(hand.State));
        }
        finally { wall.Free(); }
    }

    private static void CheckSupport()
    {
        var socket = new Transform3D(Basis.Identity, new(0, 0, -.4f));
        var owner = new NativeXrWeaponSupport(socket);
        for (var step = 0; step < 10; step++) owner.Advance(1.0 / 90, Transform3D.Identity, socket, true);
        if (owner.Engaged) throw new InvalidOperationException("Passing the support socket acquired a weapon.");
        for (var step = 0; step < 24; step++) owner.Advance(1.0 / 90, Transform3D.Identity, socket, true);
        if (!owner.Engaged || owner.Blend < .99f) throw new InvalidOperationException("Deliberate support dwell did not attach.");
        var moved = new Transform3D(Basis.Identity, new(.2f, 0, -.34641016f));
        var aim = owner.Advance(1.0 / 90, Transform3D.Identity, moved, true);
        var result = owner.SupportPose(new(aim, Vector3.Zero), moved);
        if (result.Origin.DistanceTo(moved.Origin) > .002f) throw new InvalidOperationException("Two-hand aiming displaced the source support socket.");
        owner.Advance(1.0 / 90, Transform3D.Identity, moved, false);
        if (owner.Engaged || owner.Blend != 0) throw new InvalidOperationException("Menu/tracking loss retained support ownership.");
    }

    private async Task CheckPush()
    {
        var prop = new RigidBody3D { Mass = 1, GravityScale = 0 };
        prop.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * .2f } });
        AddChild(prop);
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            using var hand = new NativeXrHandContact(GetWorld3D().Space, [], 1);
            hand.AddShape(new SphereShape3D { Radius = .05f }, Transform3D.Identity);
            hand.Resolve(new(Basis.Identity, new(-.2f, 0, 0)), true, true);
            for (var frame = 0; frame < 45; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                hand.Resolve(new(Basis.Identity, new(Math.Min(.25f, -.2f + frame * .015f), 0, 0)), true, true, 1.0 / 60);
                if (hand.PushImpulse > 2.001f) throw new InvalidOperationException("Hand prop force exceeded its bounded spring.");
            }
            if (prop.Position.X < .3f) throw new InvalidOperationException("Physical hand failed to push the actual dynamic body.");
            GD.Print($"OPENNV_XR_PROP_CONTACT_PASS x={prop.Position.X:F3} authoredMass={prop.Mass} damageEvents=0");
        }
        finally { prop.Free(); }
    }

    private async Task CheckOwned()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Length != 2) throw new ArgumentException("Expected owned Data and a native checkpoint.");
        RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(args[1]))!;
        var player = records.RuntimeFormKey(7);
        var appearance = FalloutNpcAppearanceResolver.Resolve(records, player,
            equippedArmor: saved.EquippedRuntimeFormIds.Select(records.RuntimeFormKey).Where(key => records.GetEffective(key).Signature == "ARMO").ToArray(),
            appearanceState: FalloutNativeCharacterCreation.ActorState(records, player, saved.Character));
        var weapons = saved.Inventory.Where(item => item.RecordType == "WEAP").Select(item => records.RuntimeFormKey(item.RuntimeFormId)).ToArray();
        foreach (var weapon in new FalloutFormKey?[] { null }.Concat(weapons.Select(key => (FalloutFormKey?)key)))
        {
            var first = new RuntimeNativePlayerActor(records, content, appearance, weapon, true, .0142875f, Colors.White);
            var body = new RuntimeNativePlayerActor(records, content, appearance, weapon, false, .0142875f, Colors.White);
            AddChild(first); AddChild(body);
            try
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                body.ConfigureBodyContacts(2); first.EnableTrackedArms();

                using var left = new NativeXrHandContact(GetWorld3D().Space, [], 1);
                using var right = new NativeXrHandContact(GetWorld3D().Space, [], 1);
                first.BindHandContact(left, body, true); first.BindHandContact(right, body, false);
                if (first.XrSupportGrip is { } socket) { CheckSourceReach(first, socket); CheckPronation(first, socket); }
                if (first.Weapon is { AmmoUse: > 0, ClipSize: > 0, Ammunition.Count: > 0, ReloadAnimation: not 255 }) CheckActionGrip(first);
                for (var frame = 0; frame < 12; frame++) first.Advance(1.0 / 90, Vector3.Zero, true, false);
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (var frame = 0; frame < 180; frame++) first.Advance(1.0 / 90, Vector3.Zero, true, false);
                clock.Stop();
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                if (first.Error is not null) throw new InvalidOperationException(first.Error);
                GD.Print($"OPENNV_XR_OWNED_CONTACT weapon={weapon} leftShapes={left.ShapeCount} rightShapes={right.ShapeCount} support={first.XrSupportGrip is not null} supportOffset={first.XrSupportGrip?.Origin} animationBytesPerFrame={allocated / 180} animationMillisecondsPerFrame={clock.Elapsed.TotalMilliseconds / 180:F3}");
            }
            finally { first.Free(); body.Free(); }
        }
    }

    private static void CheckSourceReach(RuntimeNativePlayerActor actor, Transform3D socket)
    {
        var head = new Transform3D(Basis.Identity, new(0, 1.68f, 0));
        actor.PrepareTrackedBody(head);
        var primary = new Transform3D(Basis.Identity, new(.15f, 1.4f, -.2f));
        var support = primary * socket;
        var target = actor.XrLeftArm.HandTarget(support);
        if (!actor.XrLeftArm.CanReach(target)) throw new InvalidOperationException("Source support cannot reach a shouldered weapon.");
        var far = actor.XrLeftArm.HandTarget(new(Basis.Identity, new(0, 1.4f, -2)));
        if (actor.XrLeftArm.CanReach(far)) throw new InvalidOperationException("Unreachable support was admitted.");
        var clamped = actor.XrLeftArm.ConstrainHandTarget(far);
        actor.PoseTrackedArms(1.0 / 90, head, support, primary, true, true, 0, 0, 0, 0, false, false, true,
            primary.Basis, clamped, actor.XrRightArm.ConstrainHandTarget(actor.XrRightArm.HandTarget(primary, primary.Basis)));
        if (actor.XrLeftArm.WristErrorMeters > .001f) throw new InvalidOperationException("Anatomical limit separated the rendered wrist from its physical target.");
        actor.PoseTrackedArms(1.0 / 90, head, support, primary, true, true, 0, 0, 0, 0, false, false, true,
            primary.Basis, clamped, actor.XrRightArm.HandTarget(new(Basis.Identity, new(0, 1.4f, -2)), primary.Basis));
        if (actor.XrRightContactReached || actor.XrContactPoseError is null)
            throw new InvalidOperationException("A retained contact outside body reach was admitted for firing.");
    }

    private static void CheckPronation(RuntimeNativePlayerActor actor, Transform3D socket)
    {
        var head = new Transform3D(Basis.Identity, new(0, 1.68f, 0));
        var primary = new Transform3D(Basis.Identity, new(.15f, 1.4f, -.2f));
        var support = primary * socket;
        var left = actor.XrLeftArm.HandTarget(support);
        var right = actor.XrRightArm.HandTarget(primary, primary.Basis);
        var skeleton = actor.Skeleton.Node;
        var forearm = actor.Skeleton.BoneIndex("Bip01 L Forearm");
        var helper = actor.Skeleton.BoneIndex("Bip01 L ForeTwist");
        void Publish()
        {
            actor.Advance(1.0 / 90, Vector3.Zero, true, false);
            actor.PoseTrackedArms(1.0 / 90, head, support, primary, true, true, 0, 0, 0, 0, false, false, true,
                primary.Basis, left, right);
        }
        Publish();
        var elbow = skeleton.GetBoneGlobalPose(forearm);
        var twist = skeleton.GetBoneGlobalPose(helper);
        var axis = (left.Origin - (skeleton.GlobalTransform * elbow).Origin).Normalized();
        left.Basis = new Basis(axis, 1) * left.Basis;
        Publish();
        if (!skeleton.GetBoneGlobalPose(forearm).IsEqualApprox(elbow))
            throw new InvalidOperationException("Wrist pronation rotated the elbow skin's primary bone.");
        var roll = twist.Basis.GetRotationQuaternion().AngleTo(skeleton.GetBoneGlobalPose(helper).Basis.GetRotationQuaternion());
        if (MathF.Abs(roll - 1) > .002f || actor.XrLeftArm.WristErrorMeters > .001f)
            throw new InvalidOperationException("Source forearm twist helper lost the tracked wrist roll.");
    }

    private static void CheckActionGrip(RuntimeNativePlayerActor actor)
    {
        actor.SetAction(null, 0); actor.Advance(0, Vector3.Zero, true, false);
        var bone = actor.Skeleton.BoneIndex("Weapon");
        var original = actor.Skeleton.Node.GetBonePose(bone);
        var head = new Transform3D(Basis.Identity, new(0, 1.68f, 0));
        var right = new Transform3D(Basis.Identity, new(.2f, 1.4f, -.3f));
        foreach (var group in new[] { actor.Weapon!.AttackGroup, actor.Weapon.ReloadGroup })
        {
            var clip = actor.PrepareAction(group);
            var maximumPosition = 0f; var maximumBasis = 0f;
            for (var step = 0; step <= 12; step++)
            {
                actor.SetAction(group, (clip.Sequence.StopTime - clip.Sequence.StartTime) * step / 12);
                actor.Advance(0, Vector3.Zero, true, false);
                actor.PoseTrackedArms(1.0 / 90, head, new(Basis.Identity, new(-.22f, 1.35f, -.3f)), right,
                    true, true, 0, 0, 0, 0, false, false, true, right.Basis);
                var pose = actor.Skeleton.Node.GetBonePose(bone);
                // Compare physical displacement, not relative component error
                // near zero in the exported Float32 rotation matrix.
                var basisError = Math.Max((pose.Basis.X - original.Basis.X).Length(),
                    Math.Max((pose.Basis.Y - original.Basis.Y).Length(), (pose.Basis.Z - original.Basis.Z).Length()));
                maximumPosition = Math.Max(maximumPosition, pose.Origin.DistanceTo(original.Origin));
                maximumBasis = Math.Max(maximumBasis, basisError);
            }
            GD.Print($"OPENNV_XR_ACTION_GRIP weapon={actor.Weapon.Form} group={group} positionDelta={maximumPosition:R} basisDelta={maximumBasis:R}");
            if (maximumPosition > .001f || maximumBasis > .001f)
                throw new InvalidOperationException($"Tracked {actor.Weapon.Form} {group} changed its physical grip: distance={maximumPosition:R} basis={maximumBasis:R}");
        }
        actor.SetAction(null, 0); actor.Advance(0, Vector3.Zero, true, false);
    }
}
