using System.Text;
using OpenNV.Runtime.Content;

internal static class HackingAllowanceContracts
{
    internal static void Run()
    {
        Require(FalloutHackingAllowance.Calculate([]) == 0, "Empty allowance changed.");
        for (var count = 1; count <= 20; ++count)
        {
            var words = Enumerable.Range(0, count).Select(index =>
                new FalloutHackingWord(Encoding.ASCII.GetBytes(new string((char)('A' + index), 4)))).ToArray();
            var before = words.Select(word => word.Bytes.ToArray()).ToArray();
            // All wrong guesses have zero likeness, so each induced group loses
            // exactly its guessed candidate. The recurrence returns count.
            Require(FalloutHackingAllowance.Calculate(words) == count &&
                FalloutHackingAllowance.Calculate(words.Reverse().ToArray()) == count,
                "Indistinguishable outcomes were replaced with a fixed attempt allowance.");
            Require(words.Select((word, index) => word.Bytes.SequenceEqual(before[index])).All(value => value),
                "Allowance mutated a source word.");
        }
        Check(["AAAA", "AAAB", "AABA"], 2);
        // The central word has a group of four. Each other row has a group
        // of three; their induced three-word matrices return three, then +1.
        Check(["AAAA", "AAAB", "AABA", "ABAA", "BAAA"], 4);
        Check(["AABB", "AABC", "BBBC"], 2);
        // The largest group has likeness ten, outside the four outcome indices
        // visited by this source recursion. It therefore returns count + 1.
        Check(["AAAAAAAAAAA", "AAAAAAAAAAB", "AAAAAAAAAAC", "AAAAAAAAAAD"], 5);
        Reject([Word("AAAA"), Word("AAAA")]);
        Reject([Word("AAAA"), Word("AAA")]);
        Reject([Word("AAAA"), null!]);
        Reject(Enumerable.Range(0, 21).Select(index => Word(new string((char)('A' + index), 4))).ToArray());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { FalloutHackingAllowance.Calculate([Word("AAAA")], cancelled.Token); }
        catch (OperationCanceledException)
        {
            Console.WriteLine("OPENNV_HACKING_ALLOWANCE_CONTRACT_PASS recursiveGroups=true " +
                "aboveFour=true sourceImmutable=true cancellation=true ordinaryHacking=unverified");
            return;
        }
        throw new InvalidDataException("Cancelled allowance calculation continued.");
    }

    private static FalloutHackingWord Word(string value) => new(Encoding.ASCII.GetBytes(value));

    private static void Check(string[] source, uint expected)
    {
        var words = source.Select(Word).ToArray();
        Require(FalloutHackingAllowance.Calculate(words) == expected &&
            FalloutHackingAllowance.Calculate(words.Reverse().ToArray()) == expected,
            "Candidate outcome-group recurrence changed.");
    }

    private static void Reject(IReadOnlyList<FalloutHackingWord> words)
    {
        try { FalloutHackingAllowance.Calculate(words); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid allowance inputs were admitted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
