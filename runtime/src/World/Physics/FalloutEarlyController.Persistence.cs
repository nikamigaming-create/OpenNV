namespace OpenNV.Runtime.World.Physics;

internal sealed partial class FalloutEarlyController
{
    internal void RestoreColdFields(FalloutEarlyControllerSnapshot saved)
    {
        // This is current state restoration. It runs no original helper,
        // scalar/reset callback or source constructor store sequence.
        _scalar = saved.ResetScalarBits; _flags = saved.Flags; _selector = saved.PlayerPropertySelector;
        _actorScaleBits = saved.ActorScaleBits; _failure = saved.Failure;
        _stores.AddRange(saved.Stores); _resets.AddRange(saved.Resets);
        _sequence = _stores.Count == 0 ? 0 : _stores[^1].Sequence;
    }
}
