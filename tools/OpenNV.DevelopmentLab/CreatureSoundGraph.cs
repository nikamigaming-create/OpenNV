using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class CreatureSoundGraph
{
    internal static int Run(FalloutPluginStack records)
    {
        var rows = new List<object>();
        var failures = new List<object>();
        var sources = records.EffectiveRecords("CREA").ToArray();
        foreach (var source in sources)
        {
            try
            {
                var fields = source.ReadSubrecords().ToArray();
                FalloutFormKey? Link(string signature)
                {
                    var matches = fields.Where(value => value.Signature == signature).ToArray();
                    if (matches.Length > 1 || matches.Length == 1 && matches[0].Data.Length != 4)
                        throw new InvalidDataException($"Invalid {signature} extent or multiplicity.");
                    return matches.Length == 0 ? null : source.Plugin.AdjustOptionalFormId(
                        BinaryPrimitives.ReadUInt32LittleEndian(matches[0].Data.Span));
                }
                var sound = Link("CSCR"); var template = Link("TPLT");
                FalloutPluginRecord? Target(FalloutFormKey? key) => key is null ? null : records.GetEffective(key.Value);
                var acbs = fields.Where(value => value.Signature == "ACBS").ToArray();
                if (acbs.Length != 1 || acbs[0].Data.Length != 24)
                    throw new InvalidDataException("Invalid creature ACBS extent or multiplicity.");
                var types = fields.Where(value => value.Signature == "CSDT").Select(value =>
                    value.Data.Length == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(value.Data.Span) :
                    throw new InvalidDataException("Invalid CSDT extent.")).ToArray();
                var soundTarget = Target(sound); var templateTarget = Target(template);
                var soundSignature = soundTarget?.Signature; var templateSignature = templateTarget?.Signature;
                if (soundSignature is not (null or "CREA") || templateSignature is not (null or "CREA" or "LVLC"))
                    throw new InvalidDataException("Sound or actor template has an unexpected winning record type.");
                static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
                rows.Add(new { source = source.FormKey.ToString(), sourceSha256 = Hash(source), deleted = source.IsDeleted,
                    sound = sound?.ToString(), soundSha256 = soundTarget is null ? null : Hash(soundTarget), soundDeleted = soundTarget?.IsDeleted,
                    template = template?.ToString(), templateSignature,
                    templateSha256 = templateTarget is null ? null : Hash(templateTarget), templateDeleted = templateTarget?.IsDeleted,
                    templateFlags = BinaryPrimitives.ReadUInt16LittleEndian(acbs[0].Data.Span[22..]), types });
            }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or KeyNotFoundException)
            { failures.Add(new { source = source.FormKey.ToString(), error = error.Message }); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-creature-sound-graph/v1",
            denominator = sources.Length, decoded = rows.Count, failures, rows,
            boundary = "Source links only; inheritance precedence, execution and retail behavior are unverified." }));
        return failures.Count == 0 ? 0 : 1;
    }
}
