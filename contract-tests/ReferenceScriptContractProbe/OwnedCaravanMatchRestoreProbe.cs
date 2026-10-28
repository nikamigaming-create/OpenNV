using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class OwnedCaravanMatchRestoreProbe
{
    internal static void Run(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var decks = records.EffectiveRecords("CDCK"); var passed = 0;
        foreach (var record in decks)
        {
            var deck = FalloutCaravanDeck.Read(records, record); long serial = 0;
            var first = FalloutCaravanCardAdapter.InterpretDeck(records, deck, _ => serial++);
            var second = FalloutCaravanCardAdapter.InterpretDeck(records, deck, _ => serial++);
            // Supplied ordering and opening policy are fixture inputs, not a
            // claim about retail shuffle, player deck or first turn.
            var match = new FalloutCaravanMatch(first, second, firstPlayer: 0, drawDuringOpening: false);
            var saved = JsonSerializer.Serialize(match.Capture());
            var snapshot = JsonSerializer.Deserialize<FalloutCaravanMatch.Snapshot>(saved)!;
            var cold = FalloutCaravanMatch.RestoreFromSource(snapshot, records);
            if (JsonSerializer.Serialize(cold.Capture()) != saved)
                throw new InvalidDataException($"Owned deck {deck.Form} lost match instance state on cold reconstruction.");
            var altered = snapshot.FirstCards.ToArray();
            var firstCard = altered[0];
            altered[0] = firstCard with { Kind = firstCard.Kind == FalloutCaravanMatch.Kind.Joker
                ? FalloutCaravanMatch.Kind.King : FalloutCaravanMatch.Kind.Joker,
                Suit = firstCard.Kind == FalloutCaravanMatch.Kind.Joker ? FalloutCaravanRules.Suit.Hearts : null, Number = 0 };
            try
            {
                _ = FalloutCaravanMatch.RestoreFromSource(snapshot with { FirstCards = altered }, records);
                throw new InvalidOperationException("Altered source interpretation was accepted.");
            }
            catch (InvalidDataException) { }
            ++passed;
        }
        if (passed == 0 || passed != decks.Count) throw new InvalidDataException("Owned deck match reconstruction was incomplete.");
        Console.WriteLine($"OPENNV_OWNED_CARAVAN_MATCH_RESTORE_PASS decks={passed} fixtureDeal=true retailRulesAndCampaign=unverified");
    }
}
