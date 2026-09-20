using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorCombatAudit
{
    private async Task ExerciseCompanionCombat(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutGlobalState globals, string actorHex, string targetHex, string destinationHex, bool emptyMagazine = false)
    {
        using var world = new FalloutReferenceWorld(records);
        var key = records.RuntimeFormKey(Convert.ToUInt32(actorHex, 16));
        var targetKey = records.RuntimeFormKey(Convert.ToUInt32(targetHex, 16));
        world.MoveTo(key, records.RuntimeFormKey(Convert.ToUInt32(destinationHex, 16)));
        var actorCell = world.ComposeResidency(FalloutCellSceneReader.Read(records, world.Placement(key).Cell));
        var targetCell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(targetKey))!.Value);
        world.LoadCell(actorCell); world.LoadCell(targetCell);
        world.InitializeActorTemplates(key, 1, globals); world.InitializeActorTemplates(targetKey, 1, globals);
        var state = world.Get(key); state.Enabled = true; world.SetPlayerTeammate(key, true);
        var targetState = world.Get(targetKey); targetState.Enabled = true;
        if (emptyMagazine)
        {
            // A separate resume fixture starts with an exhausted source magazine.
            // It must recharge and finish the fight, without granting ammunition.
            var item = world.Inventory(key, 1, globals).Contents.Items.Single(value => value.RecordType == "WEAP");
            var weapon = FalloutWeaponPresentation.Read(records, item.FormKey, false);
            if (weapon.Model is not null || weapon.NpcsUseAmmo || weapon.Ammunition.Count == 0)
                throw new InvalidOperationException("Recharge fixture requires an embedded ammo-free source weapon.");
            state.Engagement = new(targetKey, WeaponHandling: new(true,
                [new(weapon.Form, weapon.Ammunition[0], 0, UsesInventoryAmmo: false)]));
        }
        var actor = RuntimeNativeCreature.Create(records, content, actorCell.References.Single(value => value.FormKey == key), state, .0142875f);
        actor.ConfigureAi(records, new(records), world);
        var target = RuntimeNativeCreature.Create(records, content, targetCell.References.Single(value => value.FormKey == targetKey), targetState, .0142875f);
        actor.Position = new(0, .05f, 0); target.Position = new(0, .05f, -7);
        AddChild(actor); AddChild(target);
        actor.SetPhysicsProcess(false); target.SetPhysicsProcess(false);
        var player = new RuntimeNativePlayer(); AddChild(player);
        player.Configure(RuntimeConfiguration.Load(), new(Basis.Identity, new(5, .05f, 0)));
        player.CollisionLayer = 1; // This fixture's query mask is explicitly 1|2.
        player.SetProcess(false); player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
        var floor = new StaticBody3D { Position = new(0, -.05f, -5) };
        var floorShape = new CollisionShape3D { Shape = new BoxShape3D { Size = new(40, .1f, 40) } };
        floorShape.SetMeta("opennv_havok_material", 0u); // Explicit stone material for this synthetic floor.
        floor.AddChild(floorShape); AddChild(floor);
        var context = new NativeActorCombatContext(() => player, () => new(1, 200, 200, 70, 70, 0, 100),
            (_, _) => throw new InvalidOperationException("Companion fixture hit its player."), (_, to) => [to],
            _ => true, () => 1, globals, .4f, 9.81f);
        try
        {
            RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, 2);
            RuntimeNativeActorContacts.Configure(target, target.Skeleton, 2);
            actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath, world, state, records, content, 2, 3, context);
            target.Combat = RuntimeNativeActorCombat.Attach(target, target.Skeleton, target.Appearance.SkeletonPath, world, targetState, records, content, 2, 3, context);
            // A marked physics fixture: the opponent's aggression supplies the
            // player threat; the companion must independently select and hit it.
            targetState.Engagement = new(records.RuntimeFormKey(0x14));
            target.Combat.SetPhysicsProcess(false);
            actor.Combat.SetPhysicsProcess(false);
            var before = world.Health(targetKey).Current;
            if (!emptyMagazine)
            {
                player.Position = new(0, .05f, -2);
                for (var frame = 0; frame < 180; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    actor.Combat._PhysicsProcess(1d / 60);
                    if (actor.Combat.EngagementError is { } blockedError) throw new InvalidOperationException(blockedError);
                }
                if (world.Health(targetKey).Current != before ||
                    !JsonSerializer.Serialize(actor.Combat.Observation).Contains("held-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Companion fired through its obstructing player: " + JsonSerializer.Serialize(actor.Combat.Observation));
                player.Position = new(5, .05f, 0);
            }
            for (var frame = 0; frame < 1800 && !target.Combat.Dead; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor.Combat._PhysicsProcess(1d / 60);
                if (actor.Combat.EngagementError is { } error) throw new InvalidOperationException(error);
            }
            if (!target.Combat.Dead || world.Health(targetKey).Current >= before || world.Get(targetKey).Injury?.Killer != key)
                throw new InvalidOperationException("Companion did not kill the source-contact target: " + JsonSerializer.Serialize(actor.Combat.Observation));
            actor.Combat._PhysicsProcess(1d / 60);
            if (actor.Combat.OwnsPose) throw new InvalidOperationException("Ended companion fight retained combat pose ownership.");
            GD.Print($"OPENNV_NATIVE_COMPANION_COMBAT_PASS companion={key} target={targetKey} health={before:R}->{world.Health(targetKey).Current:R} " +
                $"sourceWeapon=true sourceContacts=true assistance={!emptyMagazine} emptyMagazineResume={emptyMagazine} " +
                "combatEnded=true friendlyLineRefused=true fixture=synthetic-floor ordinaryGameplay=separate");
        }
        finally { actor.Free(); target.Free(); player.Free(); floor.Free(); }
    }
}
