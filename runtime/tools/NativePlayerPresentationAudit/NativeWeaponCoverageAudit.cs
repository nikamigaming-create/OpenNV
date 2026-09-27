using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

public partial class NativePlayerPresentationAudit
{
    private async Task AuditWeaponCoverage(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutNativeCampaignState saved, string output)
    {
        var player = records.RuntimeFormKey(7);
        var appearance = FalloutNpcAppearanceResolver.Resolve(records, player,
            equippedArmor: saved.EquippedRuntimeFormIds.Select(records.RuntimeFormKey)
                .Where(key => records.GetEffective(key).Signature == "ARMO").ToArray(),
            appearanceState: FalloutNativeCharacterCreation.ActorState(records, player, saved.Character));
        using var writer = new StreamWriter(output, false);
        var total = 0; var failed = 0;
        foreach (var record in records.EffectiveRecords("WEAP").Where(record => !record.IsDeleted))
        {
            var failures = new List<object>();
            var pairs = new List<object>();
            var actions = new List<object>();
            FalloutWeaponPresentation? source = null;
            try
            {
                source = FalloutWeaponPresentation.Read(records, record.FormKey);
                if (source.IsMine) failures.Add(new { stage = "attack-owner", error = "Mine placement and proximity detonation are unbound." });
                else if (!source.IsMeleeWeapon)
                    foreach (var ammo in source.Ammunition.Count == 0 ? new FalloutFormKey?[] { null } : source.Ammunition.Select(key => (FalloutFormKey?)key))
                    {
                        FalloutWeaponShot? shot = null;
                        string? error = null;
                        try
                        {
                            shot = FalloutWeaponShot.Read(records, source.Form, ammo, source.HasAmmunitionSource);
                            shot.RequireRuntimeAttackOwner();
                        }
                        catch (Exception failure) { error = failure.Message; failures.Add(new { stage = "projectile", ammo = ammo?.ToString(), error }); }
                        pairs.Add(new { ammo = ammo?.ToString(), projectile = shot?.Projectile, error });
                    }
            }
            catch (Exception failure) { failures.Add(new { stage = "weapon-source", error = failure.Message }); }
            foreach (var first in new[] { true, false })
            {
                if (source is not { Playable: true, Embedded: false }) continue;
                RuntimeNativePlayerActor? actor = null;
                try
                {
                    actor = new(records, content, appearance, record.FormKey, first, .0142875f, Colors.White);
                    AddChild(actor);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    actor.Advance(0, Vector3.Zero, true, false);
                    if (actor.Error is not null) throw new NotSupportedException(actor.Error);
                    var held = actor.Weapon!;
                    var groups = new List<string> { "equip", "unequip", held.AttackGroup };
                    if (held.HasAmmunitionSource && held.ClipSize > 0 && held.ReloadAnimation != 255) groups.Add(held.ReloadGroup);
                    foreach (var group in groups)
                    {
                        try
                        {
                            var clip = actor.PrepareAction(group);
                            actor.SetAction(group, (clip.Sequence.StopTime - clip.Sequence.StartTime) / clip.Sequence.Frequency * .6);
                            actor.Advance(0, Vector3.Zero, true, false);
                            if (actor.Error is not null) throw new NotSupportedException(actor.Error);
                            var keys = clip.TextKeys.SelectMany(key => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).ToArray();
                            actions.Add(new { first, group, clip.Sequence.CycleType, keys });
                            if (group.StartsWith("attack", StringComparison.Ordinal) && !keys.Any(FalloutWeaponAnimationTimeline.DischargesWeapon) &&
                                !(held.Automatic && held.AttackAnimation == 74 && clip.Sequence.CycleType == 0))
                                throw new NotSupportedException("Attack animation has no source Hit, Fire or Release event.");
                        }
                        catch (Exception failure) { failures.Add(new { stage = "action", first, group, error = failure.Message }); }
                    }
                }
                catch (Exception failure) { failures.Add(new { stage = "assembly", first, error = failure.Message }); }
                finally { actor?.Free(); }
            }
            var name = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "FULL").Data;
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                weapon = record.FormKey.ToString(),
                name = name.IsEmpty ? "" : FalloutDialogueTopic.Text(name.Span),
                type = source?.WeaponAnimationType,
                grip = source?.Grip,
                source?.Automatic,
                source?.AttackAnimation,
                source?.ReloadAnimation,
                source?.ClipSize,
                source?.AmmoUse,
                source?.HasAmmunitionSource,
                source?.Playable,
                source?.Embedded,
                playerAssemblyApplicable = source is { Playable: true, Embedded: false },
                pairs,
                actions,
                failures,
                boundary = "source-and-native-animation-coverage;ordinary-input-contact-damage-persistence-separate"
            }));
            writer.Flush(); total++; if (failures.Count != 0) failed++;
            if (total % 25 == 0) GD.Print($"OPENNV_WEAPON_COVERAGE_PROGRESS weapons={total} withFailures={failed}");
        }
        GD.Print($"OPENNV_WEAPON_COVERAGE_COMPLETE weapons={total} withFailures={failed} gameplayAcceptance=false output={output}");
    }
}
