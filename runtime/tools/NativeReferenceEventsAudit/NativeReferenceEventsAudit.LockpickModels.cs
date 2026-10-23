using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeReferenceEventsAudit
{
    private async Task OwnedLockpickModels(string game, string? capture)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        var random = new FalloutSharedUnsignedRandom(() => 123u);
        var before = FalloutSharedUnsignedRandom.Process.Observation;
        var models = new NativeOwnedLockpickModels();
        Require(FalloutSharedUnsignedRandom.Process.Observation == before, "Preparing presentation consumed gameplay randomness.");
        var surface = models.Surface;
        var lockModel = surface.Model(NativeOwnedLockpickModels.LockPath.ToUpperInvariant().Replace('/', '\\'));
        var pinModel = surface.Model(NativeOwnedLockpickModels.PinPath);
        Require(!ReferenceEquals(lockModel.Animation, pinModel.Animation), "Owned lock and pin share an animation owner.");
        try { _ = surface.Animation; throw new InvalidDataException("Ambiguous surface animation was accepted."); }
        catch (InvalidOperationException) { }
        var session = FalloutLockpickSession.CreateManual(() => 100, random.NextBounded, () => { }, () => { },
            1000, 500, 100, 100, 1, 1, () => 0, () => { }, new());
        var errors = new List<Exception>();
        var menu = new NativeLockpickMenu(session, _ => { }, errors.Add, models);
        var actions = Enumerable.Range(0, 4).Select(RuntimeNativeInputControls.Action).Where(action => !InputMap.HasAction(action)).ToArray();
        foreach (var action in actions) InputMap.AddAction(action);
        try
        {
            AddChild(menu);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            models.Publish(500, 1000, 0);
            void RequirePinLighting()
            {
                var checkedMaterials = 0;
                foreach (var mesh in pinModel.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>())
                {
                    var center = surface.Camera.GlobalTransform.AffineInverse() * (mesh.GlobalTransform * mesh.GetAabb().GetCenter());
                    for (var index = 0; index < mesh.Mesh.GetSurfaceCount(); index++)
                    {
                        if (mesh.GetActiveMaterial(index) is not ShaderMaterial material ||
                            material.ResourceName != NativeNifLightingMaterial.ResourceIdentity) continue;
                        checkedMaterials++;
                        var count = material.GetShaderParameter("owned_point_count").AsInt32();
                        var positions = material.GetShaderParameter("owned_point_position").AsVector3Array();
                        var radii = material.GetShaderParameter("owned_point_radius").AsFloat32Array();
                        Require(Enumerable.Range(0, count).Any(light => center.DistanceTo(positions[light]) < radii[light]),
                            $"The translated owned pin geometry lies outside every bound fallback light: center={center}, " +
                            $"lights={string.Join(';', Enumerable.Range(0, count).Select(light => $"{positions[light]} radius={radii[light]}"))}.");
                    }
                }
                Require(checkedMaterials > 0, "Owned pin light coverage did not examine any lighting material.");
            }
            RequirePinLighting();
            foreach (var position in new[] { 0f, 250, 500, 750, 1000 })
            {
                models.Publish(position, 1000, 45); RequirePinLighting();
            }
            models.Publish(500, 1000, 0);
            var baseline = pinModel.Pose.Transform;
            var lockNodes = lockModel.Root.FindChildren("*", "", true, false).OfType<Node3D>().ToArray();
            var closedPose = lockNodes.Select(node => node.Transform).ToArray();
            lockModel.Animation.SeekSourceTime(lockModel.Animation.SequenceRange("Backward").StopTime);
            Require(lockModel.Animation.SourceTimeSeconds == 0, "Owned looping negative control no longer reproduces the prior endpoint wrap.");
            models.Publish(300, 1000, 90);
            RequirePinLighting();
            Require(lockModel.Animation.SourceTimeSeconds == lockModel.Animation.SequenceRange("Backward").StopTime &&
                lockNodes.Where((node, index) => !node.Transform.IsEqualApprox(closedPose[index])).Any(),
                "Fully open cylinder wrapped its source stop pose to the closed loop start.");
            Require(BitConverter.SingleToInt32Bits(NativeOwnedLockpickModels.PinAngle(300, 1000)) ==
                BitConverter.SingleToInt32Bits(.6283185482025146f), "Pin source angle precision/order changed.");
            models.Publish(0, 1000, 45);
            Require(pinModel.Pose.Transform != baseline &&
                Math.Abs(lockModel.Animation.SourceTimeSeconds - lockModel.Animation.SequenceRange("Backward").StopTime / 2) < .00001,
                "Owned pin pivot or cylinder source sampling did not publish.");
            models.Publish(500, 1000, 0);
            Require(pinModel.Pose.Transform.IsEqualApprox(baseline), "Pin positioning accumulated prior-frame drift.");
            using var motion = new InputEventMouseMotion { Relative = new(300, 0), ScreenRelative = new(300, 0) };
            Input.ParseInputEvent(motion);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(session.Position == 800 && pinModel.Pose.Transform != baseline && errors.Count == 0,
                "Actual menu mouse input did not drive the owned pin pose.");
            RequirePinLighting();
            Require(!lockModel.Animation.IsProcessing() && !pinModel.Animation.IsProcessing(),
                "Angle-owned model controllers advance independently of gameplay.");
            if (capture is not null)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = surface.View.GetTexture().GetImage();
                if (image is null || image.IsEmpty()) throw new InvalidDataException("Owned lockpick surface has no rendered image.");
                if (image.SavePng(capture) != Error.Ok) throw new IOException("Lockpick model capture failed.");
            }
            GD.Print("OPENNV_LOCKPICK_MODELS_PASS ownedLock=true ownedPin=true separateControllers=true actualMouse=true pivotNoDrift=true angleClock=true ordinaryCampaignAndRetailParity=unverified");
        }
        finally
        {
            menu.Free();
            foreach (var action in actions) InputMap.EraseAction(action);
        }
    }
}
