using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorHealthSource(FalloutFormKey Actor, FalloutFormKey StatsOwner,
    float Health, uint Flags, FalloutFormKey BodyParts, FalloutFormKey? DeathItem, uint ImpactMaterial)
{
    internal bool Essential => (Flags & 2) != 0;
    internal bool Invulnerable => (Flags & 0x40000000) != 0;

    internal static bool StartsDead(FalloutPluginStack records, FalloutFormKey actor, FalloutActorTemplateSelection? selection = null)
    {
        var source = records.GetEffective(actor);
        var stats = FalloutActorTemplateOwner.Resolve(records, source, 2, selection);
        return source.Signature switch
        {
            "CREA" => BinaryPrimitives.ReadInt16LittleEndian(Field(stats, "DATA", 17).Span[4..]) == 0,
            "NPC_" => BinaryPrimitives.ReadInt32LittleEndian(Field(stats, "DATA", 11).Span) == 0,
            _ => throw new InvalidDataException("Initial health source is not an actor."),
        };
    }

    internal static FalloutActorHealthSource Read(FalloutPluginStack records, FalloutFormKey actor, FalloutActorTemplateSelection? selection = null)
    {
        var source = records.GetEffective(actor);
        if (source.Signature is not ("NPC_" or "CREA")) throw new InvalidDataException("Health source is not an actor.");
        var stats = FalloutActorTemplateOwner.Resolve(records, source, 2, selection);
        var baseData = FalloutActorTemplateOwner.Resolve(records, source, 128, selection);
        var inventory = FalloutActorTemplateOwner.Resolve(records, source, 256, selection);
        var model = FalloutActorTemplateOwner.Resolve(records, source, 64, selection);
        var acbs = Field(stats, "ACBS", 24);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(Field(baseData, "ACBS", 24).Span);
        var level = FalloutActorLevel.Resolve(acbs.Span, selection?.Level);
        float health;
        if (source.Signature == "CREA")
        {
            var initial = BinaryPrimitives.ReadInt16LittleEndian(Field(stats, "DATA", 17).Span[4..]);
            if (initial < 0) throw new InvalidDataException("Creature source health is negative.");
            var multiplier = (BinaryPrimitives.ReadUInt32LittleEndian(acbs.Span) & 0x80) != 0 ? level : 1;
            var product = initial * multiplier;
            if (product > ushort.MaxValue) throw new NotSupportedException("Creature health exceeds the supported source health range.");
            health = product;
        }
        else
        {
            var data = Field(stats, "DATA", 11);
            var initial = BinaryPrimitives.ReadInt32LittleEndian(data.Span);
            if (initial < 0) throw new InvalidDataException("NPC source health is negative.");
            health = initial;
            if (initial != 0)
            {
                var derived = (data.Span[6] + (double)FalloutGameSettingFloats.Read(records, "fAVDNPCHealthEnduranceOffset")) *
                    FalloutGameSettingFloats.Read(records, "fAVDNPCHealthEnduranceMult");
                // Only the autocalculated NPC path adds the level term. Its
                // derived contribution is truncated and clamped independently
                // of the authored base; manually configured NPCs retain the
                // floating endurance contribution. See actor-damage.md.
                if ((BinaryPrimitives.ReadUInt32LittleEndian(acbs.Span) & 0x10) != 0)
                {
                    derived += (level - 1d) * FalloutGameSettingFloats.Read(records, "fAVDNPCHealthLevelMult");
                    derived = Math.Max(0, Math.Truncate(derived));
                }
                health += (float)derived;
            }
        }
        if (!float.IsFinite(health) || health < 0) throw new InvalidDataException("Actor base health is invalid.");
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
