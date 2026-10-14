using System.Buffers.Binary;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// A bounded source CFG reader. It never executes instructions. Unknown lengths,
// indirect targets and child edges refuse; a marker alone cannot close a body.
internal static class NativePluginConstructorFootprint
{
    internal sealed record Body(uint Start, uint Bytes, IReadOnlySet<uint> Children,
        IReadOnlyDictionary<uint, bool>? Branches = null, IReadOnlySet<uint>? IndexedWrites = null);
    private sealed record Instruction(uint Next, uint? Target, bool Conditional, bool Return,
        uint? Import, bool Call, bool Indirect, bool Write, bool Stack, bool Thread, uint? Cell);
    internal static void Check(NativePluginConstructorImage source, Body body,
        IReadOnlySet<string> imports, HashSet<uint> writes)
    {
        var end = checked(body.Start + body.Bytes); var code = source.Read(body.Start, checked((int)body.Bytes), true);
        var pending = new Stack<uint>(); var seen = new HashSet<uint>(); var occupied = new HashSet<uint>(); pending.Push(body.Start);
        while (pending.Count != 0)
        {
            var at = pending.Pop(); if (!seen.Add(at)) continue;
            if (at < body.Start || at >= end || occupied.Contains(at) || ++source.Instructions > 8192)
                throw new NotSupportedException("Original constructor CFG has an overlapping, foreign or unbounded instruction.");
            var instruction = Decode(code, body.Start, at);
            for (var cursor = at + 1; cursor < instruction.Next; ++cursor)
                if (seen.Contains(cursor) || !occupied.Add(cursor)) throw new NotSupportedException("Original constructor branches overlap an instruction.");
            if (instruction.Write)
            {
                if (instruction.Cell is { } cell && !instruction.Thread)
                {
                    var rva = source.ToRva(cell); source.RequireWritable(rva, 4); writes.Add(rva);
                }
                else if (!instruction.Stack && !instruction.Thread && body.IndexedWrites?.Contains(at) != true)
                    throw new NotSupportedException("Original constructor requires an independent indexed store/buffer owner.");
            }
            if (instruction.Import is { } slot)
            {
                var declaration = source.Import(source.ToRva(slot));
                if (declaration.Name is null || !imports.Contains(declaration.Library + "!" + declaration.Name))
                    throw new NotSupportedException("Original pre-entry child lacks its exact callable provider: " + declaration.Library + "!" + declaration.Name);
            }
            else if (instruction.Indirect)
                throw new NotSupportedException("Original constructor requires an actual reached indirect callback owner.");
            if (instruction.Call)
            {
                if (instruction.Target is { } child && !body.Children.Contains(child))
                    throw new NotSupportedException("Original constructor contains an unqualified direct child.");
                pending.Push(instruction.Next); continue;
            }
            if (instruction.Return || instruction.Import is not null) continue;
            if (instruction.Target is { } target)
            {
                bool? selected = null;
                if (body.Branches is not null && body.Branches.TryGetValue(at, out var take)) selected = take;
                if (!instruction.Conditional || selected != false)
                {
                    if (target >= body.Start && target < end) pending.Push(target);
                    else if (!body.Children.Contains(target)) throw new NotSupportedException("Original constructor tail leaves its selected source owner.");
                }
                if (instruction.Conditional && selected != true) pending.Push(instruction.Next);
            }
            else pending.Push(instruction.Next);
        }
    }
    private static Instruction Decode(byte[] bytes, uint basis, uint at)
    {
        var start = checked((int)(at - basis)); var p = start; var thread = false; var narrow = false;
        byte Byte() => p < bytes.Length ? bytes[p++] : throw new InvalidDataException("Original constructor instruction is truncated.");
        uint Raw(int count)
        {
            if (count > bytes.Length - p) throw new InvalidDataException("Original constructor operand is truncated.");
            var value = count switch
            {
                1 => bytes[p],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p)),
                4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p)),
                _ => throw new InvalidDataException("Original constructor operand width is unknown.")
            };
            p += count; return value;
        }
        var opcode = Byte(); var prefixes = 0;
        while (opcode is 0x64 or 0x66 or 0xf0 or 0xf2 or 0xf3)
        { if (++prefixes > 4) throw new NotSupportedException("Original constructor prefix sequence is unowned."); thread |= opcode == 0x64; narrow |= opcode == 0x66; opcode = Byte(); }
        uint? target = null, imported = null, cell = null; var conditional = false; var returns = false;
        var call = false; var indirect = false; var write = false; var stack = false; var modrm = false; var immediate = 0; var two = -1;
        if (opcode == 0x0f)
        {
            two = Byte();
            if (two is >= 0x80 and <= 0x8f) { var delta = unchecked((int)Raw(4)); target = Relative(delta); conditional = true; }
            else if (two == 0xa2) { }
            else if (two == 0x01) { if (Byte() != 0xd0) Unknown(); }
            else if (two is >= 0x40 and <= 0x4f or >= 0x90 and <= 0x9f or 0xb1 or 0xb6 or 0xb7 or 0xbe or 0xbf or 0x1f or 0x10 or 0x11 or 0x12 or 0x13 or 0x16 or 0x17 or 0x28 or 0x29 or 0x57)
            { modrm = true; write = two is >= 0x90 and <= 0x9f or 0xb1 or 0x11 or 0x13 or 0x17 or 0x29; }
            else Unknown();
        }
        else if (opcode is 0xe8 or 0xe9)
        { var delta = unchecked((int)Raw(4)); target = Relative(delta); call = opcode == 0xe8; }
        else if (opcode is 0xeb or >= 0x70 and <= 0x7f)
        { var delta = unchecked((sbyte)Raw(1)); target = Relative(delta); conditional = opcode != 0xeb; }
        else if (opcode is 0xc3 or 0xc2) { returns = true; if (opcode == 0xc2) _ = Raw(2); }
        else if (opcode is >= 0x40 and <= 0x5f or 0x90 or 0xc9 or 0xcc or 0x98 or 0x99) { returns = opcode == 0xcc; }
        else if (opcode is >= 0xb8 and <= 0xbf or 0x68) _ = Raw(narrow ? 2 : 4);
        else if (opcode is 0x6a or 0xb0 or 0xb3) _ = Raw(1);
        else if (opcode is 0xa1 or 0xa3) { cell = Raw(4); write = opcode == 0xa3; }
        else if (opcode is 0x04 or 0x0c or 0x24 or 0x2c or 0x34 or 0x3c or 0xa8) _ = Raw(1);
        else if (opcode is 0x05 or 0x0d or 0x25 or 0x2d or 0x35 or 0x3d or 0xa9) _ = Raw(narrow ? 2 : 4);
        else if (opcode is <= 0x3b && opcode % 8 <= 3 || opcode is 0x63 or 0x80 or 0x81 or 0x83 or >= 0x84 and <= 0x8f or 0xc0 or 0xc1 or 0xc6 or 0xc7 or >= 0xd0 and <= 0xd3 or 0xf6 or 0xf7 or 0xfe or 0xff)
        {
            modrm = true;
            immediate = opcode switch { 0x80 or 0x83 or 0xc0 or 0xc1 or 0xc6 => 1, 0x81 or 0xc7 => narrow ? 2 : 4, _ => 0 };
            write = opcode is <= 0x33 && opcode % 8 <= 1 || opcode is 0x80 or 0x81 or 0x83 or 0x86 or 0x87 or 0x88 or 0x89 or 0x8f or 0xc0 or 0xc1 or 0xc6 or 0xc7 or >= 0xd0 and <= 0xd3 or 0xf6 or 0xf7 or 0xfe or 0xff;
        }
        else Unknown();
        if (modrm)
        {
            var operand = Byte(); var mode = operand >> 6; var group = operand >> 3 & 7; var rm = operand & 7; var baseRegister = rm;
            if (mode != 3 && rm == 4)
            {
                var sib = Byte(); baseRegister = sib & 7;
                if ((sib >> 3 & 7) != 4) baseRegister = -1;
                if (mode == 0 && (sib & 7) == 5) { _ = Raw(4); baseRegister = -1; }
            }
            else if (mode == 0 && rm == 5) { cell = Raw(4); baseRegister = -1; }
            if (mode == 1) _ = Raw(1); else if (mode == 2) _ = Raw(4);
            stack = baseRegister is 4 or 5;
            if (opcode is 0x80 or 0x81 or 0x83 && group == 7) write = false;
            if (opcode is 0xf6 or 0xf7)
            { if (group == 0) { immediate = opcode == 0xf6 ? 1 : narrow ? 2 : 4; write = false; } else write = group is 2 or 3; }
            if (opcode == 0xff)
            {
                write = group is 0 or 1;
                if (group is 2 or 4) { call = group == 2; indirect = true; if (cell is not null) { imported = cell; indirect = false; } }
                else if (group is not (0 or 1 or 6)) Unknown();
            }
            if (mode == 3) { write = false; cell = null; stack = false; }
            if (immediate != 0) _ = Raw(immediate);
        }
        if (p - start is < 1 or > 15) Unknown();
        return new(checked(basis + (uint)p), target, conditional, returns, imported, call, indirect, write, stack, thread, cell);
        uint Relative(int delta)
        {
            var value = checked((long)basis + p + delta);
            return value is >= 0 and <= uint.MaxValue ? (uint)value : throw new InvalidDataException("Original constructor branch wraps.");
        }
        static void Unknown() => throw new NotSupportedException("Original constructor has an unowned x86 instruction/operand shape.");
    }
}
