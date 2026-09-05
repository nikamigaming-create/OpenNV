using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutHudEventKind { ItemAdded, Message }

// The event retains source identity and command order. Text, icons and timing
// are resolved from the winning graph when presented, never baked into saves.
internal sealed record FalloutHudEvent(FalloutHudEventKind Kind, FalloutFormKey Source, int Count,
    FalloutFormKey? Quest = null, FalloutFormKey? Script = null);
internal sealed record FalloutHudNotice(long Ordinal, FalloutHudEvent Event);
internal sealed record FalloutHudNotificationsSnapshot(long LastOrdinal, FalloutHudNotice? Current,
    double Elapsed, IReadOnlyList<FalloutHudNotice> Pending);

internal sealed class FalloutHudNotifications
{
    private readonly Queue<FalloutHudNotice> _pending = [];
    private long _ordinal;
    internal FalloutHudNotice? Current { get; private set; }
    internal double Elapsed { get; private set; }
    internal FalloutHudNotificationsSnapshot Capture() => new(_ordinal, Current, Elapsed, _pending.ToArray());

    internal static void Validate(IReadOnlyList<FalloutHudEvent> events)
    {
        if (events.Any(value => value.Source.ObjectId == 0 || string.IsNullOrWhiteSpace(value.Source.OwnerPlugin) ||
            (value.Kind switch { FalloutHudEventKind.ItemAdded => value.Count <= 0, FalloutHudEventKind.Message => value.Count != 0, _ => true })))
            throw new InvalidDataException("HUD event has invalid source identity, kind or count.");
    }

    internal void Publish(IReadOnlyList<FalloutHudEvent> events)
    {
        Validate(events);
        _ = checked(_ordinal + events.Count);
        foreach (var value in events) _pending.Enqueue(new(++_ordinal, value));
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
        if (_ordinal != 0 || Current is not null || _pending.Count != 0)
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
        foreach (var notice in snapshot.Pending) _pending.Enqueue(notice);
    }
}
