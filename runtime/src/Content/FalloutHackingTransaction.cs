namespace OpenNV.Runtime.Content;

internal enum FalloutHackingStage { Choosing, Succeeded, LockedOut, Cancelled, Faulted }
internal enum FalloutHackingSelection { WrongWord, CorrectWord, DudRemoved, AttemptsReset, Denied }
internal sealed record FalloutHackingOutcome(FalloutHackingSelection Selection, int Likeness,
    uint RemainingAttempts, int? RemovedPosition = null);
internal sealed record FalloutHackingPlacement(FalloutHackingWord Word, int Position);
internal sealed record FalloutHackingDisplayTarget(int Position, int SegmentEnd, int? WordIndex);

// Pure player transaction. Success/lockout is published to its real caller;
// world access, XP, crime, saves and terminal result scripts are NOT committed here.
internal sealed class FalloutHackingTransaction
{
    private readonly List<FalloutHackingPlacement> _words;
    private readonly FalloutHackingWord _answer;
    private readonly byte[] _display;
    private readonly HashSet<int> _usedBonuses = [];
    private readonly Func<uint, uint> _random;
    private readonly Func<byte, bool> _alphabetic;
    private bool _selecting;
    internal FalloutHackingStage Stage { get; private set; } = FalloutHackingStage.Choosing;
    internal uint MaximumAttempts { get; }
    internal uint RemainingAttempts { get; private set; }
    internal bool Replenished { get; private set; }
    internal string? Error { get; private set; }
    internal IReadOnlyList<FalloutHackingPlacement> Words => _words.AsReadOnly();
    internal IReadOnlySet<int> UsedBonuses => new HashSet<int>(_usedBonuses);
    internal byte[] DisplayBytes() => _display.ToArray();

    // The source file has two columns of seventeen twelve-byte payload rows.
    // rowCharacter includes the seven-character address prefix/separator. Word
    // spans are global; bracket segments end at the selected payload row.
    internal FalloutHackingDisplayTarget DisplayTarget(int column, int row, int rowCharacter)
    {
        if (_display.Length != 408)
            throw new InvalidDataException("Hacking row input requires the complete source display extent.");
        if ((uint)column >= 2 || (uint)row >= 17 || rowCharacter < 7 || rowCharacter >= 19)
            throw new ArgumentOutOfRangeException(nameof(rowCharacter), "Hacking input is outside a payload character.");
        var rowStart = (column * 17 + row) * 12;
        var position = rowStart + rowCharacter - 7;
        for (var index = 0; index < _words.Count; ++index)
            if (position >= _words[index].Position && position < _words[index].Position + _words[index].Word.Length)
                return new(position, rowStart + 12, index);
        return new(position, rowStart + 12, null);
    }

    internal FalloutHackingOutcome SelectDisplay(int column, int row, int rowCharacter)
    {
        RequireChoosing();
        // Resolve on each input, never retain a candidate index across dud removal.
        var target = DisplayTarget(column, row, rowCharacter);
        return target.WordIndex is { } index ? Guess(index) : Bonus(target.Position, target.SegmentEnd);
    }

    // Display positions/segments come from the source presentation adapter. They
    // are not candidate indices, and must be supplied before ordinary input starts.
    internal FalloutHackingTransaction(FalloutHackingCandidates generated,
        IReadOnlyList<int> positions, ReadOnlySpan<byte> display,
        Func<uint, uint> random, Func<byte, bool> alphabetic)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(alphabetic);
        if (positions.Count != generated.Words.Count)
            throw new InvalidDataException("Hacking display map does not bind every retained candidate.");
        _words = [];
        var occupied = new HashSet<int>();
        for (var index = 0; index < positions.Count; ++index)
        {
            var word = generated.Words[index]; var position = positions[index];
            if (position < 0 || position > display.Length - word.Length ||
                !display.Slice(position, word.Length).SequenceEqual(word.Bytes))
                throw new InvalidDataException("Hacking candidate does not match its source display bytes.");
            for (var offset = position; offset < position + word.Length; ++offset)
                if (!occupied.Add(offset)) throw new InvalidDataException("Hacking display candidates overlap.");
            _words.Add(new(word, position));
        }
        _answer = generated.Answer; _display = display.ToArray();
        _random = random; _alphabetic = alphabetic;
        MaximumAttempts = generated.MaximumAttempts; RemainingAttempts = MaximumAttempts;
    }

    internal FalloutHackingOutcome Guess(int wordIndex)
    {
        RequireChoosing();
        if ((uint)wordIndex >= (uint)_words.Count) throw new ArgumentOutOfRangeException(nameof(wordIndex));
        var likeness = FalloutHackingRules.Likeness(_words[wordIndex].Word.Bytes, _answer.Bytes);
        if (likeness == _answer.Length)
        {
            Stage = FalloutHackingStage.Succeeded;
            return new(FalloutHackingSelection.CorrectWord, likeness, RemainingAttempts);
        }
        --RemainingAttempts;
        if (RemainingAttempts == 0) Stage = FalloutHackingStage.LockedOut;
        return new(FalloutHackingSelection.WrongWord, likeness, RemainingAttempts);
    }

    // segmentEnd is the exact exclusive text-segment boundary supplied by the
    // renderer. Never scan into another column/row merely because it is nearby.
    internal FalloutHackingOutcome Bonus(int position, int segmentEnd)
    {
        RequireChoosing();
        if (position < 0 || segmentEnd > _display.Length || segmentEnd <= position)
            throw new ArgumentOutOfRangeException(nameof(position));
        _selecting = true;
        try
        {
            if (_usedBonuses.Contains(position) || _words.Count == 1 || !IsBonus(position, segmentEnd))
                return new(FalloutHackingSelection.Denied, 0, RemainingAttempts);
            if (!Replenished && FalloutHackingRules.Draw(_random, 4) == 0)
            {
                RemainingAttempts = MaximumAttempts; Replenished = true;
                _usedBonuses.Add(position);
                return new(FalloutHackingSelection.AttemptsReset, 0, RemainingAttempts);
            }
            int selected;
            do { selected = (int)FalloutHackingRules.Draw(_random, (uint)_words.Count); }
            while (_words[selected].Word.Bytes.SequenceEqual(_answer.Bytes));
            var removed = _words[selected];
            _display.AsSpan(removed.Position, removed.Word.Length).Fill((byte)'.');
            _words.RemoveAt(selected);
            _usedBonuses.Add(position);
            return new(FalloutHackingSelection.DudRemoved, 0, RemainingAttempts, removed.Position);
        }
        catch (Exception error)
        {
            // RNG/classifier faults may follow consumed draws. Keep a stopped
            // transaction; a retry must not repeat that prefix or award a bonus.
            Stage = FalloutHackingStage.Faulted; Error = error.Message;
            throw;
        }
        finally { _selecting = false; }
    }

    internal void Cancel()
    {
        RequireChoosing();
        Stage = FalloutHackingStage.Cancelled;
    }

    private bool IsBonus(int position, int end)
    {
        const string opening = "([{<";
        const string closing = ")]}>";
        var type = opening.IndexOf((char)_display[position]);
        if (type < 0) return false;
        for (var index = position + 1; index < end; ++index)
        {
            var value = _display[index];
            if (value == 0 || _alphabetic(value)) return false;
            if (value == (byte)closing[type]) return true;
        }
        return false;
    }

    private void RequireChoosing()
    {
        if (_selecting || Stage != FalloutHackingStage.Choosing)
            throw new InvalidOperationException("Hacking input requires the current choosing transaction.");
    }
}
