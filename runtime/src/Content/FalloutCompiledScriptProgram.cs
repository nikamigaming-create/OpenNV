using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompiledReference(uint Index, FalloutFormKey? Form, uint? Variable);
internal sealed record FalloutCompiledInstruction(int Offset, int End, ushort Opcode,
    ushort? Receiver, ReadOnlyMemory<byte> Payload);
internal sealed record FalloutCompiledEvent(ushort Event, int Begin, int End,
    ReadOnlyMemory<byte> Parameters);

// SCDA owns executable instructions. SCTX is not parsed or used to select
// instructions or operands; an unknown reached instruction remains a failure.
internal sealed class FalloutCompiledScriptProgram
{
    internal const int DecoderVersion = 3;
    internal FalloutScriptScope Scope { get; }
    internal FalloutPluginRecord Source { get; }
    internal string ProgramSha256 { get; }
    internal IReadOnlyList<FalloutCompiledReference> References { get; }
    internal IReadOnlyList<FalloutCompiledInstruction> Instructions { get; }
    internal IReadOnlyList<FalloutCompiledEvent> Events { get; }
    internal int CodeBytes { get; }
    internal bool Standalone { get; }
    internal int ScopeStart { get; }
    internal ushort ScriptType { get; }
    internal byte CompiledFlag { get; }
    internal uint LocalCount { get; }

