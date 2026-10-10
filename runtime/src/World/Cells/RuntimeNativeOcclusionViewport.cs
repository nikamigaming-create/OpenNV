using Godot;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.World.Cells;

internal static class RuntimeNativeOcclusionViewport
{
    private sealed class State(bool previous)
    { internal readonly bool Previous = previous; internal int References; }
    private static readonly ConditionalWeakTable<Viewport, State> States = new();

    internal static IDisposable Acquire(Viewport viewport)
    {
        if (!GodotObject.IsInstanceValid(viewport) || !viewport.IsInsideTree() || viewport.IsQueuedForDeletion())
            throw new InvalidDataException("Source culling has no living native viewport.");
        var state = States.GetValue(viewport, current => new(current.UseOcclusionCulling));
        if (state.References != 0 && !viewport.UseOcclusionCulling)
            throw new InvalidOperationException("Native viewport culling changed during its actual source lease.");
        viewport.UseOcclusionCulling = true; state.References++;
        return new Lease(viewport, state);
    }

    private sealed class Lease(Viewport viewport, State state) : IDisposable
    {
        private bool _retired;
        public void Dispose()
        {
            if (_retired) return;
            if (state.References <= 0) throw new InvalidOperationException("Native source culling lease was retired twice.");
            _retired = true;
            if (--state.References != 0) return;
            States.Remove(viewport);
            if (GodotObject.IsInstanceValid(viewport) && !viewport.IsQueuedForDeletion())
            {
                var changed = !viewport.UseOcclusionCulling;
                viewport.UseOcclusionCulling = state.Previous;
                if (changed) throw new InvalidOperationException("Native source culling was changed before retirement.");
            }
        }
    }
}
