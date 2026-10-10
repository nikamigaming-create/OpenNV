using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMainUtilityStartupArgumentDeclaration(string Option,
    int ReceiverByteField, string DeclarationSha256);
internal sealed record FalloutMainUtilityPureByteSetter(int ReceiverByteField, int Bytes);

internal static partial class FalloutExecutableStringTable
{
    // Discovery patterns do not establish a selected effect. First require the
    // original option comparison and ordered Win32 argument-import relationship;
    // only that producer may require a complete setter contract. Unrelated
    // methods elsewhere in the same code section cannot abort that discovery.
    // This transport seam parses independently authored instructions only; the
    // installed caller separately checks the complete selected executable hash.
    internal static FalloutMainUtilityStartupArgumentDeclaration ReadMainUtilityStartupArgumentDeclaration(
        byte[] code, Func<uint, string?> wideToken, Func<uint, string?> importName)
    {
        var candidates = new List<FalloutMainUtilityStartupArgumentDeclaration>();
        for (var at = 0; at <= code.Length - 14; at++)
        {
            if (code[at] != 0x6a || code[at + 1] != 0 || code[at + 2] != 0xe8 ||
                code[at + 7] != 0x8b || code[at + 8] != 0xc8 || code[at + 9] != 0xe8) continue;
            for (var push = Math.Max(0, at - 96); push < at; push++)
            {
                if (code[push] != 0x68) continue;
                var cursor = push + 5;
                // The whole suffix is the same frame-owned UTF16 argument passed
                // into the comparison, not a literal match elsewhere in the image.
                if (cursor + 6 <= at && code[cursor] == 0x8b && code[cursor + 1] == 0x8d)
                {
                    if (unchecked((int)U32(code, cursor + 2)) >= 0) continue;
                    cursor += 6;
                }
                else if (cursor + 3 <= at && code[cursor] == 0x8b && code[cursor + 1] == 0x4d)
                {
                    if (unchecked((sbyte)code[cursor + 2]) >= 0) continue;
                    cursor += 3;
                }
                else continue;
                if (cursor + 13 > at || code[cursor++] != 0x51 || code[cursor] != 0xe8) continue;
                _ = RelativeStatisticCall(code, cursor);
                cursor += 5;
                if (!code.AsSpan(cursor, 5).SequenceEqual(new byte[] { 0x83, 0xc4, 8, 0x85, 0xc0 })) continue;
                cursor += 5;
                // The failed comparison must skip exactly the getter/thiscall
                // store. A broader branch has independently unowned effects.
                if (cursor + 2 != at || code[cursor] != 0x75 || code[cursor + 1] != 14) continue;
                var option = wideToken(U32(code, push + 1));
                if (string.IsNullOrEmpty(option)) continue;
                if (option.Length > 128 || option.Any(character => character is < (char)0x21 or > (char)0x7e))
                    throw new NotSupportedException("Source utility option has an unowned UTF16 comparison declaration.");
                var frame = MainUtilityFrameEntry(code, at, 4096);
                if (!MainUtilityStartupArgumentImports(code, frame, push, importName)) continue;

                // These linked calls are checked only after their original
                // argument producer is identified. Calls/branches/extra writes
                // in the selected setter refuse; they are never no-op effects.
                _ = RelativeStatisticCall(code, at + 2);
                var entry = RelativeStatisticCall(code, at + 9);
                var setter = ReadMainUtilityPureByteSetter(code.AsSpan(entry));
                var digestInput = new byte[checked(at + 14 - push + setter.Bytes)];
                code.AsSpan(push, at + 14 - push).CopyTo(digestInput);
                code.AsSpan(entry, setter.Bytes).CopyTo(digestInput.AsSpan(at + 14 - push));
                var digest = Convert.ToHexString(SHA256.HashData(digestInput)).ToLowerInvariant();
                candidates.Add(new(option, setter.ReceiverByteField, digest));
            }
        }
        if (candidates.Count != 1)
            throw new NotSupportedException("Original source startup has no unique complete argument-to-independent-byte producer.");
        return candidates[0];
    }

