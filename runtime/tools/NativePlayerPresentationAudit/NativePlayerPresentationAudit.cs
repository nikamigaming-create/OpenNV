using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativePlayerPresentationAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
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
            var weapons = saved.Inventory.Where(item => item.RecordType == "WEAP").Select(item => records.RuntimeFormKey(item.RuntimeFormId)).ToArray();
            var failures = new List<string>();
            foreach (var first in new[] { true, false })
                foreach (var weapon in new FalloutFormKey?[] { null }.Concat(weapons.Select(key => (FalloutFormKey?)key)))
                {
                    RuntimeNativePlayerActor? actor = null;
                    try
                    {
                        actor = new(records, content, appearance, weapon, first, .0142875f, Colors.White); AddChild(actor);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        actor.Advance(.2, Vector3.Zero, true, false);
                        if (actor.Error is not null) throw new NotSupportedException(actor.Error);
                        if (weapon is not null)
                        {
                            var mount = actor.FindChild("EquippedWeapon", true, false)!.GetChild<Node3D>(0);
                            var roots = mount.GetChildren().OfType<Node3D>().ToArray();
                            var bound = roots.Select(root => root.Transform).ToArray();
                            for (var frame = 0; frame < 8; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                            if (roots.Where((root, index) => !root.Transform.IsEqualApprox(bound[index])).Any())
                                throw new InvalidOperationException("Held weapon left its animation attachment while physics advanced.");
                        }
                        if (first) GD.Print($"OPENNV_PLAYER_CAMERA weapon={weapon} transform={actor.SourceCamera} weaponBone={actor.Skeleton.Node.GetBoneGlobalPose(actor.Skeleton.BoneIndex("Weapon")).Origin}");
                        GD.Print($"OPENNV_PLAYER_ASSEMBLY_BOUND first={first} weapon={weapon} state={JsonSerializer.Serialize(actor.State)}");
                        if (first && weapon is null)
                        {
                            actor.PosePipBoy(actor.PipBoyDuration);
                            RemoveChild(actor);
                            var device = new NativeOwnedRenderedDevice("meshes/pipboy3000/pipboyarm.nif", FalloutInstallationSettings.Read(content), true, actor);
                            AddChild(device); device.Size = new(1280, 720);
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            device.PosePipBoy(actor.PipBoyDuration);
                            var screen = device.Geometry(device.ScreenName);
                            GD.Print($"OPENNV_PIPBOY_POSE_BOUND seconds={actor.PipBoyDuration} camera={device.Camera.Transform} screen={screen.GlobalTransform}");
                            screen.GetParent().SetMeta("opennv_audit_screen", true);
                            var vertices = screen.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                            var points = vertices.Select(vertex => device.Camera.UnprojectPosition(screen.ToGlobal(vertex))).ToArray();
                            var rect = new Rect2(new(points.Min(point => point.X), points.Min(point => point.Y)),
                                new Vector2(points.Max(point => point.X) - points.Min(point => point.X), points.Max(point => point.Y) - points.Min(point => point.Y)));
                            GD.Print("OPENNV_PIPBOY_SCREEN_PROJECTION " + rect);
                            if (!new Rect2(0, 0, 1280, 720).Encloses(rect)) throw new InvalidOperationException("Raised Pip-Boy screen is outside the source camera viewport.");
                            device.Free(); actor = null;
                        }
                        if (actor is not null)
                        {
                            if (actor.Weapon is { } held)
                            {
                                var groups = new List<string> { "equip", "unequip" };
                                if (held.ClipSize != 0 && held.Ammunition.Count != 0) groups.Add(held.ReloadGroup);
                                if (!held.Automatic && held.AnimationGroup is "1hp" or "2hr" or "2ha" && held.Ammunition.Count != 0)
                                {
                                    var shot = FalloutWeaponShot.Read(records, held.Form, held.Ammunition[0]);
                                    if (shot.Projectile.Hitscan && shot.Projectiles == 1)
                                    {
                                        shot.RequireHitscan();
                                        try { actor.PrepareMuzzle(shot.Projectile); }
                                        catch (NotSupportedException error) { GD.Print($"OPENNV_MUZZLE_PRESENTATION_UNBOUND weapon={held.Form} error={error.Message}"); }
                                        var muzzle = actor.ProjectileTransform();
                                        if (!muzzle.Origin.IsFinite() || !muzzle.Basis.IsFinite()) throw new InvalidDataException("Source projectile transform is invalid.");
                                        actor.FlashMuzzle();
                                        groups.Add(held.AttackGroup);
                                        GD.Print($"OPENNV_PLAYER_PROJECTILE_BOUND first={first} weapon={held.Form} projectile={shot.Projectile.Form} muzzle={muzzle}");
                                    }
                                }
                                foreach (var group in groups)
                                {
                                    var action = actor.PrepareAction(group);
                                    actor.SetAction(group, (action.Sequence.StopTime - action.Sequence.StartTime) / action.Sequence.Frequency * .6);
                                    actor.Advance(0, Vector3.Zero, true, false);
                                    if (actor.Error is not null) throw new NotSupportedException(actor.Error);
                                    GD.Print($"OPENNV_PLAYER_ACTION_BOUND first={first} weapon={held.Form} group={group} keys={action.TextKeys.Count}");
                                }
                                actor.SetAction(null, 0); actor.SetDrawn(false); actor.Advance(0, Vector3.Zero, true, false);
                                actor.SetDrawn(true); actor.Advance(0, Vector3.Zero, true, false);
                            }
                            foreach (var motion in new[] { Vector3.Forward, Vector3.Back, Vector3.Left, Vector3.Right })
                            {
                                actor.Advance(.2, motion * 5, true, false);
                                if (actor.Error is not null) throw new NotSupportedException(actor.Error);
                            }
                            actor.Advance(.2, Vector3.Up, false, false);
                            if (actor.Error is not null) throw new NotSupportedException(actor.Error);
                        }
                    }
                    catch (Exception error) { failures.Add($"first={first} weapon={weapon}: {error.Message}"); }
                    finally { actor?.Free(); }
                }
            if (failures.Count != 0) throw new NotSupportedException(string.Join("\n", failures));
            GD.Print("OPENNV_NATIVE_PLAYER_PRESENTATION_AUDIT_PASS recording=off visual-and-controls-acceptance=separate"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
