using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutHudEventKind { ItemAdded, Message, ItemRemoved, ObjectiveDisplayed, ObjectiveCompleted, ChallengeProgress, ChallengeCompleted, RadioDiscovered }

// The event retains source identity and command order. Text, icons and timing
// are resolved from the winning graph when presented, never baked into saves.
internal sealed record FalloutHudEvent(FalloutHudEventKind Kind, FalloutFormKey Source, int Count,
    FalloutFormKey? Quest = null, FalloutFormKey? Script = null, uint? ObjectiveIndex = null);
internal sealed record FalloutHudNotice(long Ordinal, FalloutHudEvent Event);
internal sealed record FalloutHudNotificationsSnapshot(long LastOrdinal, FalloutHudNotice? Current,
    double Elapsed, IReadOnlyList<FalloutHudNotice> Pending, bool QuestUpdateCancellationRequested = false);

internal sealed class FalloutHudNotifications
{
    private readonly Queue<FalloutHudNotice> _pending = [];
    private long _ordinal;
    private bool _questUpdateCancellationRequested;
    internal FalloutHudNotice? Current { get; private set; }
    internal double Elapsed { get; private set; }
    internal FalloutHudNotificationsSnapshot Capture() => new(_ordinal, Current, Elapsed, _pending.ToArray(), _questUpdateCancellationRequested);

    internal void RequestQuestUpdateCancellation() => _questUpdateCancellationRequested = true;

    // The script command requests cancellation; the next HUD update consumes
    // it. Loading hides quest tiles but preserves their queued notices.
    internal bool ConsumeQuestUpdateCancellation(bool loading)
    {
        if (!_questUpdateCancellationRequested) return false;
        _questUpdateCancellationRequested = false;
        if (loading) return true;
        if (Current is { } current && IsQuestUpdate(current.Event))
        {
            Current = null;
            Elapsed = 0;
        }
        var retained = _pending.Where(notice => !IsQuestUpdate(notice.Event)).ToArray();
        _pending.Clear();
        foreach (var notice in retained) _pending.Enqueue(notice);
        return true;
    }

    private static bool IsQuestUpdate(FalloutHudEvent value) =>
        value.Kind is FalloutHudEventKind.ObjectiveDisplayed or FalloutHudEventKind.ObjectiveCompleted;

    internal static void Validate(IReadOnlyList<FalloutHudEvent> events)
    {
        if (events.Any(value => value.Source.ObjectId == 0 || string.IsNullOrWhiteSpace(value.Source.OwnerPlugin) ||
            (value.Kind switch
            {
                FalloutHudEventKind.ItemAdded or FalloutHudEventKind.ItemRemoved => value.Count <= 0 || value.ObjectiveIndex is not null,
                FalloutHudEventKind.ChallengeProgress or FalloutHudEventKind.ChallengeCompleted => value.Count <= 0 || value.ObjectiveIndex is not null,
                FalloutHudEventKind.Message or FalloutHudEventKind.RadioDiscovered => value.Count != 0 || value.ObjectiveIndex is not null,
                FalloutHudEventKind.ObjectiveDisplayed or FalloutHudEventKind.ObjectiveCompleted => value.Count != 0 || value.ObjectiveIndex is null,
                _ => true,
            })))
            throw new InvalidDataException("HUD event has invalid source identity, kind or count.");
    }

    internal void Publish(IReadOnlyList<FalloutHudEvent> events)
    {
        RequirePublish(events);
        foreach (var value in events) _pending.Enqueue(new(++_ordinal, value));
    }

    internal void RequirePublish(IReadOnlyList<FalloutHudEvent> events)
    {
        Validate(events);
        _ = checked(_ordinal + events.Count);
    }

    internal void Advance(double seconds, Func<FalloutHudEvent, double> duration)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Current is not null)
        {
            var lifetime = duration(Current.Event);
            if (!double.IsFinite(lifetime) || lifetime <= 0) throw new InvalidDataException("HUD notice has no finite positive lifetime.");
            Elapsed += seconds;
            if (Elapsed < lifetime) return;
            Current = null;
        }
        // Each newly presented event starts at its first visible publication;
        // a delayed draw never silently burns through unseen notices.
        Elapsed = 0;
        if (_pending.TryPeek(out var next))
        {
            var lifetime = duration(next.Event);
            if (!double.IsFinite(lifetime) || lifetime <= 0) throw new InvalidDataException("HUD notice has no finite positive lifetime.");
            Current = _pending.Dequeue();
        }
    }

    internal void Restore(FalloutHudNotificationsSnapshot snapshot)
    {
        if (_ordinal != 0 || Current is not null || _pending.Count != 0 || _questUpdateCancellationRequested)
            throw new InvalidOperationException("HUD restoration requires a fresh owner.");
        var notices = (snapshot.Current is null ? Enumerable.Empty<FalloutHudNotice>() : [snapshot.Current]).Concat(snapshot.Pending).ToArray();
        Validate(notices.Select(notice => notice.Event).ToArray());
        if (snapshot.LastOrdinal < 0 || !double.IsFinite(snapshot.Elapsed) || snapshot.Elapsed < 0 ||
            snapshot.Current is null && snapshot.Elapsed != 0 || notices.Any(notice => notice.Ordinal <= 0 || notice.Ordinal > snapshot.LastOrdinal) ||
            notices.Zip(notices.Skip(1)).Any(pair => pair.First.Ordinal >= pair.Second.Ordinal))
            throw new InvalidDataException("Saved HUD event order or clock is invalid.");
        _ordinal = snapshot.LastOrdinal;
        Current = snapshot.Current;
        Elapsed = snapshot.Elapsed;
        _questUpdateCancellationRequested = snapshot.QuestUpdateCancellationRequested;
        foreach (var notice in snapshot.Pending) _pending.Enqueue(notice);
    }
}
