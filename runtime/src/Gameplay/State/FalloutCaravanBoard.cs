using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Draft board state for the manual-derived rules. Source card interpretation,
// turn ownership, removals, menu integration and retail parity remain unbound.
internal sealed class FalloutCaravanBoard
{
    internal readonly record struct CardIdentity(FalloutFormKey Form, long Instance);
    internal readonly record struct Modifier(CardIdentity Identity, ModifierKind Kind, FalloutCaravanRules.Suit? Suit);
    internal enum ModifierKind { King, Queen, Joker }
    internal sealed record Entry(CardIdentity Identity, FalloutCaravanRules.NumberCard Number, IReadOnlyList<Modifier> Modifiers);
    internal sealed record Lane(IReadOnlyList<Entry> Entries, FalloutCaravanRules.Direction Direction, FalloutCaravanRules.Suit? Suit);

    private readonly Lane[,] _lanes = new Lane[2, 3];
    private readonly HashSet<CardIdentity> _used = [];

    internal FalloutCaravanBoard()
    {
        for (var player = 0; player < 2; ++player)
            for (var caravan = 0; caravan < 3; ++caravan)
                _lanes[player, caravan] = new(Array.Empty<Entry>(), FalloutCaravanRules.Direction.Unset, null);
    }

    internal Lane GetLane(int player, int caravan)
    {
        new FalloutCaravanRules.Position(player, caravan, 0).Validate();
        return _lanes[player, caravan];
    }

    internal IReadOnlyList<Lane> Capture() => Enumerable.Range(0, 6)
        .Select(index => _lanes[index / 3, index % 3]).ToArray();

    // Restore final retained structure directly: cards removed during play can
    // leave adjacency that could not be reconstructed by replaying placements.
    internal void Restore(IReadOnlyList<Lane> lanes)
    {
        if (_used.Count != 0 || _lanes.Cast<Lane>().Any(lane => lane.Entries.Count != 0))
            throw new InvalidOperationException("Caravan board restoration requires a fresh board.");
        if (lanes is null || lanes.Count != 6) throw new InvalidDataException("Caravan board requires six retained lanes.");
        var next = new Lane[6]; var used = new HashSet<CardIdentity>();
        for (var index = 0; index < lanes.Count; ++index)
        {
            var lane = lanes[index] ?? throw new InvalidDataException("Saved Caravan lane is absent.");
            if (lane.Entries is null) throw new InvalidDataException("Saved Caravan lane entries are absent.");
            var entries = new List<Entry>();
            foreach (var entry in lane.Entries)
            {
                if (entry is null || entry.Modifiers is null || entry.Identity.Instance < 0 || !used.Add(entry.Identity))
                    throw new InvalidDataException("Saved Caravan board has an invalid or repeated card instance.");
                entry.Number.Validate();
                foreach (var modifier in entry.Modifiers)
                    if (modifier.Identity.Instance < 0 || !used.Add(modifier.Identity) || !Enum.IsDefined(modifier.Kind) ||
                        modifier.Suit is { } suit && !Enum.IsDefined(suit) || modifier.Kind != ModifierKind.Joker && modifier.Suit is null ||
                        modifier.Kind == ModifierKind.Joker && modifier.Suit is not null)
                        throw new InvalidDataException("Saved Caravan modifier is invalid or repeated.");
                entries.Add(entry with { Modifiers = Array.AsReadOnly(entry.Modifiers.ToArray()) });
            }
            _ = FalloutCaravanRules.Total(entries.Select(entry => new FalloutCaravanRules.NumberWithKings(entry.Number,
                entry.Modifiers.Count(modifier => modifier.Kind == ModifierKind.King))));
            var derived = Recalculate(entries.ToArray());
            if (lane.Direction != derived.Direction || lane.Suit != derived.Suit)
                throw new InvalidDataException("Saved Caravan lane has inconsistent derived state.");
            next[index] = derived;
        }
        for (var index = 0; index < next.Length; ++index) _lanes[index / 3, index % 3] = next[index];
        _used.UnionWith(used);
    }

