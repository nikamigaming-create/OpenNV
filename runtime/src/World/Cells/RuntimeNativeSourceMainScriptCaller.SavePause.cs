using System.Runtime.ExceptionServices;
using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeSourceMainScriptCaller
{
    internal SavePause AcquireSavePause()
    {
        RequireSaveDeliveryAlive();
        ObserveReturnedMainCall();
        RequireSaveDeliveryAlive();
        return new(this, _saveDelivery.Acquire(_activeMainCall));
    }

    private void RequireSaveDeliveryAlive()
    {
        RequireDeliveryThread();
        if (!_started || _retired || _stopMainDelivery || !IsInstanceValid(this) ||
            !IsInsideTree() || IsQueuedForDeletion() || _callerLease is null || _fadeLease is null)
            throw new InvalidOperationException("Save preparation lost its actual Main delivery owner.");
        if (_failure is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
        _world.RequireNoFunctionalFailure();
    }

    internal sealed class SavePause : IDisposable
    {
        private readonly RuntimeNativeSourceMainScriptCaller _owner;
        private readonly RuntimeSourceDeliveryPause.Lease _delivery;
        internal SavePause(RuntimeNativeSourceMainScriptCaller owner, RuntimeSourceDeliveryPause.Lease delivery)
        { _owner = owner; _delivery = delivery; }
        internal bool Drained
        {
            get { _owner.RequireSaveDeliveryAlive(); return _delivery.Drained; }
        }
        internal void RequireReturned()
        {
            _owner.RequireSaveDeliveryAlive();
            try { _delivery.RequireReturned(); }
            catch (Exception error) { _owner.ObserveReturnedMainCall(); ExceptionDispatchInfo.Capture(error).Throw(); }
            _owner.ObserveReturnedMainCall();
            _owner.RequireSaveDeliveryAlive();
        }
        public void Dispose() => _delivery.Dispose();
    }
}
