using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal enum FalloutNoteKind : byte { Sound, Text, Image, Voice }

internal sealed record FalloutNote(FalloutPluginRecord Record, string SourceHash, string EditorId,
    string Name, FalloutNoteKind Kind, string? Text, string? Texture, FalloutFormKey? Topic,
    FalloutFormKey? Sound, FalloutFormKey? Actor, IReadOnlyList<FalloutFormKey> Quests)
{
    internal string RequireText() => Kind == FalloutNoteKind.Text ? Text
        ?? throw new InvalidDataException("Text note has no source text.") :
        throw new NotSupportedException($"NOTE {Record.FormKey} {Kind} content has no terminal presentation owner.");

    internal static FalloutNote Read(FalloutPluginStack records, FalloutFormKey form)
    {
        var record = records.GetEffective(form);
        if (record.Signature != "NOTE") throw new InvalidDataException("Terminal note content is not NOTE.");
        var fields = record.ReadSubrecords().ToArray();
        var editor = FalloutTerminal.Text(Required(fields, "EDID").Data.Span);
        var fullName = Optional(fields, "FULL");
        var name = fullName is { } full ? FalloutTerminal.Text(full.Data.Span) : editor;
        var data = Required(fields, "DATA").Data.Span;
        if (data.Length != 1) throw new InvalidDataException("Note DATA extent is invalid.");
        if (data[0] > 3) throw new NotSupportedException("Note content type has no format owner.");
        var kind = (FalloutNoteKind)data[0];
        string? text = null, texture = null;
        FalloutFormKey? topic = null, sound = null, actor = null;
        var tnam = Optional(fields, "TNAM");
        if (tnam is { } payload)
        {
            if (kind == FalloutNoteKind.Voice) topic = Form(records, record, payload, "DIAL");
            else text = FalloutTerminal.Text(payload.Data.Span);
        }
        var xnam = Optional(fields, "XNAM");
        if (xnam is { } image) texture = FalloutTerminal.Text(image.Data.Span);
        var snam = Optional(fields, "SNAM");
        if (snam is { } source)
        {
            if (kind == FalloutNoteKind.Voice) actor = Form(records, record, source, "NPC_", "CREA");
            else sound = Form(records, record, source, "SOUN");
        }
        var quests = fields.Where(field => field.Signature == "ONAM")
            .Select(field => Form(records, record, field, "QUST")
                ?? throw new InvalidDataException("Note quest identity is absent.")).ToArray();
        // Variant payloads are optional record fields. Preserve absence in the
        // reader; the selected presentation owner must require what it consumes.
        // A texture, sound or voice never becomes invented text.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(record.ReadData());
        hash.AppendData(Encoding.UTF8.GetBytes(record.FormKey + "\0"));
        foreach (var plugin in record.Plugin.Masters.Append(record.Plugin.Name))
            hash.AppendData(Encoding.UTF8.GetBytes(plugin.ToUpperInvariant() + "\0"));
        return new(record, Convert.ToHexString(hash.GetHashAndReset()), editor, name, kind,
            text, texture, topic, sound, actor, quests);
    }

    private static FalloutFormKey? Form(FalloutPluginStack records, FalloutPluginRecord owner,
        FalloutPluginSubrecord field, params string[] signatures)
    {
        if (field.Data.Length != 4) throw new InvalidDataException($"Note {field.Signature} extent is invalid.");
        var form = owner.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
        if (form is { } key && !signatures.Contains(records.GetEffective(key).Signature, StringComparer.Ordinal))
            throw new InvalidDataException($"Note {field.Signature} has an invalid source type.");
        return form;
    }

    private static FalloutPluginSubrecord Required(IReadOnlyList<FalloutPluginSubrecord> fields, string signature) =>
        Optional(fields, signature) ?? throw new InvalidDataException($"Note has no {signature}.");

    private static FalloutPluginSubrecord? Optional(IReadOnlyList<FalloutPluginSubrecord> fields, string signature)
    {
        var selected = fields.Where(field => field.Signature == signature).ToArray();
        if (selected.Length > 1) throw new InvalidDataException($"Note repeats {signature}.");
        return selected.Length == 0 ? null : selected[0];
    }
}
