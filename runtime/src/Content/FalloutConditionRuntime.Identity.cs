using System.Buffers.Binary;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutConditionRuntime
{
    private FalloutFormKey Base(FalloutFormKey subject) => BaseIdentity?.Invoke(subject) ??
        Identity?.Invoke(subject)?.Actor ?? FalloutReferenceIdentity.Base(records, subject);

    private FalloutPluginRecord Traits(FalloutFormKey subject)
    {
        var source = records.GetEffective(Base(subject));
        if (source.Signature == "TACT") return source;
        if (source.Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Condition requires an actor traits owner.");
        return FalloutActorTemplateOwner.Resolve(records, source, 1,
            subject == records.RuntimeFormKey(0x14) ? null : world?.Get(subject).Templates);
    }

    private FalloutFormKey? ActorRace(FalloutFormKey subject)
    {
        if (Identity?.Invoke(subject) is { } identity)
        {
            if (identity.RecordType == "TACT") throw new NotSupportedException("Talking activator has no actor race query.");
            return identity.Race is null ? null : Race?.Invoke(subject) ?? identity.Race;
        }
        var traits = Traits(subject);
        if (traits.Signature == "CREA") return null;
        if (traits.Signature != "NPC_") throw new NotSupportedException("Condition has no NPC race owner.");
        return Race?.Invoke(subject) ?? (world is not null ? world.ActorRace(subject) :
            FalloutDialogueTopic.RequiredForm(traits, "RNAM"));
    }

    private bool Female(FalloutFormKey subject)
    {
        if (subject == records.RuntimeFormKey(0x14)) return Required(PlayerFemale, "player sex")();
        if (Identity?.Invoke(subject) is { RecordType: "NPC_" } identity) return identity.Female;
        var traits = Traits(subject);
        if (traits.Signature != "NPC_") throw new NotSupportedException("Condition has no qualified NPC sex owner.");
        var fields = traits.ReadSubrecords().Where(field => field.Signature == "ACBS").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 24) throw new InvalidDataException("Actor sex ACBS extent is invalid.");
        return (BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span) & 1) != 0;
    }

    private FalloutFormKey? Voice(FalloutFormKey subject)
    {
        if (Identity?.Invoke(subject) is { } identity) return identity.VoiceType;
        var traits = Traits(subject);
        var signature = traits.Signature == "TACT" ? "VNAM" : "VTCK";
        var fields = traits.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
        if (fields.Length > 1 || fields.Length == 1 && fields[0].Data.Length != 4)
            throw new InvalidDataException("Actor voice condition has an invalid source field.");
        if (fields.Length == 0) return null;
        var voice = traits.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
        if (voice is { } form && records.GetEffective(form).Signature != "VTYP")
            throw new InvalidDataException("Actor voice condition has no VTYP source.");
        return voice;
    }

    private IReadOnlyDictionary<FalloutFormKey, sbyte> ActorFactions(FalloutFormKey subject)
    {
        if (Factions is { } query) return query(subject);
        if (world is not null) return world.ActorFactions(subject);
        var identity = Identity?.Invoke(subject);
        if (identity?.RecordType == "TACT") throw new NotSupportedException("Talking activator has no actor faction query.");
        return FalloutAiPackages.ReadFactions(records, identity?.Actor ?? Base(subject), identity?.Templates);
    }

    private bool Dead(FalloutFormKey subject) => IsDead?.Invoke(subject) ??
        (subject == records.RuntimeFormKey(0x14) ? throw new NotSupportedException("Player death has no vitals owner.") :
            Required(world, "actor death").IsDead(subject));

    private float Health(FalloutFormKey subject)
    {
        if (subject == records.RuntimeFormKey(0x14)) throw new NotSupportedException("Player health percentage has no vitals owner.");
        var health = Required(world, "actor health").Health(subject);
        var maximum = health.Base + health.Permanent + health.Temporary;
        return maximum <= 0 ? 0 : Math.Clamp(health.Current / maximum, 0, 1);
    }
}
