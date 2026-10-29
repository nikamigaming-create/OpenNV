using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.Content;

// AIDT is shared by NPC_ and CREA, independently inherited through the AI-data
// template group. AV identifiers are defined once in Content.Definitions.
// Layout provenance: xEdit Core/wbDefinitionsFNV.pas, wbAIDT.
internal sealed record FalloutActorAiData(FalloutFormKey Owner, byte Aggression, byte Confidence,
    byte Energy, byte Responsibility, byte Mood, byte Assistance, bool RadiusBehavior, int Radius)
{
    private static readonly ConditionalWeakTable<FalloutPluginStack, ConcurrentDictionary<FalloutFormKey, FalloutActorAiData>> Sources = new();
    internal string Winner { get; init; } = "";
    internal string Sha256 { get; init; } = "";
    internal static FalloutActorAiData Read(FalloutPluginStack records, FalloutFormKey actor,
        FalloutActorTemplateSelection? selection = null)
    {
        // Resolve each reference's selected template before consulting the
        // immutable winning-owner cache. No modifiers or selections live here.
        var source = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor),
            (ushort)FalloutActorTemplateGroup.AiData, selection);
        return Sources.GetValue(records, _ => new()).GetOrAdd(source.FormKey, _ => ReadSource(source));
    }

    private static FalloutActorAiData ReadSource(FalloutPluginRecord source)
    {
        var fields = source.ReadSubrecords().Where(field => field.Signature == "AIDT").ToArray();
        if (fields.Length != 1) throw new InvalidDataException("Actor AI-data owner requires exactly one AIDT.");
        return Decode(source.FormKey, fields[0].Data.Span) with
        {
            Winner = source.Plugin.Name,
            Sha256 = Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant(),
        };
    }

    internal static FalloutActorAiData Decode(FalloutFormKey owner, ReadOnlySpan<byte> data)
    {
        if (data.Length != 20) throw new NotSupportedException("Actor AIDT extent is unbound.");
        var result = new FalloutActorAiData(owner, data[0], data[1], data[2], data[3], data[4], data[14],
            (data[15] & 1) != 0, BinaryPrimitives.ReadInt32LittleEndian(data[16..]));
        if (result.Aggression > 3 || result.Confidence > 4 || result.Assistance > 2 || (data[15] & ~1) != 0 || result.Radius < 0)
            throw new InvalidDataException("Actor threat parameters are invalid.");
        return result;
    }

    internal static bool Contains(FalloutActorValueId id) =>
        id is >= FalloutActorValueId.Aggression and <= FalloutActorValueId.Mood or FalloutActorValueId.Assistance;

    internal float Value(FalloutActorValueId id) => id switch
    {
        FalloutActorValueId.Aggression => Aggression,
        FalloutActorValueId.Confidence => Confidence,
        FalloutActorValueId.Energy => Energy,
        FalloutActorValueId.Responsibility => Responsibility,
        FalloutActorValueId.Mood => Mood,
        FalloutActorValueId.Assistance => Assistance,
        _ => throw new NotSupportedException($"Actor value {id} is not an AI-data value."),
    };
}
