using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeScriptReferenceDeclaration(FalloutFormKey? Form, uint Variable);
internal sealed record FalloutNativeScriptSourceDeclaration(FalloutPluginRecord Record, string Sha256,
    byte[] Header, byte[] Code, byte[] EditorId, byte[]? Text,
    IReadOnlyList<NativeNvseScriptVariable> Variables,
    IReadOnlyList<FalloutNativeScriptReferenceDeclaration> References,
    IReadOnlyList<FalloutPluginContext> Contributors, uint LoadedFlags);

// Native declaration production reads the exact winning graph once. Runtime
// construction never substitutes a plugin display name for source identity.
internal sealed class FalloutNativePluginSourceDeclarations(FalloutPluginStack records)
{
    private readonly Dictionary<FalloutFormKey, FalloutNativeScriptSourceDeclaration> _scripts = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutPluginContext, uint> _pluginFlags = [];

    internal FalloutNativeScriptSourceDeclaration Script(FalloutFormKey key)
    {
        if (_scripts.TryGetValue(key, out var existing)) { Current(existing.Record); return existing; }
        var script = records.GetEffective(key); Current(script);
        if (script.Signature != "SCPT") throw new InvalidDataException("Native Script declaration target is not SCPT.");
        var fields = script.ReadSubrecords().ToArray();
        var header = Unique("SCHR", 20); var code = Body(fields, header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)) != code.Length)
            throw new InvalidDataException("Native Script SCHR/SCDA extents disagree.");
        var references = new List<FalloutNativeScriptReferenceDeclaration>();
        var variables = new List<NativeNvseScriptVariable>(); FalloutPluginSubrecord? scalar = null;
        foreach (var field in fields)
        {
            if (field.Signature is "SCRO" or "SCRV")
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Native reference declaration is not UInt32.");
                var value = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                references.Add(field.Signature == "SCRO" ? new(value == 0 ? null : script.Plugin.AdjustFormId(value), 0) : new(null, value));
            }
            else if (field.Signature == "SLSD")
            {
                if (scalar is not null || field.Data.Length != 24) throw new InvalidDataException("Native VarInfo scalar/name declaration order is invalid.");
                scalar = field;
            }
            else if (field.Signature == "SCVR")
            {
                if (scalar is not { } entry) throw new InvalidDataException("Native variable name lacks its ordered scalar.");
                variables.Add(new(entry.Data.ToArray(), SourceString(field.Data.Span))); scalar = null;
            }
        }
        if (scalar is not null || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != references.Count)
            throw new InvalidDataException("Native Script ordered scalar/reference declarations are incomplete.");
        var contributors = new List<FalloutPluginContext>(); var flags = 8U;
        foreach (var context in records.Plugins)
        {
            foreach (var record in context.Plugin.Records.Where(record => record.Signature != "TES4" &&
                FalloutFormKeyComparer.Instance.Equals(record.FormKey, key)))
            {
                if (record.Signature != "SCPT") throw new InvalidDataException("Native Script contributor changed its source class.");
                if (record.IsDeleted) throw new NotSupportedException("Native contributor history includes deletion/recreation and needs its actual selected lifetime owner: " + key);
                // Source LoadForm preserves the internal sticky flag before
                // copying each original winning/overridden record's flags.
                flags = record.Flags | (flags & 0x4000);
                if ((PluginFlags(context) & 1) != 0)
                    contributors.RemoveAll(previous => (PluginFlags(previous) & 1) == 0);
                if (!contributors.Contains(context)) contributors.Add(context);
            }
        }
        if (contributors.Count == 0 || !contributors.Contains(records.Plugins.Single(context => ReferenceEquals(context.Plugin, script.Plugin))))
            throw new InvalidDataException("Actual native Script winner has no source contributor lifetime.");
        var texts = fields.Where(field => field.Signature == "SCTX").ToArray();
        if (texts.Length > 1) throw new InvalidDataException("Native Script has competing source texts.");
        var result = new FalloutNativeScriptSourceDeclaration(script, Convert.ToHexString(SHA256.HashData(script.ReadData())),
            header, code, SourceString(Unique("EDID", null)), texts.Length == 0 ? null : SourceString(texts[0].Data.Span),
            variables, references, contributors, flags);
        _scripts.Add(key, result); return result;

        byte[] Unique(string signature, int? size)
        {
            var rows = fields.Where(field => field.Signature == signature).ToArray();
            if (rows.Length != 1 || size is { } count && rows[0].Data.Length != count)
                throw new InvalidDataException("Native Script source field is absent, duplicated or malformed: " + signature);
            return rows[0].Data.ToArray();
        }
    }

    internal uint PluginFlags(FalloutPluginContext context)
    {
        if (!records.Plugins.Contains(context) || !context.Plugin.NativeSourceAvailable)
            throw new InvalidOperationException("Native contributor is not an actual selected reader.");
        if (_pluginFlags.TryGetValue(context, out var value)) return value;
        var header = context.Plugin.Records.Single(record => record.Signature == "TES4");
        _pluginFlags.Add(context, header.Flags); return header.Flags;
    }
    internal void Current(FalloutPluginRecord record)
    {
        if (record.IsDeleted || !record.Plugin.NativeSourceAvailable || !ReferenceEquals(records.GetEffective(record.FormKey), record))
            throw new InvalidOperationException("Native source declaration is not its actual current winning reader.");
    }
    internal static byte[] SourceString(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty && bytes[^1] == 0) bytes = bytes[..^1];
        if (bytes.Contains((byte)0)) throw new InvalidDataException("Native source string has an interior terminator.");
        return bytes.ToArray();
    }
    // Object construction does not execute a compiled program. A disabled
    // declaration with length zero may genuinely have no SCDA subrecord.
    internal static byte[] Body(IReadOnlyList<FalloutPluginSubrecord> fields, ReadOnlySpan<byte> header)
    {
        if (header.Length != 20) throw new InvalidDataException("Native Script SCHR must declare its complete extent.");
        var rows = fields.Where(field => field.Signature == "SCDA").ToArray();
        if (rows.Length > 1 || rows.Length == 0 && BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != 0)
            throw new InvalidDataException("Native Script compiled body is ambiguous or absent for a nonempty declaration.");
        var body = rows.Length == 0 ? Array.Empty<byte>() : rows[0].Data.ToArray();
        if (BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != body.Length)
            throw new InvalidDataException("Native Script SCHR/SCDA extents disagree.");
        return body;
    }
}
