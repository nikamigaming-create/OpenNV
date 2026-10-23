using System.Text;
using OpenNV.Runtime.Content;

internal static class HackingDisplayContracts
{
    internal static void Run()
    {
        var dictionary = FalloutHackingDictionary.ReadSpaceDelimited("AABB AABC BBBC "u8);
        var generated = FalloutHackingCandidates.Generate(dictionary,
            new(0x800, 0, 50, 0, 3, 3), value => value,
            new(bound => bound == 2 ? 1u : 0u, () => 0, 100, CancellationToken.None));
        var display = Enumerable.Repeat((byte)'.', 408).ToArray();
        int[] positions = [10, 202, 400];
        for (var index = 0; index < positions.Length; ++index)
            generated.Words[index].Bytes.CopyTo(display.AsSpan(positions[index]));
        "()"u8.CopyTo(display.AsSpan(28));
        display[35] = (byte)'['; display[36] = (byte)']';
        "<>"u8.CopyTo(display.AsSpan(206));
        var source = display.ToArray();
        var draws = new Queue<(uint Bound, uint Value)>();
        var drawCount = 0;
        uint Random(uint bound)
        {
            ++drawCount;
            var next = draws.Dequeue();
            Require(next.Bound == bound, "Display selection changed the transaction draw order.");
            return next.Value;
        }
        var owner = new FalloutHackingTransaction(generated, positions, display, Random,
            value => value is >= (byte)'A' and <= (byte)'Z');
        static (int Column, int Row, int Character) Coordinates(int position) =>
            (position / 204, position % 204 / 12, position % 12 + 7);
        FalloutHackingOutcome Select(int position)
        {
            var at = Coordinates(position);
            return owner.SelectDisplay(at.Column, at.Row, at.Character);
        }
        for (var index = 0; index < positions.Length; ++index)
            for (var offset = 0; offset < 4; ++offset)
            {
                var position = positions[index] + offset;
                var at = Coordinates(position);
                var target = owner.DisplayTarget(at.Column, at.Row, at.Character);
                Require(target.Position == position && target.WordIndex == index &&
                    target.SegmentEnd == (position / 12 + 1) * 12,
                    "A cross-row/column word lost its global candidate binding.");
            }
        Require(owner.DisplayTarget(0, 0, 7).Position == 0 &&
            owner.DisplayTarget(1, 16, 18).Position == 407,
            "Source display boundary mapping changed.");
        foreach (var invalid in new[] { (-1, 0, 7), (2, 0, 7), (0, -1, 7), (0, 17, 7),
            (0, 0, 6), (0, 0, 19), (int.MaxValue, int.MaxValue, int.MaxValue) })
            Reject(() => owner.SelectDisplay(invalid.Item1, invalid.Item2, invalid.Item3));
        Require(owner.Stage == FalloutHackingStage.Choosing && owner.RemainingAttempts == 4 &&
            owner.DisplayBytes().SequenceEqual(source) && drawCount == 0 && owner.UsedBonuses.Count == 0,
            "Invalid display input mutated the transaction.");
        Require(Select(35).Selection == FalloutHackingSelection.Denied && drawCount == 0,
            "Bracket selection crossed into the next display row.");
        Require(Select(203).Selection == FalloutHackingSelection.WrongWord && owner.RemainingAttempts == 3 &&
            Select(204).Selection == FalloutHackingSelection.WrongWord && owner.RemainingAttempts == 2,
            "Both halves of a cross-column word did not select the same ordinary guess.");
        draws.Enqueue((4, 0));
        Require(Select(28).Selection == FalloutHackingSelection.AttemptsReset && owner.RemainingAttempts == 4 &&
            Select(28).Selection == FalloutHackingSelection.Denied && drawCount == 1,
            "Repeated bracket selection replayed a consumed reset.");
        draws.Enqueue((3, 1));
        Require(Select(206).Selection == FalloutHackingSelection.DudRemoved && owner.Words.Count == 2 &&
            owner.DisplayTarget(0, 16, 17).WordIndex is null &&
            owner.DisplayTarget(1, 0, 7).WordIndex is null && drawCount == 2,
            "Dud removal retained a stale word index across columns.");
        Require(Select(203).Selection == FalloutHackingSelection.Denied &&
            Select(206).Selection == FalloutHackingSelection.Denied && drawCount == 2,
            "Removed or consumed display selection mutated the owner again.");
        Require(owner.DisplayTarget(1, 16, 11).WordIndex == 1 &&
            Select(13).Selection == FalloutHackingSelection.CorrectWord &&
            owner.Stage == FalloutHackingStage.Succeeded && drawCount == 2,
            "Live display mapping did not follow candidate removal or the cross-row answer.");
        Reject(() => Select(13));
        Require(display.SequenceEqual(source) && generated.Words.Count == 3 && draws.Count == 0,
            "Display transaction mutated its source inputs or skipped queued draws.");
        var shortDisplay = new FalloutHackingTransaction(generated, [0, 5, 10],
            Encoding.ASCII.GetBytes("AABB AABC BBBC"), Random, _ => false);
        Reject(() => shortDisplay.SelectDisplay(0, 0, 7));
        Require(shortDisplay.Stage == FalloutHackingStage.Choosing && shortDisplay.RemainingAttempts == 4,
            "An incomplete display entered row input.");
        var locked = new FalloutHackingTransaction(generated, positions, display, Random, _ => false);
        for (var attempt = 0u; attempt < locked.MaximumAttempts; ++attempt)
            Require(locked.SelectDisplay(1, 0, 7).Selection == FalloutHackingSelection.WrongWord,
                "Repeated ordinary wrong guesses did not consume attempts.");
        Reject(() => locked.SelectDisplay(0, 0, 17));
        Require(locked.Stage == FalloutHackingStage.LockedOut && locked.RemainingAttempts == 0 && drawCount == 2,
            "Locked-out display input resumed the transaction.");
        Console.WriteLine("HACKING_DISPLAY_PASS crossRowWord=true crossColumnWord=true rowClippedBonus=true consumed=true nativeAdmission=false");
    }

