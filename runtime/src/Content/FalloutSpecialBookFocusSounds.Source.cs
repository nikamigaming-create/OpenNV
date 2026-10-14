using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal enum FalloutSpecialBookSoundInput : uint { PageForward = 1, PageBackward = 2, FocusPrevious = 3, FocusNext = 4 }
internal enum FalloutSpecialBookFocusDirection : uint { Previous = 3, Next = 4 }
internal sealed record FalloutSpecialBookFocusSoundRead(FalloutSpecialBookFocusDirection Direction, string EditorId, uint Pointer, uint Flags);
internal sealed record FalloutSpecialBookFocusSoundOperand(FalloutSpecialBookFocusDirection Direction,
    uint Instruction, uint Pointer, string EditorId, uint Flags);
internal sealed record FalloutSpecialBookFocusSoundSelection(uint Click, uint PageField, int ReviewPage,
    uint IndexField, int MinimumIndex, int MaximumIndex, uint Refresh, ImmutableArray<uint> SoundChildren,
    FalloutSpecialBookFocusSoundOperand Previous, FalloutSpecialBookFocusSoundOperand Next);

internal sealed class FalloutSpecialBookFocusSoundsSource
{
    internal string RuntimePath { get; }
    internal string RuntimeSha256 { get; }
    internal uint ClassTable { get; }
    internal string Family => "PE32/SPECIALBook/review-index/independent-PUSH-focus-SOUN-inputs";
    internal FalloutSpecialBookFocusSoundSelection Selection { get; }
    internal FalloutSpecialBookFocusSoundsSource(string path, string hash, uint table, FalloutSpecialBookFocusSoundSelection selection)
    {
        RuntimePath = path; RuntimeSha256 = hash; ClassTable = table; Selection = selection;
        if (selection.Previous.Direction != FalloutSpecialBookFocusDirection.Previous || selection.Next.Direction != FalloutSpecialBookFocusDirection.Next ||
            selection.Previous.Instruction == selection.Next.Instruction || selection.MinimumIndex >= selection.MaximumIndex || selection.SoundChildren.Length != 4)
            throw new InvalidDataException("Book focus source lost its independent index-direction inputs.");
        _ = Input(FalloutSpecialBookFocusDirection.Previous); _ = Input(FalloutSpecialBookFocusDirection.Next);
    }
    internal static FalloutSpecialBookFocusSoundsSource Read(string executable) => FalloutExecutableStringTable.ReadSpecialBookFocusSounds(executable);
    internal FalloutSpecialBookFocusSoundOperand For(FalloutSpecialBookFocusDirection direction) => direction switch
    {
        FalloutSpecialBookFocusDirection.Previous => Selection.Previous,
        FalloutSpecialBookFocusDirection.Next => Selection.Next,
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };
    internal FalloutSourceCStringInputSource Input(FalloutSpecialBookFocusDirection direction)
    {
        var operand = For(direction); FalloutExecutableStringTable.RequireSoundEditorId(operand.EditorId);
        if (operand.Pointer == 0 || operand.Flags == 0) throw new InvalidDataException("Book focus source omitted its original pointer/flags.");
        var preimage = new byte[5]; preimage[0] = 0x68; BinaryPrimitives.WriteUInt32LittleEndian(preimage.AsSpan(1), operand.Pointer);
        return new(Family + "/" + direction, RuntimePath, RuntimeSha256, operand.Instruction, operand.Pointer, operand.EditorId, [.. preimage]);
    }
    internal void CheckPageOwner(FalloutSpecialBookPageSoundsSource page)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(RuntimePath, page.RuntimePath) || RuntimeSha256 != page.RuntimeSha256 || ClassTable != page.ClassTable ||
            Selection.Click != page.Selection.Click || Selection.PageField != page.Selection.PageField ||
            !Selection.SoundChildren.AsSpan().SequenceEqual(new uint[] { page.Selection.SoundChildren[1], page.Selection.SoundChildren[2],
                page.Selection.SoundChildren[5], page.Selection.SoundChildren[4] }))
            throw new InvalidDataException("Book focus inputs changed their actual class/page/audio owners.");
    }
    internal void CheckSame(FalloutSpecialBookFocusSoundsSource other)
    {
        var left = Selection; var right = other.Selection;
        if (!StringComparer.OrdinalIgnoreCase.Equals(RuntimePath, other.RuntimePath) || RuntimeSha256 != other.RuntimeSha256 || ClassTable != other.ClassTable ||
            left.Click != right.Click || left.PageField != right.PageField || left.ReviewPage != right.ReviewPage || left.IndexField != right.IndexField ||
            left.MinimumIndex != right.MinimumIndex || left.MaximumIndex != right.MaximumIndex || left.Refresh != right.Refresh ||
            !left.SoundChildren.AsSpan().SequenceEqual(right.SoundChildren.AsSpan()) || left.Previous != right.Previous || left.Next != right.Next)
            throw new InvalidDataException("Book focus source changed its selected class/index/bounds/input/children.");
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutSpecialBookFocusSoundsSource ReadSpecialBookFocusSounds(string executable)
    {
        var path = Path.GetFullPath(executable); var bytes = File.ReadAllBytes(path); var (code, image) = Load(bytes);
        var tables = image.SourceClassTables(".?AVSPECIALBookMenu@@", 15).ToArray();
        if (tables.Length != 1) throw new NotSupportedException("Book focus requires its actual unique class click dispatcher.");
        var click = U32(image.Read(tables[0] + 3 * 4, 4), 0);
        var selection = ReadSpecialBookFocusSoundSelection(code, image.CodeBase, click, image.Read, image.Literal);
        return new(path, Convert.ToHexString(SHA256.HashData(bytes)), tables[0], selection);
    }
    internal static FalloutSpecialBookFocusSoundSelection ReadSpecialBookFocusSoundSelection(byte[] code, uint codeBase,
        uint click, Func<uint, int, byte[]> read, Func<uint, string?> literal)
    {
        var dispatcher = InterfaceSoundInstructions(code, codeBase, click);
        var jump = dispatcher.SingleOrDefault(row => row.Opcode == 0xff && row.ModRm == 0x24 && row.Sib >> 6 == 2 && (row.Sib & 7) == 5)
            ?? throw new NotSupportedException("Book focus has no actual indexed click dispatcher.");
        if (!dispatcher.Any(row => row.Opcode is 0x81 or 0x83 && (row.ModRm >> 3 & 7) == 7 && row.Immediate == 7))
            throw new NotSupportedException("Book focus click control bound changed its selected source.");
        var receiver = dispatcher.SingleOrDefault(row => row.At < jump.At && row.Opcode == 0x89 && (row.ModRm >> 3 & 7) == 1 &&
            row.ModRm >> 6 is 1 or 2 && (row.ModRm & 7) == 5 && row.Displacement < 0)
            ?? throw new NotSupportedException("Book focus has no original click receiver alias.");
        var table = read(unchecked((uint)jump.Displacement), 32);
        var previous = Arm(U32(table, 6 * 4), FalloutSpecialBookFocusDirection.Previous, 0x7e, 5);
        var next = Arm(U32(table, 7 * 4), FalloutSpecialBookFocusDirection.Next, 0x7d, 0);
        if (previous.PageField != next.PageField || previous.Page != next.Page || previous.IndexField != next.IndexField ||
            previous.Refresh != next.Refresh || previous.Bound >= next.Bound || !previous.Children.AsSpan().SequenceEqual(next.Children.AsSpan()))
            throw new NotSupportedException("Book focus controls do not share their actual page/index/refresh/audio owners.");
        return new(click, previous.PageField, previous.Page, previous.IndexField, previous.Bound, next.Bound, previous.Refresh,
            previous.Children, previous.Operand, next.Operand);

        (uint PageField, int Page, uint IndexField, int Bound, uint Refresh, ImmutableArray<uint> Children, FalloutSpecialBookFocusSoundOperand Operand)
            Arm(uint entry, FalloutSpecialBookFocusDirection direction, byte boundPredicate, int arithmetic)
        {
            var rows = InterfaceSoundInstructions(code, codeBase, entry).ToArray();
            bool Receiver(InterfaceInstruction? alias, int register) => alias is { Opcode: 0x8b } && alias.ModRm >> 6 is 1 or 2 &&
                (alias.ModRm & 7) == 5 && alias.Displacement == receiver.Displacement && (alias.ModRm >> 3 & 7) == register;
            var comparisons = rows.Where(row => row.Opcode == 0x83 && row.ModRm >> 6 == 1 && (row.ModRm >> 3 & 7) == 7 &&
                Receiver(row.Previous, row.ModRm & 7)).ToArray();
            if (comparisons.Length != 2) throw new NotSupportedException("Book focus requires distinct actual page and index comparisons.");
            var page = comparisons[0]; var index = comparisons[1];
            var pageBranch = rows.SingleOrDefault(row => row.At == page.Next); var indexBranch = rows.SingleOrDefault(row => row.At == index.Next);
            if (pageBranch is null || pageBranch.Opcode != 0x75 || indexBranch is null || indexBranch.Opcode != boundPredicate ||
                InterfaceRelativeTarget(pageBranch, codeBase) != InterfaceRelativeTarget(indexBranch, codeBase) || page.Displacement == index.Displacement)
                throw new NotSupportedException("Book focus changed its review-page or signed current-index predicates.");
            var joint = checked((int)(InterfaceRelativeTarget(pageBranch, codeBase) - codeBase));
            var body = rows.Where(row => row.At > indexBranch.At && row.At < joint).ToArray();
            var current = body.FirstOrDefault(row => row.Opcode == 0x8b && row.ModRm >> 6 == 1 && row.Displacement == index.Displacement &&
                Receiver(row.Previous, row.ModRm & 7)) ?? throw new NotSupportedException("Book focus omitted its actual current-index read.");
            var change = body.SingleOrDefault(row => row.At == current.Next);
            if (change is null || change.Opcode != 0x83 || change.ModRm >> 6 != 3 || (change.ModRm >> 3 & 7) != arithmetic ||
                (change.ModRm & 7) != (current.ModRm >> 3 & 7) || change.Immediate != 1)
                throw new NotSupportedException("Book focus changed its original index increment/decrement.");
            // The selected fall-through keeps the adjusted index live until
            // its immediate receiver reload/store. No intervening writer may
            // replace that value, change the index field or skip the store.
            var storeReceiver = body.SingleOrDefault(row => row.At == change.Next);
            var store = storeReceiver is null ? null : body.SingleOrDefault(row => row.At == storeReceiver.Next);
            if (current.Previous!.At != indexBranch.Next || store is null || store.Opcode != 0x89 ||
                store.ModRm >> 6 != 1 || store.Displacement != index.Displacement ||
                (store.ModRm >> 3 & 7) != (current.ModRm >> 3 & 7) || !Receiver(storeReceiver, store.ModRm & 7) ||
                (store.ModRm & 7) == (current.ModRm >> 3 & 7))
                throw new NotSupportedException("Book focus changed its contiguous current-index arithmetic/receiver/store owner.");
            var names = body.Where(row => row.Opcode == 0x68 && row.Previous is { Opcode: 0x68 } flags && flags.Next == row.At &&
                flags.Previous is { Opcode: 0x6a, Immediate: 0 } && literal(unchecked((uint)row.Immediate!.Value)) is { Length: > 0 }).ToArray();
            if (names.Length != 1 || names[0].Previous!.Previous!.At != store.Next)
                throw new NotSupportedException("Book focus lost its immediate post-index-store sound input.");
            var name = names[0]; var pointer = body.SingleOrDefault(row => row.At == name.Next);
            var push = pointer is null ? null : body.SingleOrDefault(row => row.At == pointer.Next);
            var prepare = push is null ? null : body.SingleOrDefault(row => row.At == push.Next);
            if (pointer is null || pointer.Opcode != 0x8d || (pointer.ModRm & 7) != 5 || pointer.ModRm >> 6 is not (1 or 2) ||
                push is null || push.Opcode != 0x50 + (pointer.ModRm >> 3 & 7) || prepare is null || prepare.Opcode != 0xe8)
                throw new NotSupportedException("Book focus sound lost its actual result pointer/prepare call.");
            var children = body.Where(row => row.Opcode == 0xe8).Select(row => InterfaceRelativeTarget(row, codeBase)).ToImmutableArray();
            if (children.Length != 4 || children[0] != InterfaceRelativeTarget(prepare, codeBase) ||
                children.Any(child => child < codeBase || (ulong)child >= (ulong)codeBase + (uint)code.Length))
                throw new NotSupportedException("Book focus changed its original prepare/convert/play/cleanup chain.");
            var refresh = rows.SingleOrDefault(row => row.At >= joint && row.Opcode == 0xe8 && Receiver(row.Previous, 1))
                ?? throw new NotSupportedException("Book focus omitted its same-receiver joined refresh call.");
            var value = literal(unchecked((uint)name.Immediate!.Value))!; RequireSoundEditorId(value);
            return (checked((uint)page.Displacement), page.Immediate!.Value, checked((uint)index.Displacement), index.Immediate!.Value,
                InterfaceRelativeTarget(refresh, codeBase), children, new(direction, checked(codeBase + (uint)name.At),
                    unchecked((uint)name.Immediate.Value), value, unchecked((uint)name.Previous!.Immediate!.Value)));
        }
    }
}
