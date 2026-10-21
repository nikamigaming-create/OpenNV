using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorCombatAudit
{
    private async Task ExerciseRetaliation(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutGlobalState globals, string actorHex, string targetHex, bool unarmed, bool ambient, bool packageMotion = false,
        bool hitReaction = false, bool radiusWarning = false, bool sloped = false, bool attackRangeCold = false)
    {
        using var world = new FalloutReferenceWorld(records);
        var key = records.RuntimeFormKey(Convert.ToUInt32(actorHex, 16));
        var targetKey = records.RuntimeFormKey(Convert.ToUInt32(targetHex, 16));
        var actors = new List<CharacterBody3D>();
        var loadedCells = new HashSet<(FalloutReferenceWorld World, FalloutFormKey Cell)>();
        RuntimeNativePlayer? player = null;
        if (!ambient)
        {
            player = new(); AddChild(player);
            player.Configure(RuntimeConfiguration.Load(), new(Basis.Identity, new(5, .05f, 0)));
            player.CollisionLayer = 1;
            player.SetProcess(false); player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
        }
        var floor = new StaticBody3D { Position = new(0, -.05f, 0) };
        if (sloped) floor.Rotation = new(Mathf.DegToRad(30), 0, 0);
        var floorShape = new CollisionShape3D { Shape = new BoxShape3D { Size = new(40, .1f, 40) } };
        floorShape.SetMeta("opennv_havok_material", 0u);
        floor.AddChild(floorShape); AddChild(floor);
        var collisionResident = true;
        var context = new NativeActorCombatContext(() => player, () => new(1, 200, 200, 70, 70, 0, 100),
            (_, _) => throw new InvalidOperationException("Retaliation fixture attacked the uninvolved player."),
            (_, to) => [to], _ => collisionResident, () => 1, globals, .4f, 9.81f);
        try
        {
            var attacker = Create(key, Vector3.Zero);
            var warningRadius = radiusWarning ? FalloutActorThreat.Read(records, world.Get(key).Base, world.Get(key).Templates).Radius * .0142875f : 0;
            var target = Create(targetKey, sloped ? new(0, 5 * Mathf.Tan(Mathf.DegToRad(30)), -5) :
                new(0, 0, radiusWarning ? -warningRadius * .75f : -1.5f));
            await Frames(3);
            if (packageMotion)
            {
                if (player is not null || !attacker.PackageMovementReady)
                    throw new InvalidOperationException("Resident package movement still requires a player object.");
                collisionResident = false;
                if (attacker.PackageMovementReady) throw new InvalidOperationException("Unloaded collision admitted package movement.");
                collisionResident = true;
                var state = world.Get(key);
                state.Enabled = false;
                if (attacker.PackageMovementReady) throw new InvalidOperationException("Disabled actor admitted package movement.");
                state.Enabled = true;
                var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(state.Base), 32, state.Templates);
                var link = owner.ReadSubrecords().First(field => field.Signature == "PKID");
                var package = records.GetEffective(owner.Plugin.AdjustFormId(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(link.Data.Span)));
                var start = actors[0].GlobalPosition;
                var destination = start + new Vector3(-3, 0, 0);
                for (var frame = 0; frame < 240; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    attacker.AdvancePackageMotion(package, destination, .25f, false, 1d / 60);
                }
                if (actors[0].GlobalPosition.DistanceTo(start) < .5f || attacker.PackageMotion is null)
                    throw new InvalidOperationException("Playerless package motion did not advance source root motion and native collision.");
                GD.Print($"OPENNV_NATIVE_PLAYERLESS_PACKAGE_PASS actor={key} package={package.FormKey} moved={actors[0].GlobalPosition.DistanceTo(start):R} sourceAnimation=true nativeCollision=true residencyGuard=true disabledGuard=true playerPresent=false fixture=synthetic-floor");
                return;
            }
            var before = world.Health(targetKey).Current;
            if (hitReaction)
            {
                await ExerciseHitReaction(records, world, globals, attacker, actors[0], key, targetKey);
            }
            else if (radiusWarning)
            {
                var state = world.Get(key);
                var threat = FalloutActorThreat.Read(records, state.Base, state.Templates);
                var relation = world.ActorRelation(key, targetKey);
                if (!threat.Warns(relation) || threat.Initiates(relation))
                    throw new InvalidOperationException("Warning fixture requires a source radius warning without sight-attack eligibility.");
                attacker._PhysicsProcess(1d / 60);
                if (state.AggroWarning is not { } warning || state.Engagement is not null)
                    throw new InvalidOperationException("Outer source radius did not enter a warning before combat.");
                var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(state.Capture()))!;
                using (var cold = new FalloutReferenceWorld(records))
                {
                    cold.Restore([saved]);
                    if (JsonSerializer.Serialize(cold.Get(key).AggroWarning) != JsonSerializer.Serialize(warning))
                        throw new InvalidOperationException("Cold warning changed its target, count, idle identity or clocks.");
                }
                var interval = FalloutGameSettingFloats.Read(records, "fAITrespassWarningTimer");
                var maximum = checked((int)FalloutGameSettingIntegers.Read(records, "iGuardWarnings"));
                var elapsed = 0d;
                while (state.Engagement is null && elapsed <= interval * maximum + .5)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    attacker._PhysicsProcess(.25); elapsed += .25;
                    if (attacker.EngagementError is { } error) throw new InvalidOperationException(error);
                }
                if (state.Engagement?.Target != targetKey || elapsed < interval * maximum - .25 || state.AggroWarning is not null)
                    throw new InvalidOperationException("Source warning intervals did not hand off to ordinary combat exactly once.");
                GD.Print($"OPENNV_NATIVE_RADIUS_WARNING_PASS actor={key} target={targetKey} radius={warningRadius:R} interval={interval:R} warnings={maximum} " +
                    "outerWarning=true coldState=true timedCombat=true fixture=source-actors-synthetic-floor-and-accelerated-clock ordinaryGameplay=separate");
            }
            else if (ambient)
            {
                var state = world.Get(key);
                var threat = FalloutActorThreat.Read(records, state.Base, state.Templates);
                var relation = world.ActorRelation(key, targetKey);
                var initiates = threat.Initiates(relation) || threat.Warns(relation) &&
                    actors[0].GlobalPosition.DistanceTo(actors[1].GlobalPosition) <= threat.Radius * .0142875f / 2;
                GD.Print($"OPENNV_AMBIENT_SOURCE actor={key} target={targetKey} aggression={threat.Aggression} confidence={threat.Confidence} relation={relation} initiates={initiates}");
                world.ChangeActorValue(key, "aggression", "setav", 0);
                for (var frame = 0; frame < 30; frame++) attacker._PhysicsProcess(1d / 60);
                if (state.Engagement is not null) throw new InvalidOperationException("Unaggressive actor initiated combat.");
                world.ChangeActorValue(key, "aggression", "setav", threat.Aggression);
                var wall = new StaticBody3D { Position = new(0, 2, -.75f) };
                wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(10, 4, .2f) } }); AddChild(wall);
                try
                {
                    await Frames(3);
                    for (var frame = 0; frame < 30; frame++) attacker._PhysicsProcess(1d / 60);
                    if (state.Engagement is not null) throw new InvalidOperationException("Ambient actor acquired through an opaque wall.");
                }
                finally { wall.Free(); }
                await Frames(3);
                if (!initiates)
                {
                    var allyKey = world.ResidentInstances.Where(instance => instance.Reference != key && instance.Base == state.Base)
                        .Select(instance => instance.Reference).FirstOrDefault();
                    if (allyKey == default) throw new InvalidOperationException("Ambient assistance fixture requires a second source reference of the same actor base.");
                    _ = Create(allyKey, new(-3, 0, 0));
                    await Frames(3);
                    for (var frame = 0; frame < 30; frame++) attacker._PhysicsProcess(1d / 60);
                    if (state.Engagement is not null) throw new InvalidOperationException("Source relationship forbids initiating against this actor.");
                    // The other actor must choose combat itself and land a real
                    // source attack before this actor has anything to retaliate
                    // against. No input, Hit call or engagement injection.
                    for (var frame = 0; frame < 900 && state.Engagement is null; frame++)
                    {
                        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                        target._PhysicsProcess(1d / 60);
                        if (target.EngagementError is { } error) throw new InvalidOperationException(error);
                    }
                    if (state.Engagement?.Target != targetKey)
                        throw new InvalidOperationException("Neither source actor initiated the ambient encounter.");
                    var helperEngagement = world.Get(allyKey).Engagement?.Target;
                    if (threat.Assistance > 0 ? helperEngagement != targetKey : helperEngagement is not null)
                        throw new InvalidOperationException("Actor assistance differs from its source assistance/faction setting.");
                    GD.Print($"OPENNV_AMBIENT_ASSISTANCE_PASS helper={allyKey} assistance={threat.Assistance} target={helperEngagement}");
                }
            }
            else
            {
                var contact = actors[0].FindChildren("*", "Area3D", true, false).OfType<Area3D>()
                    .First(area => attacker.HitPart(area) == 0);
                attacker.Hit(contact, new(.1f, 1, 100, 1), targetKey, 1, globals);
                if (world.Get(key).Engagement?.Target != targetKey)
                    throw new InvalidOperationException("A nonfatal hit did not provoke the actual attacking reference.");
                if (actors[0] is RuntimeNativeNpc npc)
                {
                    var owner = records.GetEffective(world.Get(key).Base);
                    var procedure = new FalloutCondition(owner, 0, 0, 143, 0, 0, 0, 0);
                    var package = new FalloutCondition(owner, 0, 0, 110, 0, 0, 0, 0);
                    if (npc.EvaluateAiCondition(procedure) != 13 || npc.EvaluateAiCondition(package) != 5)
                        throw new InvalidOperationException("Combat did not own its script-visible procedure and package before reaction selection.");
                }
            }
            if (attackRangeCold)
                await ExerciseAttackRangeCold(records, world, key, targetKey, attacker, actors, Create);
            for (var frame = 0; frame < 900 && world.Health(targetKey).Current == before; ++frame)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                attacker._PhysicsProcess(1d / 60);
                if (attacker.EngagementError is { } error) throw new InvalidOperationException(error);
            }
            if (world.Health(targetKey).Current >= before)
                throw new InvalidOperationException("Source actor did not retaliate through source Hit events and anatomy: " +
                    JsonSerializer.Serialize(attacker.Observation));
            GD.Print($"OPENNV_NATIVE_{(ambient ? "AMBIENT_COMBAT" : "RETALIATION")}_PASS actor={key} target={targetKey} playerPresent={player is not null} unarmedFixture={unarmed} sloped={sloped} " +
                $"health={before:R}->{world.Health(targetKey).Current:R} sourceHit=true fixture=synthetic-floor ordinaryGameplay=separate");
        }
        finally
        {
            foreach (var actor in actors) actor.Free();
            player?.Free(); floor.Free();
        }

        RuntimeNativeActorCombat Create(FalloutFormKey reference, Vector3 position, FalloutReferenceWorld? owner = null)
        {
            owner ??= world;
            var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(reference))!.Value);
            if (loadedCells.Add((owner, cell.Cell.FormKey))) owner.LoadCell(cell);
            owner.InitializeActorTemplates(reference, 1, globals);
            var state = owner.Get(reference);
            _ = owner.IsEnabled(reference); // Validate the source chain before walking it.
            var enableRoot = state; var desiredEnable = true;
            while (enableRoot.EnableParent is { } parent)
            {
                desiredEnable ^= parent.Opposite;
                enableRoot = owner.Get(parent.Reference);
            }
            // Enable the source owner without rewriting a child's local flag.
            enableRoot.Enabled = desiredEnable;
            if (unarmed && reference == key)
            {
                var inventory = owner.Inventory(reference, 1, globals).Contents;
                foreach (var weapon in inventory.Items.Where(item => item.RecordType == "WEAP").ToArray())
                    inventory.Remove(weapon.FormKey, weapon.Count, silent: true);
            }
            var placed = cell.References.Single(value => value.FormKey == reference);
            GD.Print("OPENNV_NATIVE_ACTOR_SOURCE " + JsonSerializer.Serialize(new
            {
                reference,
                placed.Cell,
                placed.Base,
                sourcePositionGameUnits = placed.Position,
                sourceRotationRadians = placed.RotationRadians,
                fixturePositionMeters = new[] { position.X, position.Y, position.Z },
                fixture = "synthetic-floor;source-placement-not-used"
            }));
            CharacterBody3D actor;
            OpenNV.Runtime.Formats.Gamebryo.RuntimeNativeNifSkeleton skeleton;
            string skeletonPath;
            if (records.GetEffective(state.Base).Signature == "NPC_")
            {
                var npc = RuntimeNativeNpc.Create(records, content, placed, .0142875f,
                    (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.3f, .3f, .3f)),
                    equippedArmor: owner.EquippedArmor(reference, 1, globals), selection: state.Templates);
                actor = npc; skeleton = npc.Skeleton; skeletonPath = npc.Appearance.SkeletonPath;
            }
            else
            {
                var creature = RuntimeNativeCreature.Create(records, content, placed, state, .0142875f);
                actor = creature; skeleton = creature.Skeleton; skeletonPath = creature.Appearance.SkeletonPath;
            }
            actors.Add(actor); actor.Position = position + Vector3.Up * .05f;
            AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
            RuntimeNativeActorContacts.Configure(actor, skeleton, 2);
            var combat = RuntimeNativeActorCombat.Attach(actor, skeleton, skeletonPath, owner, state, records, content, 2, 3, context);
            if (!combat.CanReceiveExplosionDamage) throw new InvalidOperationException("Effective enabled actor rejected explosion eligibility.");
            enableRoot.Enabled = !desiredEnable;
            if (combat.CanReceiveExplosionDamage) throw new InvalidOperationException("Effective disabled actor admitted explosion eligibility.");
            enableRoot.Enabled = desiredEnable;
            if (actor is RuntimeNativeNpc humanoid) humanoid.Combat = combat;
            else ((RuntimeNativeCreature)actor).Combat = combat;
            combat.SetPhysicsProcess(false);
            return combat;
        }
    }
}
