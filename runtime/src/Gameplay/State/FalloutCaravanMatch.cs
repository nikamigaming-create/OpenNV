using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Shared turn owner over interpreted source-card instances. Opening draw policy
// remains explicit; match UI and settlement use the same state in flat and XR.
internal sealed class FalloutCaravanMatch
{
    internal enum Kind { Number, King, Queen, Jack, Joker }
    internal enum Phase { Opening, Playing, Finished }
    internal sealed record Card(FalloutCaravanBoard.CardIdentity Identity, Kind Kind,
        FalloutCaravanRules.Suit? Suit, int Number = 0)
    {
        internal void Validate()
        {
            if (!Enum.IsDefined(Kind) || Suit is { } suit && !Enum.IsDefined(suit) ||
                Kind != Kind.Joker && Suit is null || Identity.Instance < 0 ||
                string.IsNullOrWhiteSpace(Identity.Form.OwnerPlugin) || Identity.Form.ObjectId is 0 or > FalloutFormKey.ObjectIdMask)
                throw new InvalidDataException("Caravan card identity or interpretation is invalid.");
            if (Kind == Kind.Number) new FalloutCaravanRules.NumberCard(Number, Suit!.Value).Validate();
            else if (Number != 0) throw new InvalidDataException("A face card cannot carry a numeric value.");
        }
    }

    internal sealed record Snapshot(IReadOnlyList<Card> FirstCards, IReadOnlyList<Card> SecondCards,
        IReadOnlyList<FalloutCaravanBoard.CardIdentity> FirstStock, IReadOnlyList<FalloutCaravanBoard.CardIdentity> SecondStock,
        IReadOnlyList<FalloutCaravanBoard.CardIdentity> FirstHand, IReadOnlyList<FalloutCaravanBoard.CardIdentity> SecondHand,
        IReadOnlyList<FalloutCaravanBoard.CardIdentity> FirstDiscard, IReadOnlyList<FalloutCaravanBoard.CardIdentity> SecondDiscard,
        IReadOnlyList<FalloutCaravanBoard.Lane> Lanes, int CurrentPlayer, Phase CurrentPhase,
        FalloutCaravanRules.Sale Winner, bool DrawDuringOpening);

    private readonly Queue<Card>[] _stock;
    private readonly List<Card>[] _hands = [[], []];
    private readonly List<Card>[] _discard = [[], []];
    private readonly Dictionary<FalloutCaravanBoard.CardIdentity, Card> _cards;
    private readonly Dictionary<FalloutCaravanBoard.CardIdentity, int> _owners;
    private readonly bool _drawDuringOpening;
    private readonly FalloutCaravanBoard _board = new();
    internal int CurrentPlayer { get; private set; }
    internal Phase CurrentPhase { get; private set; } = Phase.Opening;
    internal FalloutCaravanRules.Sale Winner { get; private set; } = FalloutCaravanRules.Sale.Unresolved;

