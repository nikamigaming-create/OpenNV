using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class CaravanSourceAudit
{
    internal static int Run(FalloutPluginStack records)
    {
        var cards = new List<FalloutCaravanCard>();
        var decks = new List<FalloutCaravanDeck>();
        var failures = new List<object>();
        foreach (var record in records.EffectiveRecords("CCRD"))
        {
            try { cards.Add(FalloutCaravanCard.Read(records, record)); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
            { failures.Add(new { source = record.FormKey.ToString(), record.Signature, error = error.Message }); }
        }
        foreach (var record in records.EffectiveRecords("CDCK"))
        {
            try { decks.Add(FalloutCaravanDeck.Read(records, record)); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
            { failures.Add(new { source = record.FormKey.ToString(), record.Signature, error = error.Message }); }
        }
        var acquisitionRefused = 0; var acquisitionAdmitted = 0;
        var interpreted = new List<FalloutCaravanMatch.Card>(); var interpretationFailures = new List<object>();
        var interpretedDecks = 0;
        foreach (var deck in decks)
        {
            try { long next = 0; _ = FalloutCaravanCardAdapter.InterpretDeck(records, deck, _ => next++); ++interpretedDecks; }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
            { interpretationFailures.Add(new { source = deck.Form.ToString(), error = error.Message }); }
        }
        foreach (var card in cards)
        {
            try { interpreted.Add(FalloutCaravanCardAdapter.Interpret(card, 0)); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { interpretationFailures.Add(new { source = card.Form.ToString(), error = error.Message }); }
            try { FalloutReferenceWorld.RequirePlayerItemAcquisition(records, records.GetEffective(card.Form), records.RuntimeFormKey(0x14)); ++acquisitionAdmitted; }
            catch (NotSupportedException) { ++acquisitionRefused; }
        }
        // A resolved SCPT identity is not evidence that the card's script can
        // execute. Inventory its source and compiled lanes independently.
        var scripts = cards.GroupBy(card => card.Script).Select(group =>
        {
            var source = records.GetEffective(group.Key);
            var fields = source.ReadSubrecords().ToArray();
            var texts = fields.Where(field => field.Signature == "SCTX").ToArray();
            var compiled = fields.Where(field => field.Signature == "SCDA").ToArray();
            string disposition;
            string? error = null;
            int? events = null;
            if (texts.Length != 1) disposition = texts.Length == 0 ? "source-absent" : "source-ambiguous";
            else
            {
                try
                {
                    events = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(texts[0].Data.Span)).Count;
                    disposition = "parsed-not-executed";
                }
                catch (Exception failure) when (failure is InvalidDataException or NotSupportedException or FormatException)
                {
                    disposition = "source-parse-failed";
                    error = failure.Message;
                }
            }
            return new { script = group.Key.ToString(), cardReferences = group.Count(), sourceBodies = texts.Length,
                compiledBodies = compiled.Length, compiledBytes = compiled.Sum(field => (long)field.Data.Length),
                sourceDisposition = disposition, parsedEvents = events, error, execution = "unverified" };
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-caravan-source-audit/v1",
            cardDenominator = records.EffectiveRecords("CCRD").Count,
            deckDenominator = records.EffectiveRecords("CDCK").Count,
            decodedCards = cards.Count, decodedDecks = decks.Count, failures,
            interpretation = new { interpreted = interpreted.Count, interpretedDecks, failures = interpretationFailures,
                kinds = interpreted.GroupBy(card => card.Kind).Select(group => new { kind = group.Key.ToString(), count = group.Count() }),
                boundary = "Static source interpretation; card behavior and retail gameplay remain unverified." },
            acquisitionAdmission = new { admitted = acquisitionAdmitted, refused = acquisitionRefused,
                boundary = "Static preflight only; no pickup, event or inventory mutation was attempted." },
            cardRawIntegers = cards.GroupBy(card => new { card.FirstIntv, card.SecondIntv, card.Data })
                .Select(group => new { group.Key.FirstIntv, group.Key.SecondIntv, group.Key.Data, count = group.Count() }),
            cardLabelCorrelations = cards.GroupBy(card => new { card.FirstIntv, card.SecondIntv })
                .Select(group => new { group.Key.FirstIntv, group.Key.SecondIntv, count = group.Count(),
                    labels = group.Select(card => card.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() }),
            cardScripts = scripts,
            decks = decks.Select(deck => new { deck.Form, cards = deck.Cards.Count, distinctCards = deck.Cards.Distinct().Count(), deck.Data }),
            boundary = "Source records, references and script parsing only; script execution, card semantics, rules, AI, menu, wager and retail behavior are unverified."
        }));
        return failures.Count == 0 && interpretationFailures.Count == 0 ? 0 : 1;
    }
}
