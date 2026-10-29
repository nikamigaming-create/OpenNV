using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Raw source declarations only: the meaning of INTV/DATA and game rules are
// separate evidence lanes and must not be inferred from editor IDs or labels.
internal sealed record FalloutCaravanCard(FalloutFormKey Form, string EditorId, string Name,
    string Model, string Icon, FalloutFormKey Script, string FaceTexture, string BackTexture,
    int FirstIntv, int SecondIntv, uint Data, byte[] ObjectBounds)
{
    internal static FalloutCaravanCard Read(FalloutPluginStack records, FalloutPluginRecord record)
    {
        if (record.Signature != "CCRD") throw new InvalidDataException("Caravan card source is not CCRD.");
        var fields = record.ReadSubrecords().ToArray();
        RequireKnown(fields, ["EDID", "OBND", "FULL", "MODL", "ICON", "SCRI", "TX00", "TX01", "INTV", "DATA"]);
        var bounds = One(fields, "OBND").Data.ToArray();
        if (bounds.Length != 12) throw new InvalidDataException("Caravan card bounds must contain twelve bytes.");
        string Text(string key) => FalloutDialogueTopic.Text(One(fields, key).Data.Span);
        var integers = fields.Where(field => field.Signature == "INTV").ToArray();
        if (integers.Length != 2 || integers.Any(field => field.Data.Length != 4))
            throw new InvalidDataException("Caravan card must retain both four-byte INTV fields in source order.");
        var script = Reference(record, One(fields, "SCRI"));
        if (records.GetEffective(script).Signature != "SCPT")
            throw new InvalidDataException("Caravan card script does not resolve to SCPT.");
        return new(record.FormKey, Text("EDID"), Text("FULL"), Text("MODL"), Text("ICON"), script,
            Text("TX00"), Text("TX01"), BinaryPrimitives.ReadInt32LittleEndian(integers[0].Data.Span),
            BinaryPrimitives.ReadInt32LittleEndian(integers[1].Data.Span),
            BinaryPrimitives.ReadUInt32LittleEndian(Four(One(fields, "DATA"))), bounds);
    }

    internal static void RequireKnown(FalloutPluginSubrecord[] fields, IReadOnlyList<string> allowed)
    {
        var unknown = fields.FirstOrDefault(field => !allowed.Contains(field.Signature));
        if (unknown.Signature is not null) throw new NotSupportedException("Unowned Caravan source field: " + unknown.Signature);
    }
    internal static FalloutPluginSubrecord One(FalloutPluginSubrecord[] fields, string signature)
    {
        var matches = fields.Where(field => field.Signature == signature).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException($"Caravan source requires one {signature} field.");
    }
    internal static ReadOnlySpan<byte> Four(FalloutPluginSubrecord field) => field.Data.Length == 4 ? field.Data.Span :
        throw new InvalidDataException($"Caravan {field.Signature} field must contain four bytes.");
    internal static FalloutFormKey Reference(FalloutPluginRecord record, FalloutPluginSubrecord field) =>
        record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(Four(field)));
}

internal sealed record FalloutCaravanDeck(FalloutFormKey Form, string EditorId, string Name,
    IReadOnlyList<FalloutFormKey> Cards, uint Data)
{
    internal static FalloutCaravanDeck Read(FalloutPluginStack records, FalloutPluginRecord record)
    {
        if (record.Signature != "CDCK") throw new InvalidDataException("Caravan deck source is not CDCK.");
        var fields = record.ReadSubrecords().ToArray();
        FalloutCaravanCard.RequireKnown(fields, ["EDID", "FULL", "CARD", "DATA"]);
        var cards = fields.Where(field => field.Signature == "CARD")
            .Select(field => FalloutCaravanCard.Reference(record, field)).ToArray();
        foreach (var card in cards)
            if (records.GetEffective(card).Signature != "CCRD")
                throw new InvalidDataException($"Caravan deck {record.FormKey} contains a non-CCRD source {card}.");
        return new(record.FormKey, FalloutDialogueTopic.Text(FalloutCaravanCard.One(fields, "EDID").Data.Span),
            FalloutDialogueTopic.Text(FalloutCaravanCard.One(fields, "FULL").Data.Span), Array.AsReadOnly(cards),
            BinaryPrimitives.ReadUInt32LittleEndian(FalloutCaravanCard.Four(FalloutCaravanCard.One(fields, "DATA"))));
    }
}

// Construct only after source expressions and the command's calling reference
// have been resolved. Accepted AI values, bet rules, and modal timing require
// independent behavior evidence; this request does not start a game.
internal sealed record FalloutCaravanCommandRequest(FalloutFormKey Opponent, FalloutCaravanDeck Deck,
    int AiType, float BetPercentage)
{
    internal static FalloutCaravanCommandRequest Read(FalloutPluginStack records, FalloutFormKey opponent,
        FalloutPluginRecord deck, double aiType, double betPercentage)
    {
        if (!double.IsFinite(aiType) || aiType != Math.Truncate(aiType) || aiType < int.MinValue || aiType > int.MaxValue)
            throw new InvalidDataException("Caravan AI type must be a representable integer.");
        var bet = (float)betPercentage;
        if (!double.IsFinite(betPercentage) || !float.IsFinite(bet))
            throw new InvalidDataException("Caravan bet percentage must be a finite float.");
        return new(opponent, FalloutCaravanDeck.Read(records, deck), checked((int)aiType), bet);
    }
}
