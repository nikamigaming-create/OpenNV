using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerPresentationAudit
{
    // The selected weapon is an assembly input only. The owned checkpoint,
    // campaign inventory, quest stages and retained failures are never changed.
    private async Task AuditOwnedWeaponAction(string game, string mod, string root, string checkpoint,
        string weaponEditorId, string[] dependencies)
    {
        var savedBytes = File.ReadAllBytes(checkpoint);
        var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(savedBytes) ??
            throw new InvalidDataException("The owned checkpoint is absent.");
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        RuntimeLiveContentSource.Configure(setup.BaseInstallation.InstallRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            if (saved.SaveCompatibilityId != content.SaveCompatibilityId)
                throw new InvalidDataException("The owned checkpoint belongs to a different winning content stack.");
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            world.RestoreActorOverrides(saved.ActorOverrides);
            var player = records.RuntimeFormKey(7);
            var reference = records.RuntimeFormKey(0x14);
            var original = FalloutNativeCharacterCreation.ActorState(records, player, saved.Character) with
            { PlayerYoung = saved.Scripts?.Session?.PlayerYoung == true };
            world.BindPlayerAppearance(() => original);
            var changed = world.ActorAppearanceOverride(reference);
            var effective = original with
            {
                Race = world.ActorRace(reference),
                Height = world.ActorHeight(reference),
                Hair = changed?.HairOverridden == true ? changed.Hair : original.Hair,
                HairOverridden = changed?.HairOverridden == true,
            };
            var appearance = FalloutNpcAppearanceResolver.Resolve(records, player,
                equippedArmor: saved.EquippedRuntimeFormIds.Select(records.RuntimeFormKey)
                    .Where(key => records.GetEffective(key).Signature == "ARMO").ToArray(), appearanceState: effective);
            var weapon = FalloutDialogueTopic.Find(records, "WEAP", weaponEditorId);
            var models = new[] { true, false }.Select(first => FalloutWeaponPresentation.Read(records, weapon.FormKey, first))
                .Select(held => held.Model?.ModelPath ?? throw new InvalidDataException("Selected source weapon has no owned model."))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var resources = new Dictionary<string, (string Source, string Hash)>(StringComparer.OrdinalIgnoreCase);
            content.ResourceReadObserver = (path, source, bytes) =>
            {
                if (!path.EndsWith(".kf", StringComparison.OrdinalIgnoreCase) && !models.Contains(path)) return;
                var hash = Convert.ToHexString(SHA256.HashData(bytes.Span));
                if (resources.TryGetValue(path, out var before) && (before.Source != source || before.Hash != hash))
                    throw new InvalidDataException("Selected owned weapon resource changed during assembly.");
                resources[path] = (source, hash);
            };
            GD.Print($"OPENNV_OWNED_WEAPON_FIXTURE kind=isolated-owned-checkpoint-fixture weapon={weapon.FormKey} " +
                $"winningPlugin={weapon.Plugin.Name} plugins={content.PluginSources.Count} effectiveRace={effective.Race} " +
                $"playerYoung={original.PlayerYoung} checkpointSha256={Convert.ToHexString(SHA256.HashData(savedBytes))} campaignMutation=false recording=off");
            foreach (var first in new[] { true, false })
            {
                RuntimeNativePlayerActor? actor = null;
                try
                {
                    actor = new(records, content, appearance, weapon.FormKey, first, .0142875f, Colors.White);
                    AddChild(actor);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var held = actor.Weapon!;
                    var attachment = actor.FindChild("EquippedWeapon", true, false) as BoneAttachment3D ??
                        throw new InvalidDataException("Selected source weapon has no native bone attachment.");
                    var meshes = attachment.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToArray();
                    if (meshes.Length == 0 || meshes.All(mesh => mesh.Mesh is null || mesh.Mesh.GetSurfaceCount() == 0) ||
                        attachment.BoneName != "Weapon") throw new InvalidDataException("Selected weapon lacks its source geometry or hand attachment.");
                    if (held.Grip is < 230 or > 235) throw new NotSupportedException("Selected audit needs an authored source weapon grip.");
                    var directory = first ? "meshes/characters/_1stperson" : appearance.SkeletonPath[..appearance.SkeletonPath.LastIndexOf('/')];
                    var grip = content.ActorAnimations.Require(directory, held.AnimationGroup + "handgrip" + (held.Grip - 229));
                    if (!content.TryRead(grip, null, out var gripBytes, out _)) throw new FileNotFoundException(grip);
                    var gripSource = FalloutNifFile.Read(gripBytes);
                    var gripSequence = gripSource.Roots.Select(gripSource.ReadObject).OfType<FalloutNifControllerSequence>().Single();
                    if (gripSequence.ControlledBlocks.Length == 0) throw new InvalidDataException("Owned grip has no authored animation channels.");
                    actor.SetDrawn(true);
                    AdvanceOwnedWeapon(actor, .2, false);
                    AdvanceOwnedWeapon(actor, .2, true);
                    var weaponRoot = attachment.GetChild<Node3D>(0);
                    var rest = weaponRoot.Transform;
                    for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    if (!weaponRoot.Transform.IsEqualApprox(rest)) throw new InvalidDataException("Held source weapon left its animation attachment during physics.");
                    GD.Print($"OPENNV_OWNED_WEAPON_ASSEMBLY_PASS first={first} weapon={held.Form} model={held.Model!.ModelPath} " +
                        $"meshes={meshes.Length} grip={held.Grip} gripPath={grip} gripGroup={gripSequence.Name} " +
                        $"gripChannels={gripSequence.ControlledBlocks.Length} physicsAttachment=true");
                    var groups = new List<string> { "equip", "unequip", held.AttackGroup };
                    if (held.HasAmmunitionSource && held.ClipSize > 0 && held.ReloadAnimation != 255) groups.Add(held.ReloadGroup);
                    foreach (var group in groups)
                    {
                        actor.SetAction(null, 0);
                        var clip = actor.PrepareAction(group);
                        var duration = (clip.Sequence.StopTime - clip.Sequence.StartTime) / clip.Sequence.Frequency;
                        if (!double.IsFinite(duration) || duration <= 0 || clip.UnboundChannels.Count != 0)
                            throw new InvalidDataException("Owned weapon action has invalid timing or unbound channels.");
                        var keys = clip.TextKeys.SelectMany(key => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).ToArray();
                        if (group == held.AttackGroup && !keys.Any(FalloutWeaponAnimationTimeline.DischargesWeapon))
                            throw new NotSupportedException("Owned attack lacks a source Hit, Fire or Release event.");
                        foreach (var fraction in new[] { 0d, .5, 1 })
                        {
                            actor.SetAction(group, duration * fraction);
                            AdvanceOwnedWeapon(actor, 0, false);
                        }
                        GD.Print($"OPENNV_OWNED_WEAPON_ACTION_PASS first={first} weapon={held.Form} group={group} " +
                            $"sequence={clip.Sequence.Name} channels={clip.TransformChannelCount} keys={string.Join('|', keys)} endpoints=true");
                    }
                    actor.SetAction(null, 0);
                    actor.SetDrawn(false); AdvanceOwnedWeapon(actor, 0, false);
                    actor.SetDrawn(true); AdvanceOwnedWeapon(actor, 0, true);
                    var ammo = held.Ammunition.Single();
                    var shot = FalloutWeaponShot.Read(records, held.Form, ammo);
                    shot.RequireInstantRay();
                    actor.PrepareMuzzle(shot.Projectile);
                    var muzzle = actor.ProjectileTransform();
                    if (!muzzle.Origin.IsFinite() || !muzzle.Basis.IsFinite()) throw new InvalidDataException("Source projectile socket is invalid.");
                    actor.FlashMuzzle(); AdvanceOwnedWeapon(actor, .1, true);
                    GD.Print($"OPENNV_OWNED_WEAPON_SOCKET_PASS first={first} weapon={held.Form} ammo={ammo} projectile={shot.Projectile.Form} finite=true");
                }
                finally { actor?.Free(); }
            }
            content.ResourceReadObserver = null;
            foreach (var (path, before) in resources)
                if (!content.TryRead(path, null, out var bytes, out var source) || source != before.Source ||
                    Convert.ToHexString(SHA256.HashData(bytes)) != before.Hash) throw new InvalidDataException("Selected owned source bytes changed.");
            if (!savedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(checkpoint))) throw new InvalidDataException("The owned checkpoint changed.");
            GD.Print($"OPENNV_OWNED_WEAPON_ACTION_AUDIT_PASS resources={resources.Count} sourceReadOnly=true checkpointReadOnly=true " +
                "campaignMutation=false ordinaryInputContact=unverified persistence=unverified finalPixels=unverified recording=off");
        }
        finally
        {
            RuntimeLiveContentSource.Clear();
            if (!savedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(checkpoint))) throw new InvalidDataException("The owned checkpoint changed.");
        }
    }

    private static void AdvanceOwnedWeapon(RuntimeNativePlayerActor actor, double delta, bool aiming)
    {
        actor.Advance(delta, Vector3.Zero, true, aiming);
        if (actor.Error is not null) throw new NotSupportedException(actor.Error);
        for (var bone = 0; bone < actor.Skeleton.Node.GetBoneCount(); bone++)
        {
            var pose = actor.Skeleton.Node.GetBonePose(bone);
            if (!pose.Origin.IsFinite() || !pose.Basis.IsFinite()) throw new InvalidDataException("Owned weapon animation produced an invalid skeleton pose.");
        }
    }
}
