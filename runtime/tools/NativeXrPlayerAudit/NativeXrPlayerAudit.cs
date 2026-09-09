using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Presentation.OpenXR;

public partial class NativeXrPlayerAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            CheckCoordinateContracts();
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 2) throw new ArgumentException("Expected owned Data and a real native checkpoint.");
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
            var actor = new RuntimeNativePlayerActor(records, content, appearance, weapon, true, .0142875f, Colors.White);
            AddChild(actor);
            try
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                actor.EnableTrackedArms();
                var skeleton = actor.Skeleton.Node;
                if (actor.FindChildren("*", nameof(Skeleton3D), true, false).Count != 1)
                    throw new InvalidOperationException("The XR player has more than one skeleton.");
                var device = actor.WristDevice() ?? throw new InvalidOperationException("Saved equipped Pip-Boy is absent.");
                var identity = device.Root.GetInstanceId();
                var poses = new[]
                {
                    new Transform3D(Basis.Identity, new Vector3(0, 1.68f, 0)),
                    new Transform3D(new Basis(Vector3.Up, .6f), new Vector3(.1f, 1.68f, -.05f)),
                    new Transform3D(new Basis(Vector3.Right, -.35f), new Vector3(-.1f, 1.65f, .04f)),
                };
                foreach (var head in poses)
                    for (var step = 0; step < 9; step++)
                    {
                        actor.Advance(0, Vector3.Zero, true, false);
                        var angle = (step - 4) * .15f;
                        var aimBasis = new Basis(Vector3.Up, -angle) * new Basis(Vector3.Right, -.2f);
                        actor.PoseTrackedArms(1.0 / 90, head,
                            new(new Basis(Vector3.Forward, angle), new(-.22f, 1.35f, -.3f)),
                            new(aimBasis * new Basis(Vector3.Right, .65f), new(.22f, 1.35f, -.3f)),
                            true, true, step / 8f, step / 8f, step / 8f, step / 8f, step > 4, step > 4, true, aimBasis);
                        if (weapon is not null && actor.ProjectileTransform().Basis.Z.Normalized().Dot(aimBasis.Z) < .999f)
                            throw new InvalidOperationException("Source muzzle does not follow the separate controller aim frame.");
                        if (actor.WristDevice()!.Value.Root.GetInstanceId() != identity)
                            throw new InvalidOperationException("Controller motion replaced the equipped device.");
                        using var state = JsonDocument.Parse(JsonSerializer.Serialize(actor.XrHandState));
                        foreach (var side in new[] { "left", "right" })
                            if (state.RootElement.GetProperty(side).GetProperty("wristErrorMeters").GetSingle() > .001f)
                                throw new InvalidOperationException("Reachable wrist left its controller frame.");
                    }
                // Source weapon remains a descendant of the tracked anatomical
                // hand; no independent mesh transform can fake this join.
                if (skeleton.GetBoneParent(actor.Skeleton.BoneIndex("Weapon")) != actor.Skeleton.BoneIndex("Bip01 R Hand"))
                    throw new InvalidOperationException("Weapon is detached from the source right-hand bone.");
                var original = skeleton.GetBoneGlobalPose(actor.Skeleton.BoneIndex("Bip01 L Hand"));
                actor.Advance(0, Vector3.Zero, true, false);
                actor.PoseTrackedArms(0, poses[^1], Transform3D.Identity, Transform3D.Identity, false, false, 0, 0, 0, 0, false, false, false);
                if (!skeleton.GetBoneGlobalPose(actor.Skeleton.BoneIndex("Bip01 L Hand")).IsEqualApprox(original))
                    throw new InvalidOperationException("Tracking loss used the invalid replacement pose.");
                actor.Advance(0, Vector3.Zero, true, false);
                var headPose = new Transform3D(new Basis(Vector3.Right, -.3f), new(0, 1.7f, 0));
                actor.PoseTrackedArms(1, headPose,
                    new(new Basis(Vector3.Up, .65f) * new Basis(Vector3.Right, .2f) * new Basis(Vector3.Back, .2f), new(-.2f, 1.48f, -.38f)),
                    new(Basis.Identity, new(.04f, 1.7f, -.05f)), true, true, 1, 0, 0, 0, true, true, false);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var surface = new NativeOwnedDeviceSurface(device.Path, device.Root, "pipboyscreen:0");
                var screenFrame = surface.Geometry(surface.ScreenName).GlobalTransform *
                    NativeXrWristPresentation.ScreenFrame(surface.ScreenVertices, surface.ScreenUvs);
                foreach (var glow in new[] { "StatsGlow:0", "ItemsGlow:0", "DataGlow:0" }) surface.Geometry(glow).Visible = false;
                foreach (var section in new[] { "01", "02", "03" })
                    foreach (var geometry in surface.GeometryParts("PipBoyButton" + section))
                    {
                        var target = geometry.ToGlobal(geometry.GetAabb().GetCenter());
                        var from = target + screenFrame.Basis.Z.Normalized() * .25f;
                        surface.PickScreen(from, (target - from).Normalized());
                        GD.Print($"OPENNV_XR_BUTTON_GEOMETRY name={geometry.GetMeta("opennv_nif_source_name")} target={target} hit={surface.PickedGeometry?.GetMeta("opennv_nif_source_name")}");
                        if (geometry.GetMeta("opennv_nif_source_name").AsString().EndsWith(":1", StringComparison.Ordinal) && surface.PickedGeometry != geometry)
                            throw new InvalidOperationException("The visible source button cap is not reachable by the device ray.");
                    }
                var originalScreen = surface.Geometry(surface.ScreenName).GlobalTransform;
                var originalHand = skeleton.GetBoneGlobalPose(actor.Skeleton.BoneIndex("Bip01 L Hand"));
                var originalMeshes = device.Root.FindChildren("*", nameof(MeshInstance3D), true, false).Count;
                using (var wrist = new NativeXrWristPresentation(surface))
                {
                    wrist.Advance(.2, true); wrist.Publish(headPose);
                    var enlarged = surface.Geometry(surface.ScreenName).GlobalTransform;
                    var facing = enlarged * NativeXrWristPresentation.ScreenFrame(surface.ScreenVertices, surface.ScreenUvs);
                    var attachment = surface.Root.GetChildren().OfType<BoneAttachment3D>().Single();
                    var axis = (attachment.GlobalBasis * wrist.CuffAxis).Normalized();
                    var cuff = attachment.GlobalTransform * wrist.CuffCenter;
                    if (Mathf.Abs(enlarged.Basis.X.Length() / originalScreen.Basis.X.Length() - NativeXrWristPresentation.FocusScale) > .0001f ||
                        (wrist.PresentationTransform * wrist.CuffCenter).DistanceTo(wrist.CuffCenter) > .0001f ||
                        (wrist.PresentationTransform.Basis * wrist.CuffAxis).Normalized().Dot(wrist.CuffAxis) < .9999f ||
                        facing.Basis.Z.Slide(axis).Normalized().Dot((headPose.Origin - cuff).Slide(axis).Normalized()) < .9999f)
                        throw new InvalidOperationException("Whole-device focus left the forearm centerline, tilted its cuff axis or lost its readable roll.");
                    if (!skeleton.GetBoneGlobalPose(actor.Skeleton.BoneIndex("Bip01 L Hand")).IsEqualApprox(originalHand) ||
                        device.Root.FindChildren("*", nameof(MeshInstance3D), true, false).Count != originalMeshes)
                        throw new InvalidOperationException("Device focus changed the hand or duplicated source geometry.");
                    foreach (var section in new[] { "01", "02", "03" })
                        foreach (var geometry in surface.GeometryParts("PipBoyButton" + section))
                        {
                            if (!geometry.GetMeta("opennv_nif_source_name").AsString().EndsWith(":1", StringComparison.Ordinal)) continue;
                            var target = geometry.ToGlobal(geometry.GetAabb().GetCenter());
                            var from = target + facing.Basis.Z.Normalized() * .35f;
                            surface.PickScreen(from, (target - from).Normalized());
                            if (surface.PickedGeometry != geometry)
                                throw new InvalidOperationException("Enlarged device button is disconnected from its visible geometry.");
                        }
                    // Sweep across the angle seam more than once, then release
                    // while moving the reader. The cuff must unwind, not orbit.
                    var readerOffset = (headPose.Origin - cuff).Slide(axis);
                    for (var step = 0; step <= 144; step++)
                    {
                        var reader = new Transform3D(headPose.Basis, cuff + new Basis(axis, step * Mathf.Pi / 36) * readerOffset);
                        wrist.Advance(1.0 / 90, true); wrist.Publish(reader);
                        if ((wrist.PresentationTransform * wrist.CuffCenter).DistanceTo(wrist.CuffCenter) > .0001f ||
                            (wrist.PresentationTransform.Basis * wrist.CuffAxis).Normalized().Dot(wrist.CuffAxis) < .9999f)
                            throw new InvalidOperationException("Moving wrist focus left its anatomical socket.");
                    }
                    var previousRoll = wrist.PresentationTransform.Basis.Orthonormalized();
                    for (var step = 0; step < 15; step++)
                    {
                        wrist.Advance(1.0 / 90, false); wrist.Publish(headPose);
                        var roll = wrist.PresentationTransform.Basis.Orthonormalized();
                        var angle = previousRoll.GetRotationQuaternion().AngleTo(roll.GetRotationQuaternion());
                        if (angle > Mathf.Pi / (90 * .16f) + .001f)
                            throw new InvalidOperationException("Cuff release spun across the angle seam.");
                        previousRoll = roll;
                    }
                    if (!surface.Geometry(surface.ScreenName).GlobalTransform.IsEqualApprox(originalScreen))
                        throw new InvalidOperationException("Grip release did not restore the source wrist device.");
                }
                if (!surface.Geometry(surface.ScreenName).GlobalTransform.IsEqualApprox(originalScreen))
                    throw new InvalidOperationException("Disposing wrist focus changed the source attachment.");
                GD.Print("OPENNV_XR_WRIST_DEVICE_PASS scale=2.25 roll=forearm-axis socket=fixed motionSweep=144 buttons=source-geometry release=shortest-angle duplicateMeshes=0");
                if (weapon is { } weaponForm)
                {
                    var ammunition = saved.WeaponHandling!.Magazines.Single(row => row.Weapon == weaponForm).Ammunition;
                    var projectile = FalloutWeaponShot.Read(records, weaponForm, ammunition).Projectile;
                    actor.PrepareMuzzle(projectile); actor.FlashMuzzle();
                    var light = actor.FindChildren("SourceMuzzleLight", "", true, false).OfType<OmniLight3D>().Single();
                    var source = FalloutCellSceneReader.ReadLight(records.GetEffective(projectile.MuzzleLight!.Value));
                    if (!light.Visible || Math.Abs(light.OmniRange - source.RadiusGameUnits * actor.Skeleton.UnitsToMetres) > .00001f)
                        throw new InvalidOperationException("Muzzle light did not use the projectile's source LIGH radius.");
                    actor.Advance(.25, Vector3.Zero, true, false);
                    if (light.Visible) throw new InvalidOperationException("Muzzle light survived the source flash window.");
                    GD.Print($"OPENNV_XR_MUZZLE_LIGHT_PASS source={projectile.MuzzleLight} range={light.OmniRange} expired=true");
                }
                GD.Print("OPENNV_NATIVE_XR_PLAYER_AUDIT_PASS skeletons=1 deviceOwners=1 poses=27 trackingLoss=retained visual-acceptance=separate");
            }
            finally { actor.Free(); }
            var menuHead = new Camera3D(); AddChild(menuHead);
            var canvas = new NativeXrCanvas(menuHead); AddChild(canvas);
            try
            {
                foreach (var ownerFirst in new[] { false, true })
                {
                    var owner = new Node3D(); AddChild(owner);
                    var layer = new CanvasLayer(); owner.AddChild(layer); canvas.Admit(layer);
                    if (owner.GetSignalConnectionList(Node.SignalName.TreeExiting).Count != 1)
                        throw new InvalidOperationException("XR canvas did not attach exactly one lifetime callback.");
                    if (ownerFirst) owner.Free();
                    else
                    {
                        layer.Free();
                        if (owner.GetSignalConnectionList(Node.SignalName.TreeExiting).Count != 0)
                            throw new InvalidOperationException("Released XR canvas retained its owner's callback.");
                        owner.Free();
                    }
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                GD.Print("OPENNV_XR_CANVAS_LIFETIME_PASS ownerFirst=true layerFirst=true");
            }
            finally { canvas.Free(); menuHead.Free(); }
            GC.Collect(); GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
