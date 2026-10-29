using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// The selected retail scoring path uses the second ordered INTV as rank and
// the first as the suit identity. Enum labels are correlated with the owned
// card corpus; all unsupported combinations remain visible as divergence.
internal static class FalloutCaravanCardAdapter
{
    internal static IReadOnlyList<FalloutCaravanMatch.Card> InterpretDeck(FalloutPluginStack records,
        FalloutCaravanDeck deck, Func<FalloutFormKey, long> allocateInstance)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(allocateInstance);
        var cards = deck.Cards.Select(key => Interpret(FalloutCaravanCard.Read(records, records.GetEffective(key)),
            allocateInstance(key))).ToArray();
        if (cards.Select(card => card.Identity).Distinct().Count() != cards.Length)
            throw new InvalidDataException("Caravan deck instance allocator produced a duplicate identity.");
        return Array.AsReadOnly(cards);
    }

    internal static FalloutCaravanMatch.Card Interpret(FalloutCaravanCard source, long instance)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (instance < 0) throw new ArgumentOutOfRangeException(nameof(instance));
        var identity = new FalloutCaravanBoard.CardIdentity(source.Form, instance);
        if (source.FirstIntv == 5 && source.SecondIntv == 15)
            return new(identity, FalloutCaravanMatch.Kind.Joker, null);
        var suit = source.FirstIntv switch
        {
            1 => FalloutCaravanRules.Suit.Hearts,
            2 => FalloutCaravanRules.Suit.Spades,
            3 => FalloutCaravanRules.Suit.Diamonds,
            4 => FalloutCaravanRules.Suit.Clubs,
            _ => throw new NotSupportedException($"Caravan card {source.Form} has an unowned suit value {source.FirstIntv}."),
        };
        return source.SecondIntv switch
        {
            >= 1 and <= 10 => new(identity, FalloutCaravanMatch.Kind.Number, suit, source.SecondIntv),
            12 => new(identity, FalloutCaravanMatch.Kind.Jack, suit),
            13 => new(identity, FalloutCaravanMatch.Kind.Queen, suit),
            14 => new(identity, FalloutCaravanMatch.Kind.King, suit),
            _ => throw new NotSupportedException($"Caravan card {source.Form} has an unowned rank value {source.SecondIntv}."),
        };
    }
}
