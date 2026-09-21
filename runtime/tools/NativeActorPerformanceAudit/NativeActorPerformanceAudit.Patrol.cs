using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private void ExercisePatrolIdles(string root, string referenceHex, string packageHex)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var key = records.RuntimeFormKey(Convert.ToUInt32(referenceHex, 16));
        var package = records.GetEffective(records.RuntimeFormKey(Convert.ToUInt32(packageHex, 16)));
        var route = FalloutPatrolRoute.Read(records, package, key);
        var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(key))!.Value);
        var placed = cell.References.Single(value => value.FormKey == key);
        var globals = FalloutGlobalState.Read(records);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(cell);
        world.InitializeActorTemplates(key, 1, globals);
        var actor = RuntimeNativeNpc.Create(records, content, placed, .0142875f,
            (_, _, _, _) => new StandardMaterial3D(), world.EquippedArmor(key, 1, globals), world.Get(key).Templates);
        AddChild(actor);
        var player = new RuntimeNativePlayer(); AddChild(player);
        player.SetProcess(false); player.SetPhysicsProcess(false);
        var context = new NativeActorCombatContext(() => player, () => new(1, 200, 200, 70, 70, 0, 100),
            (_, _) => throw new InvalidOperationException("Patrol idle component check attacked."),
            (_, to) => [to], _ => true, () => 1, globals, .4f, 9.81f);
        try
        {
            actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                world, world.Get(key), records, content, 2, 3, context);
            actor.Combat.SetPhysicsProcess(false);
            // A component check of every source marker clip against the actual
            // inventory weapon. This is not ordinary movement or pixel proof.
            actor.Combat.AdvancePackageMotion(package, actor.GlobalPosition, 1, false, 0, weaponDrawn: route.WeaponDrawn);
            if (actor.Combat.AnimationWeapon is null) throw new InvalidDataException("Patrol weapon was not attached.");
            var idles = route.Points.SelectMany(point => point.Idles?.Idles ?? []).Distinct().ToArray();
            foreach (var form in idles)
            {
                actor.BeginResponseAnimation(records, form);
                var before = BonePoses(actor);
                actor._Process(.7);
                if (actor.AnimationError is not null || BonePoses(actor).SequenceEqual(before))
                    throw new InvalidDataException($"Patrol IDLE {form} did not animate: {actor.AnimationError}");
                actor.EndResponseAnimation();
                GD.Print($"OPENNV_PATROL_IDLE_BINDING_PASS reference={key} idle={form} weapon={actor.Combat.WeaponAnimationType}");
            }
            GD.Print($"OPENNV_PATROL_IDLE_AUDIT_PASS reference={key} clips={idles.Length} pixels=unverified");
        }
        finally { actor.Free(); player.Free(); }
    }
}
