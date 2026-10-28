namespace OpenNV.Runtime.Gameplay.State;

// A movie may start inside the loader's temporary pause. Once loading ends,
// that inherited baseline is replaced by the pause that loading interrupted.
internal sealed class RuntimeMoviePause
{
    private bool _pausedBefore;
    internal bool Active { get; private set; }

    internal void Acquire(bool pausedBefore)
    {
        if (Active) throw new InvalidOperationException("Movie already owns the world pause.");
        _pausedBefore = pausedBefore;
        Active = true;
    }

    internal bool RebaseLoadingPause(bool pausedAfterLoading)
    {
        if (!Active) return false;
        _pausedBefore = pausedAfterLoading;
        return true;
    }

    internal bool? Release()
    {
        if (!Active) return null;
        Active = false;
        return _pausedBefore;
    }
}
