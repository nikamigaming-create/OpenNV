using OpenNV.Runtime.Formats.Gamebryo;

internal static class TextKeyContracts
{
    internal static void Run()
    {
        FalloutNifTextKey[] source = [new(2, "start"), new(3, "Sound: first"),
            new(3, "Sound: second"), new(4, "end")];
        var looping = new FalloutNifTextKeyTimeline(source, 2, 4, 0, 2);
        var whole = looping.Crossed(0, 2.5, true).ToArray();
        var split = looping.Crossed(0, .5, true).Concat(looping.Crossed(.5, 1, false))
            .Concat(looping.Crossed(1, 1.25, false)).Concat(looping.Crossed(1.25, 2.5, false)).ToArray();
        Require(whole.SequenceEqual(split), "Splitting an advance duplicated or dropped source events.");
        Require(whole.Select(key => key.Text).SequenceEqual(new[] { "start", "Sound: first", "Sound: second", "end",
            "start", "Sound: first", "Sound: second", "end", "start", "Sound: first", "Sound: second" }),
            "Loop boundaries, duplicate-time ordering or frequency scaling changed.");
        Require(looping.Crossed(1, 1, false).Count() == 0, "An unchanged clock replayed events.");
        Require(looping.Crossed(1.1, 1.6, false).Select(key => key.Text)
            .SequenceEqual(new[] { "Sound: first", "Sound: second" }), "A seek interval replayed old events.");
        var clamped = new FalloutNifTextKeyTimeline(source, 2, 4, 2, 1);
        Require(clamped.Crossed(0, 99, true).Count() == 4 && !clamped.Crossed(99, 100, false).Any(),
            "A completed clamped sequence repeated its events.");
        var reversed = new FalloutNifTextKeyTimeline(source.Reverse().ToArray(), 2, 4, 2, 1);
        Require(reversed.Crossed(0, 2, true).Select(key => key.SourceOrdinal).SequenceEqual(new[] { 3, 1, 2, 0 }),
            "Chronological ordering lost original ordinals or same-time source order.");
        Reject(() => looping.Crossed(2, 1, false).ToArray());
        Reject(() => looping.Crossed(0, double.NaN, true).ToArray());
        Reject(() => new FalloutNifTextKeyTimeline(source, 2, 4, 1, 1));
        Console.WriteLine("OPENNV_NIF_TEXT_KEY_TIMELINE_PASS loopBoundary=true multiLoop=true ordered=true noDuplicates=true clamp=true seek=true");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid source event interval was accepted.");
    }
}
