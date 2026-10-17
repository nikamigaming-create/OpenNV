using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private HitDispatcherLease? _beforeActorHit;

    private sealed class HitDispatcherLease(FalloutReferenceWorld world, Action<FalloutFormKey> dispatch) : IDisposable
    {
        internal void Dispatch(FalloutFormKey actor) => dispatch(actor);

        public void Dispose()
        {
            if (ReferenceEquals(world._beforeActorHit, this)) world._beforeActorHit = null;
        }
    }

    internal IDisposable BindHitDispatcher(Action<FalloutFormKey> dispatch)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(dispatch);
        // Activating a cell replaces a suspended warm cell's dispatcher. Its
        // later retirement must not disconnect the newly active executor.
        var lease = new HitDispatcherLease(this, dispatch);
        _beforeActorHit = lease;
        return lease;
    }

    internal void BeforeActorHit(FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FalloutReferenceHitEvents.RequireActor(records, actor);
        if (actor != records.RuntimeFormKey(0x14) && !IsResident(actor))
            throw new InvalidOperationException("An actor hit requires its current resident contact owner.");
        _beforeActorHit?.Dispatch(actor);
    }
}