    internal FalloutCaravanMatch(IReadOnlyList<Card> firstStock, IReadOnlyList<Card> secondStock,
        int firstPlayer, bool drawDuringOpening)
    {
        ArgumentNullException.ThrowIfNull(firstStock); ArgumentNullException.ThrowIfNull(secondStock);
        if (firstPlayer is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
        if (firstStock.Count < 30 || secondStock.Count < 30)
            throw new InvalidDataException("Caravan requires at least thirty cards per deck.");
        var combined = firstStock.Concat(secondStock).ToArray();
        foreach (var card in combined)
        {
            if (card is null) throw new InvalidDataException("Caravan deck contains an absent card.");
            card.Validate();
        }
        if (combined.Select(card => card.Identity).Distinct().Count() != combined.Length)
            throw new InvalidDataException("Caravan decks contain a duplicate card instance.");
        _cards = combined.ToDictionary(card => card.Identity);
        _owners = firstStock.ToDictionary(card => card.Identity, _ => 0);
        foreach (var card in secondStock) _owners.Add(card.Identity, 1);
        _stock = [new(firstStock), new(secondStock)];
        CurrentPlayer = firstPlayer; _drawDuringOpening = drawDuringOpening;
        for (var i = 0; i < 8; ++i) { _hands[0].Add(_stock[0].Dequeue()); _hands[1].Add(_stock[1].Dequeue()); }
    }

    internal IReadOnlyList<Card> Hand(int player) => Array.AsReadOnly(_hands[Player(player)].ToArray());
    internal IReadOnlyList<Card> Discarded(int player) => Array.AsReadOnly(_discard[Player(player)].ToArray());
    internal int Remaining(int player) => _stock[Player(player)].Count;
    internal FalloutCaravanBoard.Lane Lane(int player, int caravan) => _board.GetLane(player, caravan);

    internal Snapshot Capture() => new(
        _cards.Values.Where(card => _owners[card.Identity] == 0).ToArray(),
        _cards.Values.Where(card => _owners[card.Identity] == 1).ToArray(),
        _stock[0].Select(card => card.Identity).ToArray(), _stock[1].Select(card => card.Identity).ToArray(),
        _hands[0].Select(card => card.Identity).ToArray(), _hands[1].Select(card => card.Identity).ToArray(),
        _discard[0].Select(card => card.Identity).ToArray(), _discard[1].Select(card => card.Identity).ToArray(),
        _board.Capture(), CurrentPlayer, CurrentPhase, Winner, _drawDuringOpening);

    // Gameplay restoration must not trust saved rank/suit data. The active
    // winning card record owns interpretation; distinct physical serials stay
    // in the snapshot and are checked independently by Restore.
    internal static FalloutCaravanMatch RestoreFromSource(Snapshot snapshot, FalloutPluginStack records)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(records);
        if (snapshot.FirstCards is null || snapshot.SecondCards is null)
            throw new InvalidDataException("Saved Caravan card definitions are absent.");
        var sources = new Dictionary<FalloutFormKey, FalloutCaravanCard>();
        foreach (var card in snapshot.FirstCards.Concat(snapshot.SecondCards))
        {
            if (card is null) throw new InvalidDataException("Saved Caravan card is absent.");
            card.Validate();
            if (!sources.TryGetValue(card.Identity.Form, out var source))
            {
                var record = records.GetEffective(card.Identity.Form);
                if (record.IsDeleted) throw new InvalidDataException("Saved Caravan card has a deleted winning source record.");
                source = FalloutCaravanCard.Read(records, record);
                sources.Add(card.Identity.Form, source);
            }
            if (FalloutCaravanCardAdapter.Interpret(source, card.Identity.Instance) != card)
                throw new InvalidDataException("Saved Caravan card interpretation differs from the winning source record.");
        }
        return Restore(snapshot);
    }

