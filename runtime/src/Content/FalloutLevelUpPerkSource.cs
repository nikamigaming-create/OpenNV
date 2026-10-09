using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutLevelUpPerkEffect(byte Type, byte Rank, byte Priority, int SourceIndex,
    FalloutFormKey? Form, byte? QuestStageBits, byte? EntryPoint);
internal sealed record FalloutLevelUpPerkSource(FalloutFormKey Form, string SourceSha256, string EditorId,
    string? Name, string? Description, string? Icon, string? SmallIcon, bool Trait, byte MinimumLevel,
    byte MaximumRank, bool Playable, bool Hidden, IReadOnlyList<FalloutCondition> Conditions,
    IReadOnlyList<FalloutLevelUpPerkEffect> Effects, FalloutFormKey? NextPerk)
{
    // Metadata and master binding only. This does not replace the acquired
    // rank/effect owner, condition evaluation, or the source menu's filters.
    internal static FalloutLevelUpPerkSource Read(FalloutPluginStack records, FalloutFormKey form)
    {
        var source = records.GetEffective(form);
        if (source.Signature != "PERK") throw new InvalidDataException("Level-up menu choice is not PERK.");
        var fields = source.ReadSubrecords().ToArray();
        var top = new List<FalloutPluginSubrecord>();
        var effects = new List<FalloutLevelUpPerkEffect>();
        for (var index = 0; index < fields.Length; ++index)
        {
            if (fields[index].Signature != "PRKE")
            {
                if (fields[index].Signature is not ("EDID" or "FULL" or "DESC" or "ICON" or "MICO" or "CTDA" or "DATA" or "NNAM"))
                    throw new NotSupportedException($"Perk {form} top-level field {fields[index].Signature} requires its source owner.");
                top.Add(fields[index]); continue;
            }
            var header = fields[index].Data.Span;
            if (header.Length != 3) throw new InvalidDataException("Perk effect header extent is invalid.");
            var end = Array.FindIndex(fields, index + 1, field => field.Signature == "PRKF");
            if (end < 0 || fields[end].Data.Length != 0 || fields[(index + 1)..end].Any(field => field.Signature == "PRKE"))
                throw new InvalidDataException("Perk effect is unterminated or overlaps another effect.");
            var group = fields[(index + 1)..end];
            var payloads = group.Where(field => field.Signature == "DATA").ToArray();
            if (payloads.Length != 1) throw new InvalidDataException("Perk effect has missing or duplicate data.");
            var payload = payloads[0].Data.Span;
            FalloutFormKey? effectForm = null; byte? stage = null, entry = null;
            switch (header[0])
            {
                case 0 when payload.Length == 8:
                    effectForm = source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(payload)); stage = payload[4]; break;
                case 1 when payload.Length == 4:
                    effectForm = source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(payload)); break;
                case 2 when payload.Length == 3:
                    entry = payload[0]; break;
                case 0 or 1 or 2: throw new InvalidDataException("Perk effect data extent differs from its type.");
                default: throw new NotSupportedException($"Perk effect type {header[0]} is unbound.");
            }
            if (effectForm is { } linked && records.GetEffective(linked).Signature != (header[0] == 0 ? "QUST" : "SPEL"))
                throw new InvalidDataException("Perk effect link has the wrong winning record type.");
            effects.Add(new(header[0], header[1], header[2], effects.Count, effectForm, stage, entry));
            index = end;
        }
        var declarations = top.Where(field => field.Signature == "DATA").ToArray();
        if (declarations.Length != 1 || declarations[0].Data.Length is not (4 or 5))
            throw new NotSupportedException("Level-up PERK metadata requires its four/five-byte source declaration.");
        var data = declarations[0].Data.Span;
        if (data[0] > 1 || data[3] > 1 || data.Length == 5 && data[4] > 1) throw new InvalidDataException("Perk declaration flags are invalid.");
        var nextFields = top.Where(field => field.Signature == "NNAM").ToArray();
        if (nextFields.Length > 1 || nextFields.Any(field => field.Data.Length != 4))
            throw new InvalidDataException("Next perk source link is invalid.");
        var next = nextFields.Length == 0 ? null : source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(nextFields[0].Data.Span));
        if (next is { } nextForm && records.GetEffective(nextForm).Signature != "PERK")
            throw new InvalidDataException("Next perk source link is not PERK.");
        return new(form, Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant(),
            Text("EDID", true)!, Text("FULL"), Text("DESC"), Text("ICON"), Text("MICO"),
            data[0] != 0, data[1], data[2], data[3] != 0, data.Length == 5 && data[4] != 0,
            top.Where(field => field.Signature == "CTDA").Select(field => FalloutCondition.Read(source, field.Data.Span)).ToArray(), effects, next);

        string? Text(string signature, bool required = false)
        {
            var matches = top.Where(field => field.Signature == signature).ToArray();
            if (matches.Length == 0 && !required) return null;
            if (matches.Length != 1) throw new InvalidDataException($"Perk {signature} declaration is missing or duplicated.");
            var bytes = matches[0].Data.Span;
            if (bytes.Length == 0 || bytes.IndexOf((byte)0) != bytes.Length - 1)
                throw new InvalidDataException($"Perk {signature} text is not null-terminated.");
            return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes[..^1]);
        }
    }
}
