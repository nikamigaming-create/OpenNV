using System.Text;
using OpenNV.Runtime.Content;

internal static class HackingLayoutContracts
{
    internal static void Run()
    {
        var generated = Candidates();
        var original = generated.Words.Select(word => word.Bytes.ToArray()).ToArray();
        var positions = new Queue<uint>([10, 202, 400]);
        var calls = new List<uint>();
        uint Normal(uint bound)
        {
            calls.Add(bound);
            return bound == 30 ? (uint)((calls.Count - 1) % 30) : positions.Dequeue();
        }
        var layout = FalloutHackingLayout.Create(generated, Normal);
        var display = layout.DisplayBytes();
        var punctuation = Encoding.ASCII.GetBytes("!@#$%^*()_+=-`[]{}|;':,./<>?\\\"");
        Require(punctuation.Length == 30 && calls.SequenceEqual(Enumerable.Repeat(30u, 408).Concat([403u, 403u, 403u])) &&
            layout.Positions.SequenceEqual([10, 202, 400]), "Source placement draw order or range changed.");
        for (var at = 0; at < display.Length; ++at)
        {
            var word = Enumerable.Range(0, 3).FirstOrDefault(index =>
                at >= layout.Positions[index] && at < layout.Positions[index] + 4, -1);
            var expected = word >= 0 ? generated.Words[word].Bytes[at - layout.Positions[word]] : punctuation[at % 30];
            Require(display[at] == expected, "Ordered punctuation fill or exact word copy changed.");
        }
        display[0] = 0;
        Require(layout.DisplayBytes()[0] == punctuation[0], "Layout exposed mutable backing bytes.");
        var transaction = layout.CreateTransaction(Alphabetic);
        Require(transaction.SelectDisplay(1, 0, 7).Selection == FalloutHackingSelection.WrongWord &&
            transaction.SelectDisplay(0, 1, 8).Selection == FalloutHackingSelection.CorrectWord,
            "Produced layout did not bind the existing transaction and original answer.");

        var collisionPositions = new Queue<uint>([10, 12, 16, 22]);
        var collision = FalloutHackingLayout.Create(generated, bound => bound == 30 ? 0 : collisionPositions.Dequeue());
        Require(collisionPositions.Count == 0 && collision.Positions.SequenceEqual([10, 16, 22]),
            "Placement lost its two-character collision margin or did not retry the current word.");
        var single = Candidates(words: "AABB ", count: 1);
        var last = FalloutHackingLayout.Create(single, bound => bound - 1);
        Require(last.Positions.Single() == 402 && last.DisplayBytes().AsSpan(402, 4).SequenceEqual("AABB"u8) &&
            last.DisplayBytes()[407] == (byte)'"', "Last legal start read outside the source grid or changed its bound.");

        var grids = 0;
        var junk = 0;
        var choices = 0;
        var totalChoices = 0;
        uint Exhaust(uint bound)
        {
            if (bound == 30)
            {
                if (junk++ % 408 == 0) { ++grids; choices = 0; }
                return 0;
            }
            Require(bound == 403, "Exhaustion changed the position range.");
            ++totalChoices;
            ++choices;
            return grids <= 5 && choices >= 2 ? 100u : 10u;
        }
        var retained = FalloutHackingLayout.Create(generated, Exhaust);
        Require(grids == 7 && junk == 7 * 408 && totalChoices == 5 * 102 + 101 + 1 &&
            retained.Candidates.Words.Count == 1 && retained.Positions.SequenceEqual([10]) &&
            ReferenceEquals(retained.Candidates.Answer, generated.Answer) &&
            ReferenceEquals(retained.Candidates.Words.Single(), generated.Answer) &&
            retained.Candidates.MaximumAttempts == generated.MaximumAttempts &&
            retained.Candidates.RequestedCount == generated.RequestedCount &&
            retained.Candidates.DictionaryHash == generated.DictionaryHash &&
            retained.Candidates.SelectionAttempts == generated.SelectionAttempts &&
            retained.Candidates.RetryWindows == generated.RetryWindows,
            "Exhaustion reset its retry counter, lost the prefix answer, or recalculated generation metadata.");
        Require(retained.CreateTransaction(Alphabetic).MaximumAttempts == generated.MaximumAttempts,
            "Retired candidates recalculated the transaction allowance.");

        var answerLast = Candidates(answer: 2);
        var retiredDraws = 0;
        var answerGrids = 0;
        var answerChoices = 0;
        var answerJunk = 0;
        uint RetireAnswer(uint bound)
        {
            ++retiredDraws;
            if (bound == 30)
            {
                if (answerJunk++ % 408 == 0) { ++answerGrids; answerChoices = 0; }
                return 0;
            }
            ++answerChoices;
            return 10;
        }
        Reject(() => FalloutHackingLayout.Create(answerLast, RetireAnswer), "retired the answer");
        Require(answerGrids == 5 && answerChoices == 101 && retiredDraws == 5 * (408 + 101) &&
            answerLast.Words.Count == 3 && ReferenceEquals(answerLast.Answer, answerLast.Words[2]),
            "Answer retirement invented a replacement, rewound draws, or exposed a sixth grid.");

        var lower = FalloutHackingLayout.Create(Candidates(words: "aabb aabc bbbc "), _ => 0);
        Require(lower.Positions.SequenceEqual([0, 0, 0]) && lower.DisplayBytes().AsSpan(0, 4).SequenceEqual("bbbc"u8),
            "Producer collision scanning invented case folding or a general alphabetic predicate.");
        Reject(() => lower.CreateTransaction(Alphabetic), "does not match");
        var cancelledDraws = 0;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Reject(() => FalloutHackingLayout.Create(generated, _ => { ++cancelledDraws; return 0; }, cancelled.Token), null);
        Require(cancelledDraws == 0, "Cancelled layout consumed a random draw.");
        using var duringFill = new CancellationTokenSource();
        var fillDraws = 0;
        Reject(() => FalloutHackingLayout.Create(generated, _ =>
        {
            if (++fillDraws == 42) duringFill.Cancel();
            return 0;
        }, duringFill.Token), null);
        Require(fillDraws == 42, "Cancellation replayed or concealed a consumed fill prefix.");
        Reject(() => FalloutHackingLayout.Create(generated, bound => bound), "out-of-range");
        for (var index = 0; index < original.Length; ++index)
            Require(generated.Words[index].Bytes.SequenceEqual(original[index]), "Layout mutated original candidate bytes.");
        Require(generated.Words.Count == 3 && ReferenceEquals(generated.Answer, generated.Words[0]),
            "Layout retirement mutated original candidate membership or answer identity.");
        Console.WriteLine("HACKING_LAYOUT_PASS sourceFill=true positionBounds=true collisionMargin=true fourRetries=true " +
            "prefixRetirement=true noCounterReset=true originalAllowance=true answerRetirementRefused=true nativeAdmission=false");
    }

