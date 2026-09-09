using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorHealthSource(FalloutFormKey Actor, FalloutFormKey StatsOwner,
    float Health, uint Flags, FalloutFormKey BodyParts, FalloutFormKey? DeathItem, uint ImpactMaterial)
{
    internal bool Essential => (Flags & 2) != 0;
    internal bool Invulnerable => (Flags & 0x80000000) != 0;

    internal static FalloutActorHealthSource Read(FalloutPluginStack records, FalloutFormKey actor)
    {
        var source = records.GetEffective(actor);
        if (source.Signature is not ("NPC_" or "CREA")) throw new InvalidDataException("Health source is not an actor.");
        var stats = FalloutActorTemplateOwner.Resolve(records, source, 2);
        var baseData = FalloutActorTemplateOwner.Resolve(records, source, 128);
        var inventory = FalloutActorTemplateOwner.Resolve(records, source, 256);
        var model = FalloutActorTemplateOwner.Resolve(records, source, 64);
        var acbs = Field(stats, "ACBS", 24);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(Field(baseData, "ACBS", 24).Span);
        if ((BinaryPrimitives.ReadUInt32LittleEndian(acbs.Span) & 0x80) != 0)
            throw new NotSupportedException("Actor health requires its persistent encounter-level selection.");
        float health;
        if (source.Signature == "CREA") health = BinaryPrimitives.ReadInt16LittleEndian(Field(stats, "DATA", 17).Span[4..]);
        else
        {
            var data = Field(stats, "DATA", 11);
            var initial = BinaryPrimitives.ReadInt32LittleEndian(data.Span);
            // Source-zero NPCs are placed corpses. Living NPC derivation is
            // admitted separately once the engine's level term is established.
            if (initial != 0) throw new NotSupportedException("NPC derived health formula is not yet bound.");
            health = 0;
        }
        if (health < 0) throw new InvalidDataException("Actor base health is negative.");
        var body = source.Signature == "NPC_" ? records.RuntimeFormKey(0x1d) : FalloutDialogueTopic.RequiredForm(model, "PNAM");
        var death = inventory.ReadSubrecords().SingleOrDefault(field => field.Signature == "INAM").Data;
        if (!death.IsEmpty && death.Length != 4) throw new InvalidDataException("Actor death item has an invalid extent.");
        return new(actor, stats.FormKey, health, flags, body,
            death.IsEmpty ? null : inventory.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(death.Span)),
            BinaryPrimitives.ReadUInt32LittleEndian(Field(model, "NAM4", 4).Span));
    }

    private static ReadOnlyMemory<byte> Field(FalloutPluginRecord record, string signature, int size)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
        return fields.Length == 1 && fields[0].Data.Length == size ? fields[0].Data :
            throw new InvalidDataException($"Actor {record.FormKey} has invalid {signature} data.");
    }
}
