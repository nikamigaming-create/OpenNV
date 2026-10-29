using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Shared match input and opponent ownership. The opponent policy is first-party;
// difficulty selects the strength of that policy, not a native-strategy claim.
internal sealed class FalloutCaravanSession
{
    internal enum ActionKind { Number, Modifier, Discard, Disband }
    internal sealed record Move(ActionKind Kind, FalloutCaravanBoard.CardIdentity Card = default,
        int Player = 0, int Caravan = 0, int Target = 0);

    private readonly FalloutCaravanWagerPolicy _wagerPolicy;
    private readonly FalloutSoundRandomState _random;
    private readonly IReadOnlyList<FalloutCaravanMatch.Card> _opponentCards;
    private readonly Action<bool>? _prepareCompleted;
    private readonly Action<bool>? _completed;
    internal IReadOnlyList<FalloutCaravanMatch.Card> Collection { get; }
    internal FalloutCaravanCommandRequest Request { get; }
    internal FalloutCaravanMatch? Match { get; private set; }
    internal int Wager { get; private set; }
    internal bool Finished { get; private set; }
    internal bool PlayerWon { get; private set; }
    internal string? Error { get; private set; }
    internal string LastAction { get; private set; } = "Choose at least thirty cards for your deck.";
    internal int MaximumWager => _wagerPolicy.Maximum(Request);
    internal long PlayerCurrency => _wagerPolicy.PlayerValue;
    internal long OpponentCurrency => _wagerPolicy.OpponentValue;

    internal FalloutCaravanSession(FalloutPluginStack records, FalloutPlayerCaravanCards collection,
        FalloutCaravanCommandRequest request, FalloutPlayerInventory playerInventory,
        FalloutPlayerInventory opponentInventory, FalloutFormKey caps, Action<bool>? prepareCompleted = null,
        Action<bool>? completed = null)
    {
        Request = request;
        _wagerPolicy = new(playerInventory, opponentInventory, caps);
        _prepareCompleted = prepareCompleted; _completed = completed;
        _random = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
        long serial = 0;
        Collection = collection.First.Concat(collection.Second).Select(form =>
            FalloutCaravanCardAdapter.Interpret(FalloutCaravanCard.Read(records, records.GetEffective(form)), serial++)).ToArray();
        _opponentCards = FalloutCaravanCardAdapter.InterpretDeck(records, request.Deck, _ => serial++);
    }

    internal void Start(IReadOnlyCollection<FalloutCaravanBoard.CardIdentity> selected, int wager)
    {
        if (Match is not null || Finished) throw new InvalidOperationException("A Caravan match has already started.");
        if (wager < 0 || wager > MaximumWager) throw new InvalidOperationException("Wager exceeds the available currency or source maximum.");
        _wagerPolicy.RequirePayable(wager);
        var identities = selected.ToHashSet();
        if (identities.Count != selected.Count || identities.Any(identity => !Collection.Any(card => card.Identity == identity)))
            throw new InvalidOperationException("The selected deck contains a duplicate or absent collection card.");
        var deck = Collection.Where(card => identities.Contains(card.Identity)).ToArray();
        if (deck.Count(card => card.Kind == FalloutCaravanMatch.Kind.Number) < 3 ||
            _opponentCards.Count(card => card.Kind == FalloutCaravanMatch.Kind.Number) < 3)
            throw new InvalidOperationException("Each Caravan deck must contain at least three number cards.");
        Match = new(Shuffle(deck), Shuffle(_opponentCards), firstPlayer: 0, drawDuringOpening: false);
        Wager = wager; LastAction = "Place a number card on each of your three caravans.";
    }

    private IReadOnlyList<FalloutCaravanMatch.Card> Shuffle(IEnumerable<FalloutCaravanMatch.Card> cards)
    {
        var deck = cards.ToArray();
        for (var index = deck.Length - 1; index > 0; --index)
        {
            var other = (int)_random.NextBounded((uint)(index + 1));
            (deck[index], deck[other]) = (deck[other], deck[index]);
        }
        return deck;
    }

    internal IReadOnlyList<Move> LegalMoves()
    {
        if (Match is not { } match || Finished || match.CurrentPhase == FalloutCaravanMatch.Phase.Finished) return [];
        var player = match.CurrentPlayer;
        var moves = new List<Move>();
        foreach (var card in match.Hand(player))
        {
            if (card.Kind == FalloutCaravanMatch.Kind.Number)
                for (var caravan = 0; caravan < 3; ++caravan)
                {
                    var lane = match.Lane(player, caravan);
                    if (match.CurrentPhase == FalloutCaravanMatch.Phase.Opening && lane.Entries.Count != 0) continue;
                    if (lane.Entries.Count == 0 || FalloutCaravanRules.CanAppend(lane.Entries[^1].Number,
                        new(card.Number, card.Suit!.Value), lane.Direction, lane.Suit!.Value))
                        moves.Add(new(ActionKind.Number, card.Identity, player, caravan));
                }
            else if (match.CurrentPhase == FalloutCaravanMatch.Phase.Playing)
                for (var side = 0; side < 2; ++side)
                    for (var caravan = 0; caravan < 3; ++caravan)
                        for (var target = 0; target < match.Lane(side, caravan).Entries.Count; ++target)
                            moves.Add(new(ActionKind.Modifier, card.Identity, side, caravan, target));
            if (match.CurrentPhase == FalloutCaravanMatch.Phase.Playing || match.Remaining(player) > 0)
                moves.Add(new(ActionKind.Discard, card.Identity));
        }
        if (match.CurrentPhase == FalloutCaravanMatch.Phase.Playing)
            for (var caravan = 0; caravan < 3; ++caravan)
                if (match.Lane(player, caravan).Entries.Count != 0) moves.Add(new(ActionKind.Disband, Caravan: caravan));
        return moves;
    }

