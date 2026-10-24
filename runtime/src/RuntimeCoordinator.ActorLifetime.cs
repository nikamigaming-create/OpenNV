using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    internal static void RequireNativeActorPublication(Node root)
    {
        if (!root.IsInsideTree()) throw new InvalidDataException("Native actor publication has no live tree owner.");
        foreach (var npc in SceneGraph.NodeTraversal.SelfAndDescendants<RuntimeNativeNpc>(root))
            npc.RequireSourcePublication();
        foreach (var creature in SceneGraph.NodeTraversal.SelfAndDescendants<RuntimeNativeCreature>(root))
            creature.RequireSourcePublication();
    }

    internal static void RetireNativeActorBindings(Node root)
    {
        // Capture while transforms, physical bodies and attachment nodes still
        // belong to the scene. ExitTree runs after descendant teardown begins.
        Exception? failure = null;
        void Retire(Action retire)
        {
            try { retire(); }
            catch (Exception error) { failure ??= error; }
        }
        foreach (var combat in SceneGraph.NodeTraversal.SelfAndDescendants<RuntimeNativeActorCombat>(root))
            Retire(combat.RetireSourceBindings);
        foreach (var npc in SceneGraph.NodeTraversal.SelfAndDescendants<RuntimeNativeNpc>(root))
            Retire(npc.RetireSourceBindings);
        foreach (var creature in SceneGraph.NodeTraversal.SelfAndDescendants<RuntimeNativeCreature>(root))
            Retire(creature.RetireSourceBindings);
        if (failure is not null)
            throw new InvalidOperationException("Actor retirement could not retain every outgoing continuation.", failure);
    }

    internal static void DiscardNativeActor(Node3D actor, Node3D? previousActor)
    {
        if (ReferenceEquals(actor, previousActor))
            throw new InvalidOperationException("Actor construction cannot discard its previous live owner.");
        try
        {
            if (!GodotObject.IsInstanceValid(actor) || actor.IsInsideTree()) return;
            // Detached staging never receives ExitTree. Use the same owners in
            // child-first order before releasing the failed native subtree.
            void Retire(Action retire)
            {
                try { retire(); }
                catch (Exception error) { GD.PushError($"OPENNV_ACTOR_CONSTRUCTION_RETIRE_FAILED {error}"); }
            }
            foreach (var combat in actor.FindChildren("*", "", true, false).OfType<RuntimeNativeActorCombat>())
                Retire(combat.RetireSourceBindings);
            foreach (var sound in actor.FindChildren("*", "", true, false).OfType<NativeOwnedAnimationSoundPlayer>())
                Retire(sound.RetirePlayback);
            if (actor is RuntimeNativeNpc npc) Retire(npc.RetireSourceBindings);
            else if (actor is RuntimeNativeCreature creature) Retire(creature.RetireSourceBindings);
        }
        finally
        {
            try { if (GodotObject.IsInstanceValid(actor)) actor.Free(); }
            finally
            {
                // Only an actual still-live previous presentation can recover
                // its vacant callbacks. No source execution or state is replayed.
                if (previousActor is not null && GodotObject.IsInstanceValid(previousActor) &&
                    previousActor.IsInsideTree() && !previousActor.IsQueuedForDeletion())
                {
                    if (previousActor is RuntimeNativeNpc npc) npc.RestoreSourceBindings();
                    else if (previousActor is RuntimeNativeCreature creature) creature.RestoreSourceBindings();
                }
            }
        }
    }
}