    private FalloutCompiledScriptProgram(FalloutPluginRecord source, ReadOnlyMemory<byte> code,
        IReadOnlyList<FalloutCompiledReference> references,
        IReadOnlyList<FalloutCompiledInstruction> instructions, IReadOnlyList<FalloutCompiledEvent> events,
        bool standalone, FalloutScriptScope scope, ReadOnlyMemory<byte> header)
    {
        Source = source; References = references; Instructions = instructions; Events = events;
        CodeBytes = code.Length; Standalone = standalone; Scope = scope; ScopeStart = scope.FieldStart;
        ScriptType = BinaryPrimitives.ReadUInt16LittleEndian(header.Span[16..]);
        CompiledFlag = header.Span[18]; LocalCount = BinaryPrimitives.ReadUInt32LittleEndian(header.Span[12..]);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var identity = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(identity, DecoderVersion);
        BinaryPrimitives.WriteInt32LittleEndian(identity.AsSpan(4), scope.FieldStart);
        hash.AppendData(identity);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(scope.ScopeSha256));
        hash.AppendData(header.Span);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(source.FormKey + "\0" + standalone + "\0"));
        hash.AppendData(code.Span);
        foreach (var reference in references)
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(
                $"{reference.Index}:{reference.Form}:{reference.Variable}\0"));
        ProgramSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static bool HasInstructions(IReadOnlyList<FalloutPluginSubrecord> fields) =>
        fields.Any(field => field.Signature == "SCDA" && !field.Data.IsEmpty);

    internal static bool HasProgram(IReadOnlyList<FalloutPluginSubrecord> fields) =>
        fields.Any(field => field.Signature == "SCDA");

    internal static void RequireDiagnosticOnly(FalloutPluginRecord source,
        IReadOnlyList<FalloutPluginSubrecord> fields, string family)
    {
        if (HasProgram(fields))
            throw new NotSupportedException($"Compiled {source.FormKey}/{family} has no admitted execution/continuation owner; diagnostic SCTX fallback is refused.");
    }

    internal static FalloutCompiledScriptProgram Read(FalloutPluginRecord source,
        IReadOnlyList<FalloutPluginSubrecord> fields, bool standalone)
    {
        if (fields is not FalloutScriptScope scope)
            throw new InvalidDataException("Compiled instructions require a source-owned range and ordinal.");
        scope.RequireSource(source);
        if (standalone != (scope.Kind == FalloutScriptScopeKind.Standalone) ||
            standalone != (source.Signature == "SCPT"))
            throw new InvalidDataException("Compiled standalone/result classification differs from its source scope.");
        var headers = fields.Where(field => field.Signature == "SCHR").ToArray();
        var bodies = fields.Where(field => field.Signature == "SCDA").ToArray();
        var links = fields.Where(field => field.Signature is "SCRO" or "SCRV").ToArray();
        if (headers.Length != 1 || headers[0].Data.Length != 20 || bodies.Length != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(headers[0].Data.Span[4..]) != links.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(headers[0].Data.Span[8..]) != bodies[0].Data.Length)
            throw new InvalidDataException("Compiled script body/reference scope differs from SCHR.");
        if (standalone && source.Signature != "SCPT")
            throw new InvalidDataException("Standalone compiled program source is not SCPT.");
        var references = links.Select((field, index) =>
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Compiled reference is not UInt32.");
            var value = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
            if (value == 0) throw new NotSupportedException("Compiled null reference-list entry is unbound.");
            return field.Signature == "SCRO"
                ? new FalloutCompiledReference(checked((uint)index + 1), source.Plugin.AdjustFormId(value), null)
                : new FalloutCompiledReference(checked((uint)index + 1), null, value);
        }).ToArray();
        var code = bodies[0].Data;
        var instructions = DecodeInstructions(code);
        var events = DecodeEvents(instructions, code.Length, standalone);
        return new(source, code, references, instructions, events, standalone, scope, headers[0].Data);
    }

    private static IReadOnlyList<FalloutCompiledInstruction> DecodeInstructions(ReadOnlyMemory<byte> code)
    {
        var result = new List<FalloutCompiledInstruction>();
        var offset = 0;
        while (offset < code.Length)
        {
            var start = offset;
            if (code.Length - offset < 4) throw new InvalidDataException($"Truncated compiled header at {offset:x}.");
            var opcode = BinaryPrimitives.ReadUInt16LittleEndian(code.Span[offset..]);
            var extent = BinaryPrimitives.ReadUInt16LittleEndian(code.Span[(offset + 2)..]);
            offset += 4;
            ushort? receiver = null;
            if (opcode == 0x1c)
            {
                receiver = extent;
                if (receiver == 0 || code.Length - offset < 4)
                    throw new InvalidDataException($"Malformed compiled reference-call header at {start:x}.");
                opcode = BinaryPrimitives.ReadUInt16LittleEndian(code.Span[offset..]);
                extent = BinaryPrimitives.ReadUInt16LittleEndian(code.Span[(offset + 2)..]);
                offset += 4;
                if (opcode < 0x1000)
                    throw new InvalidDataException("Compiled reference-call header targets a non-command instruction.");
            }
            if (extent > code.Length - offset)
                throw new InvalidDataException($"Compiled instruction {opcode:x4} exceeds SCDA at {start:x}.");
            result.Add(new(start, offset + extent, opcode, receiver, code.Slice(offset, extent)));
            offset += extent;
        }
        return result;
    }

    private static IReadOnlyList<FalloutCompiledEvent> DecodeEvents(
        IReadOnlyList<FalloutCompiledInstruction> instructions, int bytes, bool standalone)
    {
        var events = new List<FalloutCompiledEvent>();
        var index = 0;
        if (standalone)
        {
            if (instructions.Count == 0)
                throw new InvalidDataException("Standalone compiled script has no canonical SCN instruction.");
            var name = instructions[0];
            if (name.Opcode != 0x1d || name.Offset != 0 || !name.Payload.IsEmpty || name.Receiver is not null)
                throw new InvalidDataException("Standalone compiled script has no canonical SCN instruction.");
            index = 1;
        }
        while (index < instructions.Count)
        {
            var instruction = instructions[index];
            if (!standalone)
            {
                if (instruction.Opcode is 0x10 or 0x11 or 0x1d)
                    throw new InvalidDataException("Embedded compiled result contains a standalone block header.");
                ++index;
                continue;
            }
            if (instruction.Opcode != 0x10 || instruction.Receiver is not null || instruction.Payload.Length < 6)
                throw new NotSupportedException($"Standalone compiled instruction outside an event block at {instruction.Offset:x}.");
            var type = BinaryPrimitives.ReadUInt16LittleEndian(instruction.Payload.Span);
            var extent = BinaryPrimitives.ReadUInt32LittleEndian(instruction.Payload.Span[2..]);
            if (extent < 4 || extent > bytes - instruction.End)
                throw new InvalidDataException("Compiled event block extent exceeds SCDA.");
            var end = checked(instruction.End + (int)extent);
            var cursor = index + 1;
            while (cursor < instructions.Count && instructions[cursor].End < end)
            {
                if (instructions[cursor].Opcode is 0x10 or 0x11 or 0x1d)
                    throw new InvalidDataException("Compiled event blocks overlap or nest.");
                ++cursor;
            }
            if (cursor >= instructions.Count || instructions[cursor] is not { Opcode: 0x11 } last ||
                last.End != end || !last.Payload.IsEmpty || last.Receiver is not null)
                throw new InvalidDataException("Compiled event extent has no exact END boundary.");
            events.Add(new(type, instruction.End, last.Offset, instruction.Payload[6..]));
            index = cursor + 1;
        }
        return events;
    }

    internal IEnumerable<FalloutCompiledInstruction> ResultInstructions() => !Standalone
        ? Instructions
        : throw new InvalidOperationException("Standalone compiled events require their event admission.");

    internal IEnumerable<FalloutCompiledInstruction> EventInstructions(FalloutCompiledEvent block)
    {
        if (!Events.Contains(block)) throw new InvalidDataException("Compiled event belongs to another source program.");
        return Instructions.Where(instruction => instruction.Offset >= block.Begin && instruction.End <= block.End);
    }

    internal FalloutCompiledReference Reference(ushort index) =>
        index is > 0 && index <= References.Count ? References[index - 1] :
            throw new InvalidDataException($"Compiled reference index {index} is outside its ordered source table.");
}
