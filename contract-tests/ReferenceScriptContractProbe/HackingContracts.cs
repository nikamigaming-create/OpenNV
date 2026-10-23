using System.Text;
using OpenNV.Runtime.Content;

internal static class HackingContracts
{
    internal static void Run()
    {
        HackingDisplayContracts.Run();
        HackingLayoutContracts.Run();
        HackingAllowanceContracts.Run();
        SharedUnsignedRandomContracts.Run();
        var bytes = Encoding.ASCII.GetBytes("AABB AABC BBBC ");
        var dictionary = FalloutHackingDictionary.ReadSpaceDelimited(bytes);
        var original = bytes.ToArray();
        var bonusCalls = 0;
        var request = new FalloutHackingRequest(0x800, 0, 50, 0, 3, 3);
        Require(request.WordLength == 4 && request.RequestedWords(value => { ++bonusCalls; return value; }) == 3 &&
            bonusCalls == 1, "Hacking generation bonus was skipped or repeated.");
        for (uint difficulty = 0; difficulty < 5; ++difficulty)
        {
            var even = request with { EffectiveDifficulty = difficulty };
            var odd = even with { TerminalFormId = 0x801 };
            Require(even.WordLength == Math.Min(12, 4 + 2 * (int)difficulty) &&
                odd.WordLength == Math.Min(12, 5 + 2 * (int)difficulty), "Source identity word length changed.");
        }
        foreach (var row in new (float Threshold, float Science, int Expected)[]
        { (0, 0, 16), (0, 50, 11), (25, 25, 16), (25, 100, 6), (100, 100, 11) })
            Require((request with
            {
                MaximumWords = 16,
                MinimumWords = 6,
                MinimumScience = row.Threshold,
                CurrentScience = row.Science
            }).RequestedWords(value => value) == row.Expected,
                "Hacking count interpolation or threshold100 fallback changed.");
        Require(FalloutHackingRules.Likeness("AABA"u8, "ABAA"u8) == 2 &&
            FalloutHackingRules.Likeness("abc"u8, "ABC"u8) == 0 &&
            FalloutHackingRules.Likeness("ABC"u8, "ABCDE"u8) == 3,
            "Hacking likeness folded case or compared unordered characters.");
        var calls = new List<uint>();
        uint Random(uint bound) { calls.Add(bound); return bound == 2 ? 1u : 0u; }
        var generated = FalloutHackingCandidates.Generate(dictionary, request, value => value,
            new(Random, () => 0, 100, CancellationToken.None));
        Require(calls.SequenceEqual(Enumerable.Repeat(2u, 16).Append(3u)) &&
            generated.MaximumAttempts == 4 && generated.Answer.Bytes.SequenceEqual("AABB"u8) &&
            generated.SelectionAttempts == 1 && generated.RetryWindows == 1,
            "Source ordering, answer draw or retained allowance changed.");
        Require(bytes.SequenceEqual(original), "Dictionary source bytes were mutated.");
        var indistinguishable = FalloutHackingDictionary.ReadSpaceDelimited("AAAA BBBB CCCC DDDD EEEE FFFF GGGG"u8);
        var tick = 0u;
        var aboveFloor = FalloutHackingCandidates.Generate(indistinguishable,
            request with { MaximumWords = 7, MinimumWords = 7 }, value => value,
            new(bound => bound == 2 ? 1u : 0u, () => tick++, 10, CancellationToken.None));
        // The final likeness-class restriction cannot retain the seventh
        // zero-likeness candidate before the source retry relaxation ends.
        Require(aboveFloor.RequestedCount == 7 && aboveFloor.Words.Count == 6 && aboveFloor.MaximumAttempts == 6 &&
            aboveFloor.SelectionAttempts == 180 && aboveFloor.RetryWindows == 20,
            "Actual generation did not use its candidate-derived allowance above four.");

        static bool Alphabetic(byte value) => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';
        var display = Encoding.ASCII.GetBytes("AABB AABC BBBC () [] {} <A> <!!>");
        int Position(string value) => Encoding.ASCII.GetString(display).IndexOf(value, StringComparison.Ordinal);
        var queued = new Queue<(uint Bound, uint Value)>();
        uint Queued(uint bound)
        {
            var next = queued.Dequeue();
            Require(next.Bound == bound, "Bracket RNG draw order changed.");
            return next.Value;
        }
        var owner = new FalloutHackingTransaction(generated, [0, 5, 10], display, Queued, Alphabetic);
        Require(owner.Guess(1).Selection == FalloutHackingSelection.WrongWord && owner.RemainingAttempts == 3,
            "Wrong word did not consume one attempt.");
        queued.Enqueue((4, 0));
        var reset = owner.Bonus(Position("()"), display.Length);
        Require(reset.Selection == FalloutHackingSelection.AttemptsReset && owner.Replenished &&
            owner.RemainingAttempts == 4 && owner.UsedBonuses.Contains(Position("()")), "Reset lost retained maximum or used span.");
        Require(owner.Bonus(Position("()"), display.Length).Selection == FalloutHackingSelection.Denied && queued.Count == 0,
            "Consumed bracket awarded again or took a random draw.");
        queued.Enqueue((3, 0)); queued.Enqueue((3, 1));
        var dud = owner.Bonus(Position("[]"), display.Length);
        Require(dud.Selection == FalloutHackingSelection.DudRemoved && dud.RemovedPosition == 5 &&
            owner.Words.Count == 2 && owner.Words[1].Position == 10 &&
            owner.DisplayBytes().AsSpan(5, 4).SequenceEqual("...."u8) && queued.Count == 0,
            "Dud removal erased the answer, lost display mapping or drew a second reset lottery.");
        queued.Enqueue((2, 1));
        Require(owner.Bonus(Position("{}"), display.Length).Selection == FalloutHackingSelection.DudRemoved &&
            owner.Words.Count == 1 && owner.Bonus(Position("<!!>"), display.Length).Selection == FalloutHackingSelection.Denied,
            "Last-answer bracket admission did not deny without mutation.");
        Require(owner.Guess(0).Selection == FalloutHackingSelection.CorrectWord && owner.Stage == FalloutHackingStage.Succeeded,
            "Correct answer did not publish its success stage.");
        Reject(() => owner.Guess(0));

        var failing = new FalloutHackingTransaction(generated, [0, 5, 10], display,
            _ => throw new InvalidOperationException("Original RNG refusal."), Alphabetic);
        Require(failing.Bonus(Position("<A>"), display.Length).Selection == FalloutHackingSelection.Denied,
            "Bracket scanner crossed an alphabetic byte.");
        Require(failing.Bonus(Position("()"), Position("()") + 1).Selection == FalloutHackingSelection.Denied,
            "Bracket scanner escaped its supplied segment.");
        Reject(() => failing.Bonus(Position("()"), display.Length));
        Require(failing.Stage == FalloutHackingStage.Faulted && failing.Error == "Original RNG refusal.",
            "Owner failure was swallowed or transaction remained retryable.");
        Reject(() => failing.Bonus(Position("[]"), display.Length));
        var locked = new FalloutHackingTransaction(generated, [0, 5, 10], display, Random, Alphabetic);
        for (var attempt = 0; attempt < generated.MaximumAttempts; ++attempt) _ = locked.Guess(1);
        Require(locked.Stage == FalloutHackingStage.LockedOut && locked.RemainingAttempts == 0,
            "Repeated wrong-word attempts did not retain their failure boundary.");
        Reject(() => locked.Guess(0));
        var cancelled = new FalloutHackingTransaction(generated, [0, 5, 10], display, Random, Alphabetic);
        cancelled.Cancel(); Reject(() => cancelled.Guess(0));
        Require(cancelled.Stage == FalloutHackingStage.Cancelled, "Cancellation admitted later input.");
        Reject(() => FalloutHackingDictionary.ReadSpaceDelimited("ABC\0D"u8));
        Reject(() => FalloutHackingDictionary.ReadSpaceDelimited("ABC\r\nDEF"u8));
        var chunked = FalloutHackingDictionary.ReadSpaceDelimited(Encoding.ASCII.GetBytes(
            "  " + new string('A', 32) + "  AbCd "));
        Require(chunked.Words.Count == 3 && chunked.Words[0].Length == 31 &&
            chunked.Words[1].Bytes.SequenceEqual("A"u8) && chunked.Words[2].Bytes.SequenceEqual("AbCd"u8),
            "Dictionary delimiter consumption, buffer chunks or byte case changed.");
        Reject(() => (request with { EffectiveDifficulty = 5 }).RequestedWords(value => value));
        Reject(() => request.RequestedWords(_ => float.NaN));
        Reject(() => FalloutHackingCandidates.Generate(dictionary, request, value => value,
            new(_ => 2, () => 0, 100, CancellationToken.None)));
        Reject(() => new FalloutHackingTransaction(generated, [0, 5, 11], display, Random, Alphabetic));
        Console.WriteLine("OPENNV_HACKING_TRANSACTION_CONTRACT_PASS sourceWords=true orderingDraws=true " +
            "retainedMaximum=true dudState=true resetOnce=true usedSpans=true stages=true ownerFaultStops=true " +
            "worldUnlockAndXp=callerRequired ordinaryTerminalInteraction=unverified");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or
            NotSupportedException or ArgumentOutOfRangeException)
        { return; }
        throw new InvalidDataException("Invalid or stopped hacking input was admitted.");
    }
}
