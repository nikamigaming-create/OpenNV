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

    // Whole detached CELL staging never receives ExitTree. Retain every actor
    // while its native descendants still exist, then free even if one retention
    // fails. The caller must report the returned failure after restoring the
    // outgoing scene; disposal must not prevent rollback itself.
    internal static Exception? DiscardNativeStagedSubtree(Node? root)
    {
        if (root is null || !GodotObject.IsInstanceValid(root)) return null;
        Exception? failure = null;
        try { RetireNativeActorBindings(root); }
        catch (Exception error) { failure = error; }
        try { root.Free(); }
        catch (Exception error)
        {
            failure = failure is null ? error :
                new AggregateException("Staged CELL retirement and native disposal both failed.", failure, error);
        }
        return failure;
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
