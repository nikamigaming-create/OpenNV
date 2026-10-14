using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal enum FalloutSpecialBookPageDirection : uint { Forward = 1, Backward = 2 }
internal sealed record FalloutSourceCStringInputSource(string Family, string RuntimePath, string RuntimeSha256,
    uint Instruction, uint Pointer, string Value, ImmutableArray<byte> Preimage)
{
    internal uint Operand => checked(Instruction + 1);
}
internal sealed record FalloutSpecialBookPageSoundOperand(FalloutSpecialBookPageDirection Direction,
    uint Instruction, uint Pointer, string EditorId, uint Flags);
internal sealed record FalloutSpecialBookPageSoundSelection(uint Click, uint Turn, uint PageField,
    int MaximumPage, uint DirectionByte, ImmutableArray<uint> SoundChildren,
    FalloutSpecialBookPageSoundOperand Forward, FalloutSpecialBookPageSoundOperand Backward);
internal sealed class FalloutSpecialBookPageSoundsSource
{
    internal string RuntimePath { get; }
    internal string RuntimeSha256 { get; }
    internal string Family => "PE32/SPECIALBook/Boolean-page-direction/independent-PUSH-SOUN-inputs";
    internal uint ClassTable { get; }
    internal FalloutSpecialBookPageSoundSelection Selection { get; }
    internal FalloutSpecialBookPageSoundOperand Forward => Selection.Forward;
    internal FalloutSpecialBookPageSoundOperand Backward => Selection.Backward;
    internal FalloutSpecialBookPageSoundOperand For(FalloutSpecialBookPageDirection direction) => direction switch
    {
        FalloutSpecialBookPageDirection.Forward => Forward,
        FalloutSpecialBookPageDirection.Backward => Backward,
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };
    internal FalloutSpecialBookPageSoundsSource(string path, string hash, uint table, FalloutSpecialBookPageSoundSelection selection)
    {
        RuntimePath = path; RuntimeSha256 = hash; ClassTable = table; Selection = selection;
        if (selection.Forward.Direction != FalloutSpecialBookPageDirection.Forward || selection.Backward.Direction != FalloutSpecialBookPageDirection.Backward ||
            selection.Forward.Instruction == selection.Backward.Instruction || selection.MaximumPage <= 0 || selection.SoundChildren.Length != 7)
            throw new InvalidDataException("Book page sound inputs lost their independent direction/receiver/child associations.");
        _ = Input(FalloutSpecialBookPageDirection.Forward); _ = Input(FalloutSpecialBookPageDirection.Backward);
    }
    internal static FalloutSpecialBookPageSoundsSource Read(string executable) => FalloutExecutableStringTable.ReadSpecialBookPageSounds(executable);
    internal FalloutSourceCStringInputSource Input(FalloutSpecialBookPageDirection direction)
    {
        var slot = For(direction); FalloutExecutableStringTable.RequireSoundEditorId(slot.EditorId);
        var bytes = new byte[5]; bytes[0] = 0x68; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), slot.Pointer);
        if (slot.Pointer == 0 || slot.Flags == 0) throw new InvalidDataException("Book page sound has no original pointer/flags.");
        return new(Family + "/" + direction, RuntimePath, RuntimeSha256, slot.Instruction, slot.Pointer, slot.EditorId, [.. bytes]);
    }
    internal void CheckSame(FalloutSpecialBookPageSoundsSource current)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(RuntimePath, current.RuntimePath) || RuntimeSha256 != current.RuntimeSha256 ||
            ClassTable != current.ClassTable || Selection.Click != current.Selection.Click || Selection.Turn != current.Selection.Turn ||
            Selection.PageField != current.Selection.PageField || Selection.MaximumPage != current.Selection.MaximumPage ||
            Selection.DirectionByte != current.Selection.DirectionByte || !Selection.SoundChildren.AsSpan().SequenceEqual(current.Selection.SoundChildren.AsSpan()) ||
            Forward != current.Forward || Backward != current.Backward)
            throw new InvalidDataException("Book page sound source changed its actual class/input/page/audio children.");
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutSpecialBookPageSoundsSource ReadSpecialBookPageSounds(string executable)
    {
        var path = Path.GetFullPath(executable); var bytes = File.ReadAllBytes(path); var (code, image) = Load(bytes);
        var tables = image.SourceClassTables(".?AVSPECIALBookMenu@@", 15).ToArray();
        if (tables.Length != 1) throw new NotSupportedException("Page sounds require the actual unique SPECIAL book class/input caller.");
        var click = U32(image.Read(checked(tables[0] + 3 * 4), 4), 0);
        var selection = ReadSpecialBookPageSoundSelection(code, image.CodeBase, click, image.Read, image.Literal);
        return new(path, Convert.ToHexString(SHA256.HashData(bytes)), tables[0], selection);
    }
    internal static void RequireSoundEditorId(string value)
    {
        if (value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidDataException("Sound input is not an original ASCII editor identifier.");
    }
    internal static string ReadSoundEditorIdBytes(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        if (end <= 0 || bytes[..end].ContainsAnyExceptInRange((byte)32, (byte)126))
            throw new InvalidDataException("Sound input has no complete ASCII CString.");
        var value = Encoding.ASCII.GetString(bytes[..end]); RequireSoundEditorId(value); return value;
    }

    // Discovery starts at the used class click dispatcher. Equal text never
    // selects a slot: input branches, the same receiver page store, and the
    // original ordered sound children select each pointer independently.
    internal static FalloutSpecialBookPageSoundSelection ReadSpecialBookPageSoundSelection(byte[] code, uint codeBase,
        uint click, Func<uint, int, byte[]> read, Func<uint, string?> literal)
    {
        var dispatcher = InterfaceSoundInstructions(code, codeBase, click);
        var jumps = dispatcher.Where(row => row.Opcode == 0xff && row.ModRm == 0x24 && row.Sib >> 6 == 2 && (row.Sib & 7) == 5).ToArray();
        if (jumps.Length != 1 || !dispatcher.Any(row => row.Opcode is 0x81 or 0x83 && (row.ModRm >> 3 & 7) == 7 && row.Immediate == 7))
            throw new NotSupportedException("Book input has no admitted eight-control source dispatcher.");
        var clickReceiver = dispatcher.SingleOrDefault(row => row.At < jumps[0].At && row.Opcode == 0x89 &&
            (row.ModRm >> 3 & 7) == 1 && row.ModRm >> 6 is 1 or 2 && (row.ModRm & 7) == 5 && row.Displacement < 0)
            ?? throw new NotSupportedException("Book click dispatcher has no unique actual saved receiver.");
        var targets = read(unchecked((uint)jumps[0].Displacement), 8 * 4);
        var first = U32(targets, 0); var second = U32(targets, 4);
        if (first != second) throw new NotSupportedException("Book forward/backward controls do not share the actual source direction case.");
        var input = InterfaceSoundInstructions(code, codeBase, first);
        var calls = input.Where(row => row.Opcode == 0xe8 && row.Previous is { Opcode: 0x8b, ModRm: 0x8d } alias &&
            alias.Previous is { Opcode: 0x6a, Immediate: 0 or 1 } push && push.Next == alias.At && alias.Next == row.At).ToArray();
        if (calls.Length != 2 || calls.Select(row => row.Previous!.Previous!.Immediate).ToHashSet().Count != 2 ||
            InterfaceRelativeTarget(calls[0], codeBase) != InterfaceRelativeTarget(calls[1], codeBase) ||
            calls[0].Previous!.Displacement != calls[1].Previous!.Displacement || calls[0].Previous!.Displacement != clickReceiver.Displacement)
            throw new NotSupportedException("Book source controls lost their Boolean direction and actual same-receiver call.");
        var turn = InterfaceRelativeTarget(calls[0], codeBase); var rows = InterfaceSoundInstructions(code, codeBase, turn);
        var argument = rows.Where(row => row.Opcode == 0x0f && row.Escaped == 0xb6 && row.ModRm == 0x45 && row.Displacement == 8).ToArray();
        if (argument.Length != 1) throw new NotSupportedException("Book page method has no unique actual byte direction argument.");
        var branch = rows.SingleOrDefault(row => row.At == argument[0].Next + 2);
        if (branch is null || branch.Opcode != 0x0f || branch.Escaped != 0x84 ||
            !code.AsSpan(argument[0].Next, 2).SequenceEqual(new byte[] { 0x85, 0xc0 }))
            throw new NotSupportedException("Book page Boolean direction split changed its source predicate.");
        var back = checked((int)(InterfaceRelativeTarget(branch, codeBase) - codeBase));
        var receiver = rows.LastOrDefault(row => row.At < argument[0].At && row.Opcode == 0x89 && row.ModRm == 0x4d && row.Displacement < 0)
            ?? throw new NotSupportedException("Book page method has no original saved receiver.");
        var forwardRows = rows.Where(row => row.At >= branch.Next && row.At < back).ToArray();
        var forward = Arm(forwardRows, FalloutSpecialBookPageDirection.Forward, 0x7d, 1);
        var backwardRows = rows.Where(row => row.At >= back).ToArray();
        var backward = Arm(backwardRows, FalloutSpecialBookPageDirection.Backward, 0x7c, 0);
        if (forward.Field != backward.Field || forward.Maximum <= 0 || backward.Maximum != 1 || forward.Direction != backward.Direction ||
            !forward.Children.AsSpan().SequenceEqual(backward.Children.AsSpan()))
            throw new NotSupportedException("Book directions changed their same-page field, bounds, direction byte or ordered audio child owners.");
        return new(click, turn, forward.Field, forward.Maximum, forward.Direction, forward.Children, forward.Operand, backward.Operand);

        (uint Field, int Maximum, uint Direction, ImmutableArray<uint> Children, FalloutSpecialBookPageSoundOperand Operand)
            Arm(InterfaceInstruction[] arm, FalloutSpecialBookPageDirection direction, byte outsideOpcode, int directionValue)
        {
            var directionStore = arm.FirstOrDefault(row => row.Opcode == 0xc6 && row.ModRm == 5 && row.Immediate == directionValue)
                ?? throw new NotSupportedException("Book direction branch lost its independent source byte store.");
            var compare = arm.FirstOrDefault(row => row.Opcode == 0x83 && (row.ModRm >> 3 & 7) == 7 && row.ModRm >> 6 == 1 &&
                row.Previous is { Opcode: 0x8b } prior && prior.ModRm >> 6 == 1 && (prior.ModRm & 7) == 5 && prior.Displacement == receiver.Displacement &&
                (prior.ModRm >> 3 & 7) == (row.ModRm & 7)) ?? throw new NotSupportedException("Book branch has no same-receiver current-page bound.");
            var outside = arm.SingleOrDefault(row => row.At == compare.Next);
            if (outside is null || outside.Opcode != outsideOpcode) throw new NotSupportedException("Book page bound changed its original signed predicate.");
            var end = checked((int)(InterfaceRelativeTarget(outside, codeBase) - codeBase));
            var body = arm.Where(row => row.At > outside.At && row.At < end).ToArray();
            var pageRead = body.FirstOrDefault(row => row.Opcode == 0x8b && row.ModRm >> 6 == 1 && row.Displacement == compare.Displacement &&
                row.Previous is { Opcode: 0x8b } prior && (prior.ModRm & 7) == 5 && prior.Displacement == receiver.Displacement &&
                (prior.ModRm >> 3 & 7) == (row.ModRm & 7)) ?? throw new NotSupportedException("Book page arm omitted its actual current-page read.");
            var arithmetic = body.SingleOrDefault(row => row.At == pageRead.Next);
            var operation = direction == FalloutSpecialBookPageDirection.Forward ? 0 : 5;
            if (arithmetic is null || arithmetic.Opcode != 0x83 || arithmetic.ModRm >> 6 != 3 ||
                (arithmetic.ModRm >> 3 & 7) != operation || (arithmetic.ModRm & 7) != (pageRead.ModRm >> 3 & 7) || arithmetic.Immediate != 1)
                throw new NotSupportedException("Book page arm changed its actual increment/decrement.");
            var pageStore = body.FirstOrDefault(row => row.At > arithmetic.At && row.Opcode == 0x89 && row.ModRm >> 6 == 1 &&
                row.Displacement == compare.Displacement && (row.ModRm >> 3 & 7) == (pageRead.ModRm >> 3 & 7) &&
                row.Previous is { Opcode: 0x8b } prior && (prior.ModRm & 7) == 5 && prior.Displacement == receiver.Displacement &&
                (prior.ModRm >> 3 & 7) == (row.ModRm & 7)) ?? throw new NotSupportedException("Book page arm omitted its original page store before sound.");
            var names = body.Where(row => row.Opcode == 0x68 && row.Previous is { Opcode: 0x68 } flags && flags.Next == row.At &&
                literal(unchecked((uint)row.Immediate!.Value)) is { Length: > 0 }).ToArray();
            if (names.Length != 1 || names[0].At <= pageStore.At) throw new NotSupportedException("Book page arm has no unique post-store named sound input.");
            var name = names[0]; var next = body.SingleOrDefault(row => row.At == name.Next);
            var resultPointer = next is null ? null : body.SingleOrDefault(row => row.At == next.Next);
            var prepare = resultPointer is null ? null : body.SingleOrDefault(row => row.At == resultPointer.Next);
            if (next is null || next.Opcode != 0x8d || next.ModRm >> 6 != 1 || (next.ModRm & 7) != 5 || resultPointer is null ||
                resultPointer.Opcode != 0x50 + (next.ModRm >> 3 & 7) || prepare is null || prepare.Opcode != 0xe8)
                throw new NotSupportedException("Book sound input lost its actual result pointer and source prepare call.");
            var children = body.Where(row => row.Opcode == 0xe8).Select(row => InterfaceRelativeTarget(row, codeBase)).ToImmutableArray();
            if (children.Length != 7 || children.Any(child => child < codeBase || (ulong)child >= (ulong)codeBase + (uint)code.Length) ||
                children[1] != InterfaceRelativeTarget(prepare, codeBase) || children[4] != children[6] ||
                !body.Any(row => row.Opcode == 0x6a && row.Immediate == 0 && row.At > prepare.At))
                throw new NotSupportedException("Book sound input changed its initialization, conversion, copy, playback or temporary cleanup chain.");
            var editorId = literal(unchecked((uint)name.Immediate!.Value))!; RequireSoundEditorId(editorId);
            return (checked((uint)compare.Displacement), compare.Immediate!.Value, unchecked((uint)directionStore.Displacement), children,
                new(direction, checked(codeBase + (uint)name.At), unchecked((uint)name.Immediate.Value), editorId, unchecked((uint)name.Previous!.Immediate!.Value)));
        }
    }
}
