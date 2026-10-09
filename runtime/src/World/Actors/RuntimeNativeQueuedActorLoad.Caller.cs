using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeQueuedActorLoad
{
    // A completed CLR Task retains its Result. Move the actual pure decoder
    // payload once, then release the slot when assembly takes ownership.
    // Native publication still retains its exact separate queue/read lease.
    private sealed class PreparedOwnedInput(FalloutNpcPreparedGeometry value)
    {
        private FalloutNpcPreparedGeometry? _value = value;
        internal FalloutNpcPreparedGeometry Take()
        {
            var actual = _value ?? throw new InvalidOperationException("Actual native decoder payload was already consumed or discarded.");
            _value = null; return actual;
        }
        internal void Discard() => _value = null;
    }
    internal Guid Identity => _read.Identity;
    internal RuntimeNativeNpc? TransferredActor => _transferred;
    internal bool Published => _published;
    internal bool Retired => _disposed;
    internal bool CanRetireNow => _read.ReadTask.IsCompleted &&
        (_transferred is null || !GodotObject.IsInstanceValid(_transferred));
    internal void RequestCancellation()
    {
        RequireCurrent(); _read.RequestCancellation();
    }
    internal void RetainCallerFailure(Exception error)
    {
        RequireThread(); ArgumentNullException.ThrowIfNull(error);
        _world.RetainSourceQueuedNativeCallerFailure(_read.Identity, error);
    }
    internal void RetireDetachedTransferredActor()
    {
        RequireThread();
        if (!_published && _transferred is { } actor && GodotObject.IsInstanceValid(actor) && actor.GetParent() is null)
            actor.Free();
    }
    private void DiscardReturnedDecoderInput()
    {
        if (_read.ReadTask.Status == TaskStatus.RanToCompletion) _read.ReadTask.GetAwaiter().GetResult().Discard();
    }
}
