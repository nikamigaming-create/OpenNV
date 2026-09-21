namespace OpenNV.Runtime.Content;

/// <summary>Source package idle order and the wait between completed selections.</summary>
internal sealed class FalloutIdleCollectionPlayback(FalloutIdleCollection source, FalloutIdleReplayState replay,
    Func<FalloutFormKey, bool> eligible, Func<uint, uint>? random = null)
{
    internal FalloutIdleCollectionPlayback(FalloutScriptPackage source, FalloutIdleReplayState replay,
        Func<FalloutFormKey, bool> eligible) : this(new FalloutIdleCollection(source.Form, source.IdleFlags, source.IdleTimer, source.Idles), replay, eligible) { }
    private int _cursor;
    private int _selectionCount;
    internal FalloutIdleCollection Source { get; } = source;
    internal double WaitSeconds { get; private set; }
    internal bool Complete { get; private set; }
    internal int Cursor => _cursor;

    internal FalloutFormKey? Select()
    {
        if (Complete || WaitSeconds > 0 || Source.Idles.Count == 0) return null;
        if (!Source.RunInSequence && Source.Idles.Count > 1 && random is null)
            throw new NotSupportedException($"Package {Source.Form} requires the authoritative random idle selection owner.");
        // Cancellation releases the pose, not the actor's source replay delay.
        // Filter in source order before indexing the eligible collection. One
        // ineligible entry must not starve a later eligible animation.
        var candidates = Source.Idles.Where(idle => replay.CanSelect(idle) && eligible(idle)).ToArray();
        if (candidates.Length == 0) return null;
        var next = !Source.RunInSequence && random is not null ? checked((int)random((uint)candidates.Length)) : _cursor >= candidates.Length ? 0 : _cursor;
        var idle = candidates[next];
        _selectionCount = candidates.Length;
        _cursor = next + 1;
        return idle;
    }

    internal void Finish()
    {
        if (_cursor == 0 || Complete) throw new InvalidOperationException("Package idle completion has no active selection.");
        if (Source.RunInSequence && _cursor < _selectionCount) return;
        Complete = Source.DoOnce;
        if (!Complete) WaitSeconds = Source.IdleTimer;
    }

    internal double AdvanceWait(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var consumed = Math.Min(WaitSeconds, seconds);
        WaitSeconds -= consumed;
        return seconds - consumed;
    }
}
