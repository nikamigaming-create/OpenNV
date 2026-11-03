using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static class FalloutActorKnockdownSource
{
    internal static bool Allows(FalloutPluginStack records, FalloutFormKey actor, FalloutActorTemplateSelection? templates = null)
    {
        var source = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor),
            (ushort)Definitions.FalloutActorTemplateGroup.BaseData, templates);
        var data = source.ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span;
        if (data.Length != 24 || source.Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Actor knockdown requires its source ACBS declaration.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data);
        return (flags & (1u << 26)) == 0 && (source.Signature != "CREA" || (flags & (1u << 23)) == 0);
    }
}