    internal static void CheckOwned(FalloutHackingCandidates generated)
    {
        var before = generated.Words.Select(word => word.Bytes.ToArray()).ToArray();
        var randomBefore = FalloutSharedUnsignedRandom.Process.Observation.Draws;
        var layout = FalloutHackingLayout.CreateFromProcess(generated);
        var randomAfter = FalloutSharedUnsignedRandom.Process.Observation.Draws;
        var display = layout.DisplayBytes();
        for (var index = 0; index < layout.Positions.Count; ++index)
            Require(display.AsSpan(layout.Positions[index], layout.Candidates.Words[index].Length)
                .SequenceEqual(layout.Candidates.Words[index].Bytes), "Owned source layout lost a retained word.");
        var answer = layout.Candidates.Words.ToList().FindIndex(word => ReferenceEquals(word, generated.Answer));
        var selected = answer < 0 ? -1 : layout.Positions[answer] + generated.Answer.Length - 1;
        Require(answer >= 0 && layout.CreateTransaction(Alphabetic)
            .SelectDisplay(selected / 204, selected % 204 / 12, selected % 12 + 7).Selection ==
            FalloutHackingSelection.CorrectWord && randomAfter > randomBefore &&
            generated.Words.Select((word, index) => word.Bytes.SequenceEqual(before[index])).All(value => value),
            "Owned layout lost its answer, process stream or immutable inputs.");
        Console.WriteLine($"OPENNV_OWNED_HACKING_LAYOUT_PASS original={generated.Words.Count} retained={layout.Candidates.Words.Count} " +
            $"sourceDraws={randomAfter - randomBefore} allowance={layout.Candidates.MaximumAttempts} " +
            "placement=qualified-process-producer nativeInputAndOutcomes=unverified");
    }

    private static FalloutHackingCandidates Candidates(int answer = 0, string words = "AABB AABC BBBC ", uint count = 3) =>
        FalloutHackingCandidates.Generate(FalloutHackingDictionary.ReadSpaceDelimited(Encoding.ASCII.GetBytes(words)),
            new(0x800, 0, 50, 0, count, count), value => value,
            new(bound => bound == 2 ? 1u : bound == count ? (uint)answer : 0u, () => 0, 100, CancellationToken.None));

    private static bool Alphabetic(byte value) => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject(Action action, string? message)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException or
            NotSupportedException or OperationCanceledException)
        {
            if (message is not null) Require(error.Message.Contains(message, StringComparison.Ordinal), "Unexpected refusal: " + error.Message);
            return;
        }
        throw new InvalidOperationException("Unqualified hacking layout was admitted.");
    }
}