    internal static void CheckOwned(FalloutHackingCandidates generated)
    {
        // Real owned candidates, controlled source-shaped placement. This does
        // not stand in for the still-unqualified placement exhaustion path.
        var display = Enumerable.Repeat((byte)'.', 408).ToArray();
        var positions = new List<int>();
        var cursor = 10;
        foreach (var word in generated.Words)
        {
            Require(cursor + word.Length <= display.Length, "Owned fixture exceeds the source display extent.");
            positions.Add(cursor);
            word.Bytes.CopyTo(display.AsSpan(cursor));
            cursor += word.Length + 2;
        }
        var original = display.ToArray();
        var transaction = new FalloutHackingTransaction(generated, positions, display,
            _ => throw new InvalidOperationException("Owned word mapping unexpectedly consumed a bonus draw."),
            value => value is >= (byte)'A' and <= (byte)'Z');
        var checkedCharacters = 0;
        for (var index = 0; index < positions.Count; ++index)
            for (var offset = 0; offset < generated.Words[index].Length; ++offset)
            {
                var position = positions[index] + offset;
                var mapped = transaction.DisplayTarget(position / 204, position % 204 / 12, position % 12 + 7);
                Require(mapped.Position == position && mapped.WordIndex == index &&
                    mapped.SegmentEnd == (position / 12 + 1) * 12, "Owned word character mapping changed.");
                ++checkedCharacters;
            }
        var answer = generated.Words.ToList().FindIndex(word => word.Bytes.SequenceEqual(generated.Answer.Bytes));
        Require(answer >= 0, "Owned display lost its retained answer.");
        var selected = positions[answer] + generated.Answer.Length - 1;
        Require(transaction.SelectDisplay(selected / 204, selected % 204 / 12, selected % 12 + 7).Selection ==
            FalloutHackingSelection.CorrectWord && transaction.Stage == FalloutHackingStage.Succeeded &&
            transaction.DisplayBytes().SequenceEqual(original) && display.SequenceEqual(original),
            "Owned answer mapping changed source/display state or did not reach the real transaction.");
        Console.WriteLine($"OPENNV_OWNED_HACKING_DISPLAY_PASS words={positions.Count} characters={checkedCharacters} " +
            "controlledPlacement=true placementGenerationAndNativeInput=unverified");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Invalid hacking display input was admitted.");
    }
}
