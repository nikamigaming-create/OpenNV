namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed class LiveHarnessPublicationCadence
{
    internal const ulong IntervalMilliseconds = 250;
    private ulong _lastScheduled;

    internal bool Due(ulong now)
    {
        if (now < _lastScheduled) throw new InvalidDataException("Live-state publication clock regressed.");
        return now - _lastScheduled >= IntervalMilliseconds;
    }

    internal void Scheduled(ulong now)
    {
        if (now < _lastScheduled) throw new InvalidDataException("Live-state publication schedule regressed.");
        _lastScheduled = now;
    }
}