    internal void Play(Move move)
    {
        if (Match is not { CurrentPlayer: 0 } || Finished) throw new InvalidOperationException("It is not your turn.");
        Apply(move);
    }

    private void Apply(Move move)
    {
        var match = Match ?? throw new InvalidOperationException("Caravan has no active match.");
        if (!LegalMoves().Contains(move)) throw new InvalidOperationException("That card cannot be played at this position.");
        var player = match.CurrentPlayer;
        var card = match.Hand(player).SingleOrDefault(value => value.Identity == move.Card);
        ApplyTo(match, move);
        LastAction = $"{(player == 0 ? "You" : "Opponent")}: " + (move.Kind switch
        {
            ActionKind.Number => $"{Label(card!)} on caravan {move.Caravan + 1}.",
            ActionKind.Modifier => $"{Label(card!)} on {(move.Player == 0 ? "your" : "opponent's")} caravan {move.Caravan + 1}.",
            ActionKind.Discard => $"discarded {Label(card!)}.",
            _ => $"disbanded caravan {move.Caravan + 1}."
        });
        if (match.CurrentPhase == FalloutCaravanMatch.Phase.Finished)
            Finish(match.Winner == FalloutCaravanRules.Sale.First);
        else if (match.Hand(match.CurrentPlayer).Count == 0 || LegalMoves().Count == 0)
            Finish(match.CurrentPlayer == 1);
    }

    private static void ApplyTo(FalloutCaravanMatch match, Move move)
    {
        switch (move.Kind)
        {
            case ActionKind.Number: match.PlayNumber(move.Card, move.Caravan); break;
            case ActionKind.Modifier: match.PlayModifier(move.Card, move.Player, move.Caravan, move.Target); break;
            case ActionKind.Discard: match.Discard(move.Card); break;
            case ActionKind.Disband: match.Disband(move.Caravan); break;
            default: throw new InvalidOperationException("Unknown Caravan action.");
        }
    }

    internal void PlayOpponent()
    {
        if (Match is not { CurrentPlayer: 1 } match || Finished) return;
        var moves = LegalMoves();
        if (moves.Count == 0) { Finish(true); return; }
        Move? best = null; var bestValue = double.NegativeInfinity;
        foreach (var move in moves)
        {
            var candidate = FalloutCaravanMatch.Restore(match.Capture());
            ApplyTo(candidate, move);
            var value = Evaluate(candidate, 1);
            // Do not spend a turn disbanding a useful lane or discarding when
            // a board improvement is available. Equal moves are randomized.
            if (move.Kind == ActionKind.Discard) value -= 1;
            if (move.Kind == ActionKind.Disband) value -= 2;
            value += _random.NextUnitFloat() * (3 - Request.AiType) * 8;
            if (value > bestValue) { best = move; bestValue = value; }
        }
        Apply(best!);
    }

    private static double Evaluate(FalloutCaravanMatch match, int player)
    {
        if (match.CurrentPhase == FalloutCaravanMatch.Phase.Finished)
            return match.Winner == (player == 0 ? FalloutCaravanRules.Sale.First : FalloutCaravanRules.Sale.Second) ? 100000 : -100000;
        double value = 0;
        for (var caravan = 0; caravan < 3; ++caravan)
        {
            var own = Total(match.Lane(player, caravan)); var other = Total(match.Lane(1 - player, caravan));
            var sale = FalloutCaravanRules.ResolveSale(own, other);
            value += sale == FalloutCaravanRules.Sale.First ? 100 : sale == FalloutCaravanRules.Sale.Second ? -100 : 0;
            value += own > 26 ? -40 - own : own >= 21 ? 25 + own : own;
            value -= other > 26 ? -40 : other >= 21 ? 25 + other : other * .4;
        }
        return value;
    }

    internal static int Total(FalloutCaravanBoard.Lane lane) => FalloutCaravanRules.Total(lane.Entries.Select(entry =>
        new FalloutCaravanRules.NumberWithKings(entry.Number, entry.Modifiers.Count(modifier => modifier.Kind == FalloutCaravanBoard.ModifierKind.King))));
    internal static string Label(FalloutCaravanMatch.Card card) =>
        (card.Kind == FalloutCaravanMatch.Kind.Number ? card.Number == 1 ? "Ace" : card.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : card.Kind.ToString()) +
        (card.Suit is { } suit ? " of " + suit : "");

    internal FalloutCaravanMatch.Card Card(FalloutCaravanBoard.CardIdentity identity) =>
        Collection.Concat(_opponentCards).Single(card => card.Identity == identity);

    internal void Forfeit()
    {
        if (Match is { } match && !Finished)
            Finish(match.CurrentPhase == FalloutCaravanMatch.Phase.Finished && match.Winner == FalloutCaravanRules.Sale.First);
    }

    private void Finish(bool won)
    {
        if (Finished) return;
        if (Error is not null) throw new InvalidOperationException("Caravan outcome publication stopped: " + Error);
        try
        {
            _prepareCompleted?.Invoke(won);
            // Settlement is one conserved, atomic transfer between real gameplay
            // inventories, ready for any admitted nonzero source wager policy.
            _wagerPolicy.Settle(Wager, won);
            Finished = true; PlayerWon = won;
            // Currency is committed before statistics/challenges. A callback
            // fault retains that prefix and never replays payment or its count.
            _completed?.Invoke(won);
        }
        catch (Exception error) { Error = error.Message; throw; }
    }
}
