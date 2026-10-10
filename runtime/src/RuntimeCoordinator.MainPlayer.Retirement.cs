namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void RetireActualNativeMainPlayerConsumers()
    {
        RequireNativeMainDeliveryClosed();
        var failures = new List<Exception>();
        try
        {
            _nativeMainPlayerLease?.Dispose();
            _nativeMainPlayerLease = null; _nativeMainPlayerConsumers = null;
        }
        catch (Exception failure) { failures.Add(failure); }
        if (_nativeMainPlayerLease is not null)
            throw new AggregateException("Main Player still owns its native consumer lease; retain its scene and conversion providers.", failures);
        try { _nativePlayer?.RetireStandalonePlayerScene(); }
        catch (Exception failure) { failures.Add(failure); }
        try
        {
            if (_nativeMainPlayerReplacement is not null && _nativeMainPlayerBoundWorld is { } world)
                world.PlayerMoves.SourcePending.Replaced -= _nativeMainPlayerReplacement;
            _nativeMainPlayerReplacement = null;
        }
        catch (Exception failure) { failures.Add(failure); }
        try { RetireNativeCallingThreadFistp(); }
        catch (Exception failure) { failures.Add(failure); }
        try
        {
            if (!_nativeMainPlayerCancellationDisposed)
            { _nativeMainPlayerCancellation.Dispose(); _nativeMainPlayerCancellationDisposed = true; }
        }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0)
            throw new AggregateException("Main Player native consumers retained independent retirement failures.", failures);
        _nativeMainPlayerBoundWorld = null;
    }
}