    internal static FalloutCaravanMatch Restore(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.FirstCards is null || snapshot.SecondCards is null || snapshot.Lanes is null ||
            snapshot.FirstStock is null || snapshot.SecondStock is null || snapshot.FirstHand is null || snapshot.SecondHand is null ||
            snapshot.FirstDiscard is null || snapshot.SecondDiscard is null || snapshot.CurrentPlayer is < 0 or > 1 ||
            !Enum.IsDefined(snapshot.CurrentPhase) || !Enum.IsDefined(snapshot.Winner))
            throw new InvalidDataException("Saved Caravan match has absent zones or invalid phase.");
        var match = new FalloutCaravanMatch(snapshot.FirstCards, snapshot.SecondCards, snapshot.CurrentPlayer, snapshot.DrawDuringOpening);
        var seen = new HashSet<FalloutCaravanBoard.CardIdentity>();
        Card Resolve(FalloutCaravanBoard.CardIdentity identity, int? owner)
        {
            if (!seen.Add(identity) || !match._cards.TryGetValue(identity, out var card) ||
                owner is { } player && match._owners[identity] != player)
                throw new InvalidDataException("Saved Caravan match has a repeated, foreign or misplaced card.");
            return card;
        }
        void Zone(IEnumerable<FalloutCaravanBoard.CardIdentity> ids, int owner, Action<Card> add)
        { foreach (var identity in ids) add(Resolve(identity, owner)); }
        for (var player = 0; player < 2; ++player)
        { match._stock[player].Clear(); match._hands[player].Clear(); match._discard[player].Clear(); }
        Zone(snapshot.FirstStock, 0, match._stock[0].Enqueue); Zone(snapshot.SecondStock, 1, match._stock[1].Enqueue);
        Zone(snapshot.FirstHand, 0, match._hands[0].Add); Zone(snapshot.SecondHand, 1, match._hands[1].Add);
        Zone(snapshot.FirstDiscard, 0, match._discard[0].Add); Zone(snapshot.SecondDiscard, 1, match._discard[1].Add);
        if (snapshot.Lanes.Count != 6) throw new InvalidDataException("Saved Caravan match requires six lanes.");
        for (var index = 0; index < snapshot.Lanes.Count; ++index)
        {
            var lane = snapshot.Lanes[index] ?? throw new InvalidDataException("Saved Caravan lane is absent.");
            if (lane.Entries is null) throw new InvalidDataException("Saved Caravan lane entries are absent.");
            foreach (var entry in lane.Entries)
            {
                if (entry is null || entry.Modifiers is null) throw new InvalidDataException("Saved Caravan entry is absent.");
                var number = Resolve(entry.Identity, index / 3);
                if (number.Kind != Kind.Number || number.Number != entry.Number.Value || number.Suit != entry.Number.Suit)
                    throw new InvalidDataException("Saved Caravan number differs from its source card.");
                foreach (var modifier in entry.Modifiers)
                {
                    var card = Resolve(modifier.Identity, null);
                    var kind = modifier.Kind switch
                    {
                        FalloutCaravanBoard.ModifierKind.King => Kind.King,
                        FalloutCaravanBoard.ModifierKind.Queen => Kind.Queen,
                        FalloutCaravanBoard.ModifierKind.Joker => Kind.Joker,
                        _ => throw new InvalidDataException("Saved Caravan modifier kind is unknown."),
                    };
                    if (card.Kind != kind || kind != Kind.Joker && card.Suit != modifier.Suit ||
                        kind == Kind.Joker && modifier.Suit is not null)
                        throw new InvalidDataException("Saved Caravan modifier differs from its source card.");
                }
            }
        }
        if (seen.Count != match._cards.Count) throw new InvalidDataException("Saved Caravan match lost a card instance.");
        match._board.Restore(snapshot.Lanes);
        var winner = match._board.Winner();
        if (snapshot.CurrentPhase == Phase.Finished ? snapshot.Winner == FalloutCaravanRules.Sale.Unresolved || snapshot.Winner != winner :
            snapshot.Winner != FalloutCaravanRules.Sale.Unresolved || snapshot.CurrentPhase == Phase.Playing && winner != FalloutCaravanRules.Sale.Unresolved)
            throw new InvalidDataException("Saved Caravan phase and winner disagree with the board.");
        if (snapshot.CurrentPhase == Phase.Opening &&
            (snapshot.Lanes.All(lane => lane.Entries.Count != 0) || snapshot.Lanes.Any(lane => lane.Entries.Count > 1 ||
                lane.Entries.Any(entry => entry.Modifiers.Count != 0))))
            throw new InvalidDataException("Saved Caravan opening contains a completed or invalid opening board.");
        match.CurrentPhase = snapshot.CurrentPhase; match.Winner = snapshot.Winner;
        return match;
    }

    internal void PlayNumber(FalloutCaravanBoard.CardIdentity identity, int caravan)
    {
        var card = InHand(identity);
        if (card.Kind != Kind.Number) throw new InvalidOperationException("Only a number card can start or extend a caravan.");
        if (CurrentPhase == Phase.Opening && _board.GetLane(CurrentPlayer, caravan).Entries.Count != 0)
            throw new InvalidOperationException("Each opening card must start a different caravan.");
        var draw = CurrentPhase != Phase.Opening || _drawDuringOpening;
        _board.PlayNumber(CurrentPlayer, caravan, card.Identity, new(card.Number, card.Suit!.Value));
        Consume(card, draw);
        if (CurrentPhase == Phase.Opening && Enumerable.Range(0, 2).All(player =>
                Enumerable.Range(0, 3).All(index => _board.GetLane(player, index).Entries.Count != 0)))
            CurrentPhase = Phase.Playing;
        FinishTurn();
    }

    internal void PlayModifier(FalloutCaravanBoard.CardIdentity identity, int targetPlayer, int caravan, int target)
    {
        RequirePlaying();
        var card = InHand(identity);
        var player = Player(targetPlayer);
        var position = new FalloutCaravanRules.Position(player, caravan, target);
        if (card.Kind is Kind.Jack or Kind.Joker)
        {
            var removed = card.Kind == Kind.Jack ? _board.ApplyJack(position) : _board.ApplyJoker(card.Identity, position);
            foreach (var removedIdentity in removed) _discard[_owners[removedIdentity]].Add(_cards[removedIdentity]);
            if (card.Kind == Kind.Jack)
            {
                _hands[CurrentPlayer].Remove(card); _discard[CurrentPlayer].Add(card); Draw();
            }
            else Consume(card, true);
            FinishTurn();
            return;
        }
        var kind = card.Kind switch
        {
            Kind.King => FalloutCaravanBoard.ModifierKind.King,
            Kind.Queen => FalloutCaravanBoard.ModifierKind.Queen,
            _ => throw new InvalidOperationException("A number card cannot be used as a modifier."),
        };
        _board.PlayModifier(player, caravan, target, new(card.Identity, kind, card.Suit!.Value));
        Consume(card, true); FinishTurn();
    }

    internal void Discard(FalloutCaravanBoard.CardIdentity identity)
    {
        var card = InHand(identity);
        if (CurrentPhase == Phase.Opening && _stock[CurrentPlayer].Count == 0)
            throw new InvalidOperationException("Opening discard requires a replacement card.");
        _hands[CurrentPlayer].Remove(card); _discard[CurrentPlayer].Add(card);
        Draw();
        if (CurrentPhase != Phase.Opening) FinishTurn();
    }

    internal void Disband(int caravan)
    {
        RequirePlaying();
        var removed = _board.Disband(CurrentPlayer, caravan);
        foreach (var identity in removed) _discard[_owners[identity]].Add(_cards[identity]);
        FinishTurn();
    }

    private Card InHand(FalloutCaravanBoard.CardIdentity identity)
    {
        if (CurrentPhase == Phase.Finished) throw new InvalidOperationException("Caravan match is finished.");
        return _hands[CurrentPlayer].SingleOrDefault(card => card.Identity == identity) ??
            throw new InvalidOperationException("Card is not in the current player's hand.");
    }
    private void RequirePlaying()
    {
        if (CurrentPhase != Phase.Playing) throw new InvalidOperationException("Caravan action is unavailable in this phase.");
    }
    private void Consume(Card card, bool draw)
    {
        if (!_hands[CurrentPlayer].Remove(card)) throw new InvalidDataException("Caravan hand lost its selected card.");
        if (draw) Draw();
    }
    private void Draw()
    {
        if (_stock[CurrentPlayer].TryDequeue(out var card)) _hands[CurrentPlayer].Add(card);
    }
    private void FinishTurn()
    {
        if (CurrentPhase == Phase.Playing && (Winner = _board.Winner()) != FalloutCaravanRules.Sale.Unresolved)
            CurrentPhase = Phase.Finished;
        else CurrentPlayer = 1 - CurrentPlayer;
    }
    private static int Player(int player) => player is 0 or 1 ? player : throw new ArgumentOutOfRangeException(nameof(player));
}