    private static bool MainUtilityStartupArgumentImports(byte[] code, int frame, int before,
        Func<uint, string?> importName)
    {
        for (var at = frame; at <= before - 13; at++)
        {
            // Actual stdcall GetCommandLineW result EAX is the first argument
            // of CommandLineToArgvW. The retained caller already pushed its
            // independent frame-owned count output before GetCommandLineW.
            if (code[at] != 0xff || code[at + 1] != 0x15 || code[at + 6] != 0x50 ||
                code[at + 7] != 0xff || code[at + 8] != 0x15) continue;
            if (importName(U32(code, at + 2)) == "GetCommandLineW" &&
                importName(U32(code, at + 9)) == "CommandLineToArgvW") return true;
        }
        return false;
    }

    internal static FalloutMainUtilityPureByteSetter ReadMainUtilityPureByteSetter(ReadOnlySpan<byte> input)
    {
        // A complete bounded x86 thiscall transport: EBP frame, one reserved
        // receiver local, reload that same receiver, byte argument at EBP+8,
        // one byte store, canonical frame return and four-byte callee cleanup.
        // It admits no arbitrary instruction decoder/executable behavior.
        var body = input[..Math.Min(input.Length, 64)];
        var cursor = 0;
        RequireMainUtilityBytes(body, ref cursor, [0x55, 0x8b, 0xec]);
        if (cursor < body.Length && body[cursor] == 0x51) cursor++;
        else RequireMainUtilityBytes(body, ref cursor, [0x83, 0xec, 4]);
        RequireMainUtilityBytes(body, ref cursor, [0x89, 0x4d]);
        var slot = ReadMainUtilityOperand(body, ref cursor);
        if (unchecked((sbyte)slot) != -4)
            throw new NotSupportedException("Source byte setter does not own its reserved receiver local.");
        RequireMainUtilityBytes(body, ref cursor, [0x8b]);
        var receiverLoad = ReadMainUtilityOperand(body, ref cursor);
        if ((receiverLoad & 0xc7) != 0x45 || ReadMainUtilityOperand(body, ref cursor) != slot)
            throw new NotSupportedException("Source byte setter changed its saved this receiver.");
        var receiver = receiverLoad >> 3 & 7;
        if (receiver is 4 or 5)
            throw new NotSupportedException("Source byte setter aliases its receiver with the active frame.");
        RequireMainUtilityBytes(body, ref cursor, [0x8a]);
        var argumentLoad = ReadMainUtilityOperand(body, ref cursor);
        if ((argumentLoad & 0xc7) != 0x45 || ReadMainUtilityOperand(body, ref cursor) != 8)
            throw new NotSupportedException("Source byte setter has no exact first byte argument.");
        var argument = argumentLoad >> 3 & 7;
        if (argument > 3 || argument == receiver)
            throw new NotSupportedException("Source byte setter changes its receiver or has an unowned high-byte transport.");
        RequireMainUtilityBytes(body, ref cursor, [0x88]);
        var store = ReadMainUtilityOperand(body, ref cursor);
        if ((store & 0x3f) != (argument << 3 | receiver) || store >> 6 is not (1 or 2))
            throw new NotSupportedException("Source byte setter changed its sole receiver/argument store.");
        int field;
        if (store >> 6 == 1) field = unchecked((sbyte)ReadMainUtilityOperand(body, ref cursor));
        else
        {
            field = unchecked((int)U32(body, cursor));
            cursor = checked(cursor + 4);
        }
        if (field < 0)
            throw new NotSupportedException("Source byte setter writes outside its receiver-owned extent.");
        RequireMainUtilityBytes(body, ref cursor, [0x8b, 0xe5, 0x5d, 0xc2, 4, 0]);
        return new(field, cursor);
    }

    private static byte ReadMainUtilityOperand(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        if ((uint)cursor >= bytes.Length)
            throw new InvalidDataException("Source byte setter operand exceeds its complete method extent.");
        return bytes[cursor++];
    }
    private static void RequireMainUtilityBytes(ReadOnlySpan<byte> bytes, ref int cursor, ReadOnlySpan<byte> expected)
    {
        if (cursor < 0 || cursor > bytes.Length - expected.Length)
            throw new InvalidDataException("Source byte setter instruction exceeds its complete method extent.");
        if (!bytes.Slice(cursor, expected.Length).SequenceEqual(expected))
            throw new NotSupportedException("Selected source byte setter has an unowned instruction/effect envelope.");
        cursor = checked(cursor + expected.Length);
    }
}