    internal void PlayNumber(int player, int caravan, CardIdentity identity, FalloutCaravanRules.NumberCard card)
    {
        var lane = GetLane(player, caravan);
        card.Validate(); RequireUnused(identity);
        if (lane.Entries.Count != 0)
        {
            var previous = lane.Entries[^1].Number;
            if (!FalloutCaravanRules.CanAppend(previous, card, lane.Direction,
                    lane.Suit ?? throw new InvalidDataException("A nonempty caravan has no suit.")))
                throw new InvalidOperationException("Number card is not legal on this caravan.");
        }
        var entries = lane.Entries.Append(new Entry(identity, card, Array.Empty<Modifier>())).ToArray();
        _lanes[player, caravan] = Recalculate(entries);
        _used.Add(identity);
    }

    internal void PlayModifier(int player, int caravan, int target, Modifier modifier)
    {
        var lane = GetLane(player, caravan);
        if (target < 0 || target >= lane.Entries.Count) throw new InvalidDataException("Caravan modifier target is absent.");
        if (!Enum.IsDefined(modifier.Kind) || modifier.Suit is { } modifierSuit && !Enum.IsDefined(modifierSuit) ||
            modifier.Kind != ModifierKind.Joker && modifier.Suit is null)
            throw new InvalidDataException("Caravan modifier is unknown.");
        RequireUnused(modifier.Identity);
        var entries = lane.Entries.ToArray();
        entries[target] = entries[target] with { Modifiers = Array.AsReadOnly(entries[target].Modifiers.Append(modifier).ToArray()) };
        // Reject arithmetic overflow before publishing any part of the change.
        _ = FalloutCaravanRules.Total(entries.Select(entry => new FalloutCaravanRules.NumberWithKings(entry.Number,
            entry.Modifiers.Count(value => value.Kind == ModifierKind.King))));
        _lanes[player, caravan] = Recalculate(entries);
        _used.Add(modifier.Identity);
    }

    internal int Total(int player, int caravan) => FalloutCaravanRules.Total(GetLane(player, caravan).Entries.Select(entry =>
        new FalloutCaravanRules.NumberWithKings(entry.Number, entry.Modifiers.Count(value => value.Kind == ModifierKind.King))));

    internal FalloutCaravanRules.Sale Winner() => FalloutCaravanRules.ResolveWinner(
        Enumerable.Range(0, 3).Select(index => (Total(0, index), Total(1, index))).ToArray());

    internal IReadOnlyList<CardIdentity> ApplyJack(FalloutCaravanRules.Position target) => RemoveNumbers([target], null, null);

    internal IReadOnlyList<CardIdentity> ApplyJoker(CardIdentity joker, FalloutCaravanRules.Position target)
    {
        RequireUnused(joker);
        var board = new List<FalloutCaravanRules.BoardNumber>();
        for (var player = 0; player < 2; ++player)
            for (var caravan = 0; caravan < 3; ++caravan)
                for (var index = 0; index < _lanes[player, caravan].Entries.Count; ++index)
                    board.Add(new(new(player, caravan, index), _lanes[player, caravan].Entries[index].Number));
        return RemoveNumbers(FalloutCaravanRules.JokerRemovals(board, target), target, joker);
    }

