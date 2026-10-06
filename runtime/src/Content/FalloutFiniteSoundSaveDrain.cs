namespace OpenNV.Runtime.Content;

internal interface IFalloutFiniteSoundSaveDrainLease : IDisposable
{
    FalloutFiniteSoundVoice Voice { get; }
    bool ObserveFinished();
    void Activate();
}

internal sealed class FalloutFiniteSoundSaveDrainInvalidatedException(string message) : InvalidOperationException(message) { }

// Preparing a lease proves a fixed set. Activating it changes only those audio
// nodes; neither operation supplies a source completion or a capture exemption.
internal sealed class FalloutFiniteSoundSaveDrain(
    IReadOnlyList<IFalloutFiniteSoundSaveDrainLease> leases, Action validateRegistry, Action releaseRegistry) : IDisposable
{
    private bool _disposed, _active;
    internal IReadOnlyList<FalloutFiniteSoundVoice> Voices { get; } = Array.AsReadOnly(leases.Select(lease => lease.Voice).ToArray());

    internal IReadOnlyList<FalloutFiniteSoundVoice> ObservePending()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        validateRegistry();
        return Array.AsReadOnly(leases.Where(lease => !lease.ObserveFinished()).Select(lease => lease.Voice).ToArray());
    }

    internal void Activate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_active) throw new InvalidOperationException("Finite save drain was activated twice.");
        _ = ObservePending();
        foreach (var lease in leases) lease.Activate();
        _active = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception> errors = [];
        try
        {
            foreach (var lease in leases.Reverse())
            {
                try { lease.Dispose(); }
                catch (Exception error) { errors.Add(error); }
            }
        }
        finally { releaseRegistry(); }
        if (errors.Count != 0) throw new AggregateException("Finite save-drain cleanup failed.", errors);
    }
}
