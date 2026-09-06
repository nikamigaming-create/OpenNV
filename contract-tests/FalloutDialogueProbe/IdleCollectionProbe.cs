using OpenNV.Runtime.Content;

internal static class IdleCollectionProbe
{
    internal static void Run()
    {
        var first = new FalloutFormKey("Synthetic.esm", 0x100);
        var second = new FalloutFormKey("Synthetic.esm", 0x101);
        var source = new FalloutScriptPackage(new("Synthetic.esm", 0x200), "Sequence", 1, 0.75f, [first, second], new Dictionary<string, FalloutFormKey?>());
        var replay = new FalloutIdleReplayState();
        var queue = new FalloutIdleCollectionPlayback(source, replay);
        Require(queue.Select() == first, "The first source idle was skipped.");
        queue.Finish();
        Require(queue.WaitSeconds == 0 && queue.Select() == second, "A timer split an ordered idle sequence.");
        queue.Finish();
        Require(queue.Select() is null, "The sequence ignored its source timer.");
        Require(queue.AdvanceWait(0.25) == 0 && queue.WaitSeconds == 0.5, "An idle timer consumed the wrong interval.");
        Require(queue.AdvanceWait(0.75) == 0.25 && queue.Select() == first, "Idle wait dropped residual frame time.");
        var once = new FalloutIdleCollectionPlayback(source with { IdleFlags = 5 }, replay);
        Require(once.Select() == first, "A once-only sequence lost its first idle.");
        once.Finish();
        Require(once.Select() == second, "A once-only sequence finished before its last idle.");
        once.Finish();
        Require(once.Complete && once.Select() is null, "A once-only sequence replayed.");
        var single = new FalloutIdleCollectionPlayback(source with { IdleFlags = 0, Idles = [first] }, replay);
        Require(single.Select() == first, "A one-item random collection required an RNG.");
        single.Finish();
        single.AdvanceWait(1);
        Require(single.Select() == first, "A one-item random collection did not repeat.");
        var rejected = false;
        try { _ = new FalloutIdleCollectionPlayback(source with { IdleFlags = 0 }, replay).Select(); }
        catch (NotSupportedException) { rejected = true; }
        Require(rejected, "An unowned random collection silently chose an idle.");

        replay.Started(first, 12);
        replay.Advance(2);
        // A response may interrupt the pose, or an AI transition may replace
        // the collection. Neither permits restarting the actor's recent idle.
        var replacement = new FalloutIdleCollectionPlayback(source with { IdleTimer = 0 }, replay);
        Require(single.Select() is null && replacement.Select() is null && replacement.Cursor == 0,
            "Package playback restarted an interrupted idle during its source cooldown.");
        Require(replay.Remaining[first] == 10 && replay.CanSelect(second),
            "A refused package selection restarted the delay or blocked another idle.");
        replay.Advance(9.75f);
        Require(replacement.Select() is null, "A package restarted its idle before the delay expired.");
        replay.Advance(0.25f);
        Require(replacement.Select() == first, "A package failed to resume when its actor cooldown expired.");
        replacement.Finish();
        Require(replacement.Select() == second, "Cooldown admission changed source sequence order.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
