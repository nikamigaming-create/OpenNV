namespace OpenNV.Runtime.Gameplay.State;

// Fired continuations outlive menu cancellation and replacement of the current
// session. None is serialized yet, so every retained receipt blocks capture.
internal sealed class FalloutVatsPublications
{
    private readonly HashSet<FalloutVatsSession.Publication> _pending = [];
    internal int Count => _pending.Count;
    internal string? SaveBlocker => Count == 0 ? null : "vats-projectile-continuation";

    internal FalloutVatsSession.Publication Begin(FalloutVatsSession session)
    {
        var publication = session.BeginPublication();
        _pending.Add(publication);
        return publication;
    }

    internal void Finish(FalloutVatsSession.Publication publication)
    {
        if (!_pending.Contains(publication))
            throw new InvalidOperationException("VATS publication has no retained capture owner or finished twice.");
        publication.Owner.FinishPublication(publication);
        _pending.Remove(publication);
    }
}
