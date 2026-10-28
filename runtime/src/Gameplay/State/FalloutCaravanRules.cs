namespace OpenNV.Runtime.Gameplay.State;

// Rule primitives from the published Fallout: New Vegas manual. These operate
// on already interpreted cards: CCRD field meanings and native edge cases are
// separate evidence, so this type is not yet a gameplay or script owner.
internal static class FalloutCaravanRules
{
    internal enum Suit { Clubs, Diamonds, Hearts, Spades }
    internal enum Direction { Unset, Ascending, Descending }
    internal enum Sale { Unresolved, First, Second }
    internal readonly record struct Position(int Player, int Caravan, int Index)
    {
        internal void Validate()
        {
            if (Player is < 0 or > 1 || Caravan is < 0 or > 2 || Index < 0)
                throw new InvalidDataException("Caravan card position is outside the two-player board.");
        }
    }
    internal readonly record struct BoardNumber(Position Position, NumberCard Card);

    internal readonly record struct NumberCard(int Value, Suit Suit)
    {
        internal void Validate()
        {
            if (Value is < 1 or > 10 || !Enum.IsDefined(Suit))
                throw new InvalidDataException("A Caravan number card must have a value from 1 through 10 and a known suit.");
        }
    }

    internal readonly record struct NumberWithKings(NumberCard Card, int Kings)
    {
        internal int Value()
        {
            Card.Validate();
            if (Kings < 0) throw new InvalidDataException("Caravan king count cannot be negative.");
            var value = Card.Value;
            for (var i = 0; i < Kings; ++i) value = checked(value * 2);
            return value;
        }
    }

    internal static bool CanAppend(NumberCard previous, NumberCard next, Direction direction, Suit currentSuit)
    {
        previous.Validate(); next.Validate();
        if (!Enum.IsDefined(direction) || !Enum.IsDefined(currentSuit))
            throw new InvalidDataException("Caravan direction or suit is unknown.");
        if (previous.Value == next.Value) return false;
        return direction == Direction.Unset || next.Suit == currentSuit ||
            direction == Direction.Ascending && next.Value > previous.Value ||
            direction == Direction.Descending && next.Value < previous.Value;
    }

    internal static int Total(IEnumerable<NumberWithKings> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var total = 0;
        foreach (var card in cards) total = checked(total + card.Value());
        return total;
    }

    // An unset direction remains unset when reversed; the queen supplies suit.
    internal static (Direction Direction, Suit Suit) Queen(Direction direction, Suit queenSuit)
    {
        if (!Enum.IsDefined(queenSuit) || !Enum.IsDefined(direction))
            throw new InvalidDataException("Queen requires a known Caravan direction and suit.");
        return (direction switch
        {
            Direction.Ascending => Direction.Descending,
            Direction.Descending => Direction.Ascending,
            _ => Direction.Unset
        }, queenSuit);
    }

    // Return positions rather than mutating a board: the eventual state owner
    // must remove attached face cards and preserve source card identities.
    internal static IReadOnlyList<Position> JokerRemovals(IReadOnlyList<BoardNumber> board, Position target)
    {
        ArgumentNullException.ThrowIfNull(board);
        target.Validate();
        var positions = new HashSet<Position>();
        NumberCard? selected = null;
        foreach (var entry in board)
        {
            entry.Position.Validate(); entry.Card.Validate();
            if (!positions.Add(entry.Position)) throw new InvalidDataException("Caravan board contains a duplicate position.");
            if (entry.Position == target) selected = entry.Card;
        }
        var card = selected ?? throw new InvalidDataException("Joker target is absent from the Caravan board.");
        return board.Where(entry => entry.Position != target &&
                (card.Value == 1 ? entry.Card.Suit == card.Suit : entry.Card.Value == card.Value))
            .Select(entry => entry.Position).ToArray();
    }

    internal static Sale ResolveSale(int first, int second)
    {
        if (first < 0 || second < 0) throw new InvalidDataException("Caravan totals cannot be negative.");
        var firstSold = first is >= 21 and <= 26;
        var secondSold = second is >= 21 and <= 26;
        if (!firstSold && !secondSold || firstSold && secondSold && first == second) return Sale.Unresolved;
        return firstSold && (!secondSold || first > second) ? Sale.First : Sale.Second;
    }

    internal static Sale ResolveWinner(IReadOnlyList<(int First, int Second)> opposingCaravans)
    {
        ArgumentNullException.ThrowIfNull(opposingCaravans);
        if (opposingCaravans.Count != 3) throw new InvalidDataException("Caravan requires three opposing piles.");
        var first = 0; var second = 0;
        foreach (var (left, right) in opposingCaravans)
        {
            switch (ResolveSale(left, right))
            {
                case Sale.First: ++first; break;
                case Sale.Second: ++second; break;
                default: return Sale.Unresolved;
            }
        }
        return first > second ? Sale.First : Sale.Second;
    }
}