    private IReadOnlyList<CardIdentity> RemoveNumbers(IReadOnlyList<FalloutCaravanRules.Position> positions,
        FalloutCaravanRules.Position? jokerTarget, CardIdentity? joker)
    {
        var selected = new HashSet<FalloutCaravanRules.Position>();
        foreach (var position in positions)
        {
            var lane = GetLane(position.Player, position.Caravan);
            if (position.Index < 0 || position.Index >= lane.Entries.Count || !selected.Add(position))
                throw new InvalidDataException("Caravan removal target is absent or duplicated.");
        }
        if (jokerTarget is { } target && (target.Index < 0 || target.Index >= GetLane(target.Player, target.Caravan).Entries.Count ||
            selected.Contains(target) || joker is null))
            throw new InvalidDataException("Caravan joker target cannot be removed.");
        var next = new Lane[2, 3]; var removed = new List<CardIdentity>();
        for (var player = 0; player < 2; ++player)
            for (var caravan = 0; caravan < 3; ++caravan)
            {
                var entries = new List<Entry>(); var lane = _lanes[player, caravan];
                for (var index = 0; index < lane.Entries.Count; ++index)
                {
                    var position = new FalloutCaravanRules.Position(player, caravan, index); var entry = lane.Entries[index];
                    if (selected.Contains(position))
                    {
                        removed.Add(entry.Identity); removed.AddRange(entry.Modifiers.Select(value => value.Identity));
                    }
                    else if (jokerTarget == position)
                        entries.Add(entry with { Modifiers = Array.AsReadOnly(entry.Modifiers.Append(new Modifier(joker!.Value, ModifierKind.Joker, null)).ToArray()) });
                    else entries.Add(entry);
                }
                next[player, caravan] = Recalculate(entries.ToArray());
            }
        if (removed.Any(identity => !_used.Contains(identity))) throw new InvalidDataException("Caravan board lost a card instance.");
        for (var player = 0; player < 2; ++player)
            for (var caravan = 0; caravan < 3; ++caravan) _lanes[player, caravan] = next[player, caravan];
        foreach (var identity in removed) _used.Remove(identity);
        if (joker is { } added) _used.Add(added);
        return Array.AsReadOnly(removed.ToArray());
    }

    // Return every physical instance so the match owner can move it to its
    // discard zone. This board does not own a hand, deck, turn or discard pile.
    internal IReadOnlyList<CardIdentity> Disband(int player, int caravan)
    {
        var lane = GetLane(player, caravan);
        if (lane.Entries.Count == 0) throw new NotSupportedException("Disbanding an empty caravan is unverified.");
        var removed = lane.Entries.SelectMany(entry => new[] { entry.Identity }.Concat(entry.Modifiers.Select(value => value.Identity))).ToArray();
        if (removed.Any(identity => !_used.Contains(identity))) throw new InvalidDataException("Caravan board lost a card instance.");
        _lanes[player, caravan] = new(Array.Empty<Entry>(), FalloutCaravanRules.Direction.Unset, null);
        foreach (var identity in removed) _used.Remove(identity);
        return Array.AsReadOnly(removed);
    }

    private void RequireUnused(CardIdentity identity)
    {
        if (identity.Instance < 0) throw new InvalidDataException("Caravan card instance must be nonnegative.");
        if (_used.Contains(identity)) throw new InvalidOperationException("Caravan card instance is already on the board.");
    }

    private static Lane Recalculate(Entry[] entries)
    {
        if (entries.Length == 0) return new(Array.Empty<Entry>(), FalloutCaravanRules.Direction.Unset, null);
        var last = entries[^1];
        var direction = entries.Length < 2 ? FalloutCaravanRules.Direction.Unset :
            last.Number.Value > entries[^2].Number.Value ? FalloutCaravanRules.Direction.Ascending : FalloutCaravanRules.Direction.Descending;
        var suit = last.Number.Suit;
        foreach (var modifier in last.Modifiers.Where(value => value.Kind == ModifierKind.Queen))
        {
            suit = modifier.Suit ?? throw new InvalidDataException("A queen has no source suit.");
            direction = direction switch
            {
                FalloutCaravanRules.Direction.Ascending => FalloutCaravanRules.Direction.Descending,
                FalloutCaravanRules.Direction.Descending => FalloutCaravanRules.Direction.Ascending,
                _ => FalloutCaravanRules.Direction.Unset,
            };
        }
        return new(Array.AsReadOnly(entries), direction, suit);
    }
}
