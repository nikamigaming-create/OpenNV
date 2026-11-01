using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutCompassFrame NativeCompass(float stripWidth)
    {
        var player = _nativePlayer!;
        var position = NativePipBoyMapPosition(player);
        var rotation = GamebryoCoordinate.ReferenceEuler(player.GlobalBasis);
        Dictionary<FalloutFormKey, RuntimeNativeActorCombat>? actors = null;
        var sight = new Dictionary<FalloutFormKey, bool>();
        return _nativeReferences!.Compass(new(_nativeActiveCell!.Cell.FormKey,
            [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z]),
            player.UnitsToMeters, stripWidth, _nativeQuestState!, _nativeOpeningStageDriver!.EvaluateTargetCondition, Sight);

        bool? Sight(FalloutFormKey reference)
        {
            if (sight.TryGetValue(reference, out var cached)) return cached;
            // The cache belongs only to this compass publication. Never retain
            // a collision result across moving actors or a physics update.
            if (actors is null)
            {
                actors = [];
                var group = GetTree().GetNodesInGroup("OpenNVNativeCombatActors");
                foreach (var combat in group.OfType<RuntimeNativeActorCombat>())
                {
                    if (!combat.IsInsideTree() || combat.IsQueuedForDeletion() || combat.Dead || !combat.EffectiveEnabled) continue;
                    var key = combat.GetParent() switch
                    {
                        RuntimeNativeNpc npc => npc.Appearance.Reference,
                        RuntimeNativeCreature creature => creature.Appearance.Reference,
                        _ => null,
                    };
                    if (key is { } identity && !actors.TryAdd(identity, combat))
                        throw new InvalidOperationException($"Detection target {identity} has multiple live process owners.");
                }
            }
            if (!actors.TryGetValue(reference, out var target)) return null;
            if (target.GetParent() is not RuntimeNativeNpc npcTarget)
                throw new NotSupportedException("Creature detection requires its source bounds-height owner.");
            // Player LOS admits any clear candidate. Its third candidate is
            // always the source bounds quarter-height, even when cached bone
            // nodes replace the first two. One clear exact candidate suffices.
            var quarter = npcTarget.GlobalPosition + Vector3.Up *
                (npcTarget.SourceHeight * npcTarget.GlobalBasis.Scale.Y * player.UnitsToMeters / 4);
            var exclusions = new Godot.Collections.Array<Rid>(player.CombatCollisionRids);
            using var query = PhysicsRayQueryParameters3D.Create(player.DetectionOrigin, quarter,
                player.CollisionMask | player.CollisionLayer, exclusions);
            using var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
            var visible = hit.Count == 0 || hit["collider"].AsGodotObject() is Node collider && RuntimeNativeActorCombat.Find(collider) == target;
            sight.Add(reference, visible);
            return visible;
        }
    }
}
