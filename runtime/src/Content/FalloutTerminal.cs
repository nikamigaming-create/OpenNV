using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

[Flags]
internal enum FalloutTerminalEntryFlags : byte
{
    None = 0, AddNote = 1, ForceRedraw = 2
}

internal sealed record FalloutTerminalLocal(uint Index, byte Type, string Name);
internal sealed record FalloutTerminalScriptReference(FalloutFormKey? Form, uint? LocalIndex);

// An embedded result keeps its own compiled scope. Neighboring menu items,
// an attached object script, and a matching EDID cannot lend it references.
internal sealed record FalloutTerminalResultProgram(FalloutPluginRecord Terminal, int EntryIndex,
    string Identity, IReadOnlyList<FalloutPluginSubrecord> Fields, string Source,
    uint CompiledSize, ushort Type, ushort Flags, IReadOnlyList<FalloutTerminalLocal> Locals,
    IReadOnlyList<FalloutTerminalScriptReference> References)
{
    internal bool HasSource => FalloutDialogueTopic.CodeLines(Source).Any();

    internal void RequireSourceExecution()
    {
        if (CompiledSize != 0 && !HasSource)
            throw new NotSupportedException($"TERM {Terminal.FormKey} entry {EntryIndex} has a compiled-only result.");
        if (HasSource && CompiledSize == 0)
            throw new InvalidDataException("Terminal result source has no compiled program extent.");
        if (HasSource && (Type is not (0 or 1) || Flags != 1))
            throw new NotSupportedException("Terminal result script type or execution flags have no source owner.");
        // The shared result interpreter binds world/quest locals to their
        // attached SCPT. An embedded event-list local needs its own demonstrated
        // lifetime; never substitute an unrelated attached slot with that index.
        if (HasSource && (Locals.Count != 0 || References.Any(reference => reference.LocalIndex is not null)))
            throw new NotSupportedException("Terminal result embedded locals have no event-list state owner.");
    }

    internal static FalloutTerminalResultProgram Read(FalloutPluginStack records, FalloutPluginRecord terminal,
        int entryIndex, string identity, IReadOnlyList<FalloutPluginSubrecord> fields)
    {
        var header = Required(fields, "SCHR");
        if (header.Data.Length != 20) throw new InvalidDataException("Terminal result SCHR extent is invalid.");
        var bytes = header.Data.Span;
        var referenceCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var compiledSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        var variableCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        var compiled = Optional(fields, "SCDA");
        if (compiledSize != (uint)(compiled?.Data.Length ?? 0))
            throw new InvalidDataException("Terminal result compiled extent disagrees with SCHR.");
        var source = Optional(fields, "SCTX");
        var references = new List<FalloutTerminalScriptReference>();
        var locals = new Dictionary<uint, FalloutTerminalLocal>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FalloutPluginSubrecord? declaration = null;
        foreach (var field in fields)
        {
            if (field.Signature == "SLSD")
            {
                if (field.Data.Length != 24 || declaration is not null)
                    throw new InvalidDataException("Terminal result local declaration is malformed.");
                declaration = field;
            }
            else if (field.Signature == "SCVR")
            {
                if (declaration is not { } local)
                    throw new InvalidDataException("Terminal result local name has no declaration.");
                var index = BinaryPrimitives.ReadUInt32LittleEndian(local.Data.Span);
                var type = local.Data.Span[16];
                var name = FalloutTerminal.Text(field.Data.Span);
                if (type > 5 || string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("Terminal result local type or name is invalid.");
                var value = new FalloutTerminalLocal(index, type, name);
                if (locals.TryGetValue(index, out var previous))
                {
                    // Compiler padding is not part of a local's identity.
                    if (previous != value)
                        throw new InvalidDataException("Terminal result repeats a conflicting local slot.");
                }
                else
                {
                    if (!names.Add(name)) throw new InvalidDataException("Terminal result local name is ambiguous.");
                    locals.Add(index, value);
                }
                declaration = null;
            }
            else if (field.Signature is "SCRO" or "SCRV")
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Terminal result reference extent is invalid.");
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                if (field.Signature == "SCRO")
                {
                    var form = terminal.Plugin.AdjustFormId(raw);
                    _ = records.GetEffective(form);
                    references.Add(new(form, null));
                }
                else references.Add(new(null, raw));
            }
        }
        if (declaration is not null || variableCount != (uint)locals.Count || referenceCount != (uint)references.Count)
            throw new InvalidDataException("Terminal result local/reference extents disagree with SCHR.");
        if (references.Any(reference => reference.LocalIndex is { } index && !locals.ContainsKey(index)))
            throw new InvalidDataException("Terminal result SCRV has no declared local slot.");
        return new(terminal, entryIndex, identity, fields,
            source is { } text ? FalloutDialogueTopic.ScriptText(text.Data.Span) : "", compiledSize,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]),
            locals.Values.ToArray(), references.ToArray());
    }

    private static FalloutPluginSubrecord Required(IReadOnlyList<FalloutPluginSubrecord> fields, string signature) =>
        Optional(fields, signature) ?? throw new InvalidDataException($"Terminal result has no {signature}.");

    private static FalloutPluginSubrecord? Optional(IReadOnlyList<FalloutPluginSubrecord> fields, string signature)
    {
        var selected = fields.Where(field => field.Signature == signature).ToArray();
        if (selected.Length > 1) throw new InvalidDataException($"Terminal result repeats {signature}.");
        return selected.Length == 0 ? null : selected[0];
    }
}

