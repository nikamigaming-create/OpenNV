using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutMainUtilityCommandSource ReadMainUtilityCommandSource(string executable, FalloutMainUtilitySource main)
    {
        var (code, image) = MainUtilityImage(executable, main);
        var start = image.ScriptCommandStart("AddAchievement", code, 1);
        var body = StatisticFunctionBody(code, start);
        var reads = new List<uint>();
        foreach (var at in MainUtilityInstructionOffsets(body))
        {
            if (at + 7 > body.Length || body[at] != 0x0f || body[at + 1] != 0xb6 || (body[at + 2] & 0xc7) != 5) continue;
            var address = U32(body, at + 3);
            if (!image.IsWritableExtent(address, 1)) throw new InvalidDataException("Original suppression operand has no loaded writable byte extent.");
            reads.Add(address);
        }
        if (reads.Count != 1) throw new NotSupportedException("Original command has no unique complete suppression-byte read.");
        // PE loading initializes the non-file-backed tail of an admitted
        // writable section to zero. This is a real source constructor value,
        // not absence inferred from an unavailable callback/read API.
        var initial = image.IsFileExtent(reads[0], 1) ? image.Read(reads[0], 1)[0] : (byte)0;
        return FalloutMainUtilityCommandSource.Read(main, initial,
            Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant());
    }

    internal static FalloutPlatformStartupArgumentSource ReadMainPlatformStartupArguments(string executable, FalloutMainUtilitySource main)
    {
        var (code, image) = MainUtilityImage(executable, main);
        var candidates = new List<(string Option, string Digest)>();
        for (var at = 0; at + 14 <= code.Length; at++)
        {
            if (code[at] != 0x6a || code[at + 1] != 0 || code[at + 2] != 0xe8 ||
                code[at + 7] != 0x8b || code[at + 8] != 0xc8 || code[at + 9] != 0xe8) continue;
            var setter = RelativeStatisticCall(code, at + 9);
            if (!MainUtilitySourceByteSetter(code, setter)) continue;
            for (var push = Math.Max(0, at - 96); push < at; push++)
            {
                if (code[push] != 0x68) continue;
                var cursor = push + 5;
                if (cursor + 6 <= at && code[cursor] == 0x8b && code[cursor + 1] == 0x8d) cursor += 6;
                else if (cursor + 3 <= at && code[cursor] == 0x8b && code[cursor + 1] == 0x4d) cursor += 3;
                else continue;
                if (cursor + 13 > at || code[cursor++] != 0x51 || code[cursor] != 0xe8) continue;
                cursor += 5;
                if (!code.AsSpan(cursor, 5).SequenceEqual(new byte[] { 0x83, 0xc4, 8, 0x85, 0xc0 })) continue;
                cursor += 5;
                if (cursor + 2 != at || code[cursor] != 0x75 || unchecked((sbyte)code[cursor + 1]) < 14) continue;
                var option = MainUtilityWideToken(image, U32(code, push + 1));
                if (option is null) continue;
                // Both source Win32 argument APIs must precede this actual
                // comparison in the same bounded frame, in original order.
                var frame = MainUtilityFrameEntry(code, at, 4096);
                var imports = new List<(int At, string? Name)>();
                for (var scan = frame; scan + 6 <= push; scan++)
                    if (code[scan] == 0xff && code[scan + 1] == 0x15)
                        imports.Add((scan, image.ImportName(U32(code, scan + 2))));
                var first = imports.LastOrDefault(item => item.Name == "GetCommandLineW");
                var second = imports.LastOrDefault(item => item.Name == "CommandLineToArgvW");
                if (first.Name is null || second.Name is null || first.At >= second.At) continue;
                candidates.Add((option, Convert.ToHexString(SHA256.HashData(code.AsSpan(push, at + 14 - push))).ToLowerInvariant()));
            }
        }
        if (candidates.Count != 1) throw new NotSupportedException("Original source startup has no unique argument-to-independent-byte producer.");
        return FalloutPlatformStartupArgumentSource.Read(main, candidates[0].Option, candidates[0].Digest);
    }
    private static (byte[] Code, Image Image) MainUtilityImage(string executable, FalloutMainUtilitySource main)
    {
        main.Validate(); var bytes = File.ReadAllBytes(executable);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != main.Main.EngineSha256)
            throw new InvalidDataException("Main utility declaration changed its selected original executable.");
        return Load(bytes);
    }
    private static string? MainUtilityWideToken(Image image, uint address)
    {
        var bytes = new List<byte>();
        for (var index = 0U; index < 129; index++)
        {
            var at = checked(address + index * sizeof(ushort));
            if (!image.IsFileExtent(at, sizeof(ushort))) return null;
            var value = image.Read(at, sizeof(ushort));
            if (value[0] == 0 && value[1] == 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (value[1] != 0 || value[0] is < 0x21 or > 0x7e) return null;
            bytes.Add(value[0]);
        }
        return null;
    }
    private static int MainUtilityFrameEntry(byte[] code, int at, int bound)
    {
        for (var cursor = at; cursor >= Math.Max(0, at - bound); cursor--)
            if (cursor + 3 <= code.Length && code.AsSpan(cursor, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec })) return cursor;
        throw new NotSupportedException("Original utility source has no bounded compiler frame.");
    }
    private static bool MainUtilitySourceByteSetter(byte[] code, int entry)
    {
        ReadOnlySpan<byte> body;
        try { body = StatisticFunctionBody(code, entry); }
        catch (NotSupportedException) { return false; }
        if (body.Length > 64 || body[^3] != 0xc2 || body[^2] != 4 || body[^1] != 0) return false;
        var argument = false; var stored = false;
        foreach (var at in MainUtilityInstructionOffsets(body))
        {
            if (at + 3 <= body.Length && body[at] == 0x8a && body[at + 1] == 0x4d && body[at + 2] == 8) argument = true;
            if (at + 3 <= body.Length && body[at] == 0x88 && body[at + 1] == 0x48) stored = argument;
            if (body[at] == 0xe8) return false;
        }
        return stored;
    }
    private static IReadOnlyList<int> MainUtilityInstructionOffsets(ReadOnlySpan<byte> body)
    {
        var result = new List<int>();
        for (var at = 0; at < body.Length;)
        {
            result.Add(at); var length = MainUtilityInstructionLength(body[at..]);
            if (length <= 0 || length > body.Length - at) throw new NotSupportedException("Original utility command has an unowned compiler instruction envelope.");
            at += length;
        }
        return result.AsReadOnly();
    }
    private static int MainUtilityInstructionLength(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return -1;
        var opcode = bytes[0];
        if (opcode is >= 0x50 and <= 0x5f || opcode is 0x90 or 0xc3) return 1;
        if (opcode is 0x6a or 0xeb || opcode is >= 0x70 and <= 0x7f || opcode is >= 0xb0 and <= 0xb7) return 2;
        if (opcode is 0xe8 or 0xe9 or 0x68 || opcode is >= 0xb8 and <= 0xbf) return 5;
        if (opcode == 0xc2) return 3;
        if (opcode == 0x0f)
        {
            if (bytes.Length < 2) return -1;
            if (bytes[1] is >= 0x80 and <= 0x8f) return 6;
            if (bytes[1] is 0xb6 or 0xbe) return 2 + MainUtilityModRmLength(bytes[2..]);
            return -1;
        }
        if (opcode is 0x8b or 0x89 or 0x8d or 0x85 or 0x84 or 0x33 or 0x32 or 0x31 or 0x8a or 0x88)
            return 1 + MainUtilityModRmLength(bytes[1..]);
        if (opcode is 0x83 or 0xc6) return 2 + MainUtilityModRmLength(bytes[1..]);
        if (opcode is 0x81 or 0xc7) return 5 + MainUtilityModRmLength(bytes[1..]);
        return -1;
    }
    private static int MainUtilityModRmLength(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) throw new InvalidDataException("Original utility ModRM is truncated.");
        var mode = bytes[0] >> 6; var rm = bytes[0] & 7; var length = 1;
        if (mode == 3) return length;
        if (rm == 4)
        {
            if (bytes.Length < 2) throw new InvalidDataException("Original utility SIB is truncated.");
            length++; if (mode == 0 && (bytes[1] & 7) == 5) length += 4;
        }
        else if (mode == 0 && rm == 5) length += 4;
        length += mode == 1 ? 1 : mode == 2 ? 4 : 0;
        return length;
    }
}
