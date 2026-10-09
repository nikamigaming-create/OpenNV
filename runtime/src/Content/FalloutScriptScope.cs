using System.Buffers.Binary;
using System.Collections;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal enum FalloutScriptScopeKind
{
    Standalone, DialogueBegin, DialogueEnd, QuestStageResult,
    PackageBegin, PackageEnd, PackageChange, TerminalEntry
}

// The record reader supplies ranges and ordinals. Equal byte sequences in two
// results do not identify the same invocation and are never searched to bind it.
internal sealed class FalloutScriptScope : IReadOnlyList<FalloutPluginSubrecord>
{
    private readonly FalloutPluginSubrecord[] _fields;
    internal FalloutPluginRecord Source { get; }
    internal FalloutScriptScopeKind Kind { get; }
    internal int StageOrdinal { get; }
    internal int ResultOrdinal { get; }
    internal int FieldStart { get; }
    internal int FieldCount => _fields.Length;
    internal string RecordSha256 { get; }
    internal string ScopeSha256 { get; }
    internal bool Compiled => _fields.Any(field => field.Signature == "SCDA");
    public int Count => _fields.Length;
    public FalloutPluginSubrecord this[int index] => _fields[index];

    private FalloutScriptScope(FalloutPluginRecord source, FalloutPluginSubrecord[] original,
        FalloutScriptScopeKind kind, int start, int count, int stageOrdinal, int resultOrdinal)
    {
        Source = source; Kind = kind; FieldStart = start; StageOrdinal = stageOrdinal;
        ResultOrdinal = resultOrdinal; _fields = original[start..checked(start + count)];
        RecordSha256 = Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant();
        ScopeSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"opennv-source-result-scope/v1\0{source.FormKey}\0{RecordSha256}\0{kind}\0{start}\0{count}\0{stageOrdinal}\0{resultOrdinal}"))).ToLowerInvariant();
    }

    internal static FalloutScriptScope Standalone(FalloutPluginRecord source)
    {
        if (source.Signature != "SCPT") throw new InvalidDataException("Standalone scope owner is not SCPT.");
        var fields = source.ReadSubrecords().ToArray();
        return new(source, fields, FalloutScriptScopeKind.Standalone, 0, fields.Length, -1, 0);
    }

    internal static FalloutScriptScope Dialogue(FalloutPluginRecord source, bool begin)
    {
        if (source.Signature != "INFO") throw new InvalidDataException("Dialogue result owner is not INFO.");
        var fields = source.ReadSubrecords().ToArray();
        var next = Enumerable.Range(0, fields.Length).Where(index => fields[index].Signature == "NEXT").ToArray();
        if (next.Length > 1 || next.Length == 1 && !fields[next[0]].Data.IsEmpty)
            throw new InvalidDataException("INFO result boundary is ambiguous or malformed.");
        var split = next.Length == 0 ? fields.Length : next[0];
        var start = begin ? 0 : next.Length == 0 ? fields.Length : split + 1;
        return new(source, fields, begin ? FalloutScriptScopeKind.DialogueBegin : FalloutScriptScopeKind.DialogueEnd,
            start, begin ? split : fields.Length - start, -1, begin ? 0 : 1);
    }

    internal static FalloutScriptScope QuestEntry(FalloutPluginRecord source, int stageField, int entryField)
    {
        if (source.Signature != "QUST") throw new InvalidDataException("Stage result owner is not QUST.");
        var fields = source.ReadSubrecords().ToArray();
        if ((uint)stageField >= fields.Length || fields[stageField].Signature != "INDX" ||
            fields[stageField].Data.Length != 2 || BinaryPrimitives.ReadInt16LittleEndian(fields[stageField].Data.Span) < 0)
            throw new InvalidDataException("Stage result has no original INDX boundary.");
        var end = stageField + 1;
        while (end < fields.Length && fields[end].Signature is not ("INDX" or "QOBJ")) ++end;
        var entries = Enumerable.Range(stageField + 1, end - stageField - 1)
            .Where(index => fields[index].Signature == "QSDT").ToArray();
        var ordinal = Array.IndexOf(entries, entryField);
        if (ordinal < 0 || entries.Length == 0 || entries[0] != stageField + 1)
            throw new InvalidDataException("Stage result has no original log-entry boundary.");
        var entryEnd = ordinal + 1 == entries.Length ? end : entries[ordinal + 1];
        var stageOrdinal = fields.Take(stageField).Count(field => field.Signature == "INDX");
        return new(source, fields, FalloutScriptScopeKind.QuestStageResult, entryField,
            entryEnd - entryField, stageOrdinal, ordinal);
    }

    internal static FalloutScriptScope PackageEvent(FalloutPluginRecord source, string eventName)
    {
        if (source.Signature != "PACK") throw new InvalidDataException("Package event scope owner is not PACK.");
        var kind = eventName switch
        {
            "POBA" => FalloutScriptScopeKind.PackageBegin,
            "POEA" => FalloutScriptScopeKind.PackageEnd,
            "POCA" => FalloutScriptScopeKind.PackageChange,
            _ => throw new InvalidDataException("Package result has no lifecycle event identity.")
        };
        var fields = source.ReadSubrecords().ToArray();
        var markers = Enumerable.Range(0, fields.Length)
            .Where(index => fields[index].Signature is "POBA" or "POEA" or "POCA").ToArray();
        if (markers.Any(index => !fields[index].Data.IsEmpty) ||
            markers.Select(index => fields[index].Signature).Distinct(StringComparer.Ordinal).Count() != markers.Length)
            throw new InvalidDataException("Package event boundaries are repeated or malformed.");
        var ordinal = Array.FindIndex(markers, index => fields[index].Signature == eventName);
        if (ordinal < 0) throw new InvalidDataException("Package result has no original event boundary.");
        var start = markers[ordinal] + 1;
        var end = ordinal + 1 < markers.Length ? markers[ordinal + 1] : fields.Length;
        return new(source, fields, kind, start, end - start, -1, ordinal);
    }

    internal static FalloutScriptScope TerminalResult(FalloutPluginRecord source, int entryOrdinal)
    {
        if (source.Signature != "TERM") throw new InvalidDataException("Terminal result scope owner is not TERM.");
        var fields = source.ReadSubrecords().ToArray();
        var entries = Enumerable.Range(0, fields.Length).Where(index => fields[index].Signature == "ITXT").ToArray();
        if ((uint)entryOrdinal >= entries.Length)
            throw new InvalidDataException("Terminal result has no original menu-entry boundary.");
        var start = entries[entryOrdinal];
        var end = entryOrdinal + 1 < entries.Length ? entries[entryOrdinal + 1] : fields.Length;
        return new(source, fields, FalloutScriptScopeKind.TerminalEntry, start, end - start, -1, entryOrdinal);
    }

    internal void RequireSource(FalloutPluginRecord source)
    {
        if (source.FormKey != Source.FormKey || source.Plugin != Source.Plugin ||
            source.HeaderOffset != Source.HeaderOffset ||
            !Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(RecordSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Script scope differs from its original winning record.");
    }

    internal string DiagnosticSource()
    {
        var sources = _fields.Where(field => field.Signature == "SCTX").ToArray();
        if (sources.Length > 1) throw new InvalidDataException("Result scope has ambiguous diagnostic source.");
        return sources.Length == 0 ? "" : FalloutDialogueTopic.ScriptText(sources[0].Data.Span);
    }

    public IEnumerator<FalloutPluginSubrecord> GetEnumerator() => ((IEnumerable<FalloutPluginSubrecord>)_fields).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