internal sealed record FalloutTerminalEntry(int Index, string Text, string ResultText, FalloutTerminalEntryFlags Flags,
    FalloutFormKey? Note, FalloutFormKey? Submenu, IReadOnlyList<FalloutCondition> Conditions,
    FalloutTerminalResultProgram Program)
{
    internal bool AddNote => (Flags & FalloutTerminalEntryFlags.AddNote) != 0;
    internal bool ForceRedraw => (Flags & FalloutTerminalEntryFlags.ForceRedraw) != 0;

    internal void RequireSelectionEffects()
    {
        Program.RequireSourceExecution();
        // A result may read/remove its displayed note. Until the native engine
        // order is established, inventory-before-script and script-before-
        // inventory are different behaviors, not interchangeable conveniences.
        if (AddNote && Note is not null && Program.CompiledSize != 0)
            throw new NotSupportedException("Terminal AddNote/result order has no shared source engine owner.");
    }
}

internal sealed record FalloutTerminal(FalloutPluginRecord Record, string SourceHash, string EditorId, string Name,
    string Welcome, byte Difficulty, byte Flags, byte ServerType, FalloutFormKey? Password,
    IReadOnlyList<FalloutTerminalEntry> Entries)
{
    internal const uint MenuId = 1057;
    private static readonly HashSet<string> EntryFields = new(StringComparer.Ordinal)
    { "ITXT", "RNAM", "ANAM", "INAM", "TNAM", "SCHR", "SCDA", "SCTX", "SLSD", "SCVR", "SCRO", "SCRV", "CTDA" };
    private static readonly HashSet<string> ProgramFields = new(StringComparer.Ordinal)
    { "SCHR", "SCDA", "SCTX", "SLSD", "SCVR", "SCRO", "SCRV" };

    internal static FalloutTerminal Read(FalloutPluginStack records, FalloutFormKey form)
    {
        var record = records.GetEffective(form);
        if (record.Signature != "TERM") throw new InvalidDataException("Terminal menu source is not TERM.");
        var fields = record.ReadSubrecords().ToArray();
        var firstEntry = Array.FindIndex(fields, field => field.Signature == "ITXT");
        var header = firstEntry < 0 ? fields : fields[..firstEntry];
        if (header.Any(field => EntryFields.Contains(field.Signature)))
            throw new InvalidDataException("Terminal result/menu fields precede their ITXT entry.");
        var editor = Text(Required(header, "EDID").Data.Span);
        var fullName = Optional(header, "FULL");
        var name = fullName is { } full ? Text(full.Data.Span) : editor;
        var welcome = Text(Required(header, "DESC").Data.Span);
        var data = Required(header, "DNAM").Data.Span;
        if (data.Length != 4) throw new InvalidDataException("Terminal DNAM extent is invalid.");
        if (data[0] > 5 || (data[1] & ~15) != 0 || data[2] > 9)
            throw new NotSupportedException("Terminal difficulty, flags or server type have no format owner.");
        var password = OptionalForm(records, record, header, "PNAM", "NOTE");
        _ = OptionalForm(records, record, header, "SCRI", "SCPT");
        _ = OptionalForm(records, record, header, "SNAM", "SOUN");
        var sourceHash = Hash(record);
        var entries = new List<FalloutTerminalEntry>();
        if (firstEntry >= 0)
        {
            for (var start = firstEntry; start < fields.Length;)
            {
                var end = start + 1;
                while (end < fields.Length && fields[end].Signature != "ITXT") ++end;
                var selected = fields[start..end];
                if (selected.Any(field => !EntryFields.Contains(field.Signature)))
                    throw new NotSupportedException("Terminal menu contains an unowned entry field.");
                var itemText = Text(Required(selected, "ITXT").Data.Span);
                var resultText = Text(Required(selected, "RNAM").Data.Span);
                var entryFlags = Required(selected, "ANAM").Data.Span;
                if (entryFlags.Length != 1) throw new InvalidDataException("Terminal menu ANAM extent is invalid.");
                if ((entryFlags[0] & ~3) != 0) throw new NotSupportedException("Terminal menu has unknown flags.");
                var note = OptionalForm(records, record, selected, "INAM", "NOTE");
                var submenu = OptionalForm(records, record, selected, "TNAM", "TERM");
                var conditions = selected.Where(field => field.Signature == "CTDA")
                    .Select(field => FalloutCondition.Read(record, field.Data.Span)).ToArray();
                var index = entries.Count;
                var identity = FragmentHash(sourceHash, index);
                var program = FalloutTerminalResultProgram.Read(records, record, index, identity,
                    selected.Where(field => ProgramFields.Contains(field.Signature)).ToArray());
                entries.Add(new(index, itemText, resultText, (FalloutTerminalEntryFlags)entryFlags[0], note, submenu, conditions, program));
                start = end;
            }
        }
        return new(record, sourceHash, editor, name, welcome, data[0], data[1], data[2], password, entries);
    }

    internal static string Text(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        if (end < 0 || bytes[end..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("Terminal text is not null-terminated or contains data after its terminator.");
        // Empty RNAM values in owned records include more than one zero byte.
        return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes[..end]);
    }

    internal static string Hash(FalloutPluginRecord record)
    {
        if (record.Signature != "TERM") throw new InvalidDataException("Terminal source identity is not TERM.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(record.ReadData());
        hash.AppendData(Encoding.UTF8.GetBytes(record.FormKey + "\0"));
        foreach (var plugin in record.Plugin.Masters.Append(record.Plugin.Name))
            hash.AppendData(Encoding.UTF8.GetBytes(plugin.ToUpperInvariant() + "\0"));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string FragmentHash(string sourceHash, int index)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(sourceHash));
        Span<byte> ordinal = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(ordinal, index);
        hash.AppendData(ordinal);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static FalloutFormKey? OptionalForm(FalloutPluginStack records, FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> fields, string signature, string type)
    {
        if (Optional(fields, signature) is not { } field) return null;
        if (field.Data.Length != 4) throw new InvalidDataException($"Terminal {signature} identity extent is invalid.");
        var key = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
        if (key is { } form && records.GetEffective(form).Signature != type)
            throw new InvalidDataException($"Terminal {signature} target is not {type}.");
        return key;
    }

    private static FalloutPluginSubrecord Required(IReadOnlyList<FalloutPluginSubrecord> fields, string signature) =>
        Optional(fields, signature) ?? throw new InvalidDataException($"Terminal has no {signature}.");

    private static FalloutPluginSubrecord? Optional(IReadOnlyList<FalloutPluginSubrecord> fields, string signature)
    {
        var selected = fields.Where(field => field.Signature == signature).ToArray();
        if (selected.Length > 1) throw new InvalidDataException($"Terminal repeats {signature}.");
        return selected.Length == 0 ? null : selected[0];
    }
}
