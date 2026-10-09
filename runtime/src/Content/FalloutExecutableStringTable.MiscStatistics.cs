using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutMiscellaneousStatisticCatalogue ReadMiscellaneousStatistics(string executable, string expectedSha256)
    {
        _ = FalloutMiscellaneousStatisticSource.Consumers(expectedSha256);
        var bytes = File.ReadAllBytes(executable);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedSha256)
            throw new InvalidDataException("Statistic executable changed from its selected source receipt.");
        var (code, image) = Load(bytes);
        var start = image.ScriptCommandStart("ModPCMiscStat", code, 2);
        _ = image.ScriptCommandStart("GetPCMiscStat", code, 1);
        return ReadMiscellaneousStatisticCatalogue(code, start, expectedSha256,
            image.IsWritableExtent, image.IsFileExtent, image.Read, image.Literal);
    }

    // Independently authored compiler/transport contracts use the same reader;
    // reaching this seam never supplies an owned executable/hash admission.
    internal static FalloutMiscellaneousStatisticCatalogue ReadMiscellaneousStatisticCatalogue(byte[] code, int start,
        string selectedEngineSha256, Func<uint, int, bool> writable, Func<uint, int, bool> fileBacked,
        Func<uint, int, byte[]> read, Func<uint, string?> literal)
    {
        _ = FalloutMiscellaneousStatisticSource.Consumers(selectedEngineSha256);
        if (start < 0 || start >= code.Length) throw new InvalidDataException("Statistic command entry exceeds its code extent.");
        var command = code.AsSpan(start, Math.Min(192, code.Length - start));
        var consumers = new List<(int Entry, int Count, uint Array)>();
        for (var at = 0; at < command.Length; at++)
        {
            var next = FramePush(command, at, out var first);
            if (next < 0) continue;
            next = FramePush(command, next, out var second);
            if (next < 0 || first >= 0 || second >= 0 || first == second || next + 5 > command.Length || command[next] != 0xe8)
                continue;
            var entry = RelativeStatisticCall(code, start + next);
            var body = StatisticFunctionBody(code, entry);
            var bounds = StatisticBounds(body).ToArray();
            var arrays = StatisticIndexedLoads(body).Distinct().ToArray();
            if (bounds.Length != 1 || arrays.Length != 1 || !writable(arrays[0], checked(bounds[0] * sizeof(uint)))) continue;
            consumers.Add((entry, bounds[0], arrays[0]));
        }
        var unique = consumers.Distinct().ToArray();
        if (unique.Length != 1) throw new NotSupportedException("Statistic command has no unique source-indexed signed counter consumer.");
        var delta = unique[0];
        var declaration = FalloutMiscellaneousStatisticSource.Consumers(selectedEngineSha256);
        var deltaBody = StatisticFunctionBody(code, delta.Entry);
        if (declaration.ChallengeEvent is { } kind && !StatisticImmediateCall(deltaBody, kind))
            throw new NotSupportedException("Statistic delta has no declared source challenge-event invocation.");
        var menuId = StatisticMenuId(code, delta.Entry, deltaBody);

        var constructors = new List<int>();
        for (var at = 0; at + 7 <= code.Length; at++)
        {
            if (!StatisticIndexedAddress(code, at, 0x89, out var destination) || destination != delta.Array) continue;
            var entry = StatisticFrameEntry(code, at);
            var body = StatisticFunctionBody(code, entry);
            if (!StatisticBounds(body).Contains(delta.Count)) continue;
            if (!constructors.Contains(entry)) constructors.Add(entry);
        }
        if (constructors.Count != 1) throw new NotSupportedException("Statistic pointer array has no unique source constructor.");
        var constructor = StatisticFunctionBody(code, constructors[0]);
        var catalogues = new List<(uint Table, IReadOnlyList<string> Names)>();
        foreach (var table in StatisticIndexedLoads(constructor).Distinct())
        {
            if (table == delta.Array || !fileBacked(table, checked(delta.Count * sizeof(uint)))) continue;
            var words = read(table, checked(delta.Count * sizeof(uint)));
            var names = Enumerable.Range(0, delta.Count).Select(index => literal(U32(words, index * sizeof(uint)))).ToArray();
            if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != delta.Count) continue;
            catalogues.Add((table, Array.AsReadOnly(names.Select(name => name!).ToArray())));
        }
        if (catalogues.Count != 1) throw new NotSupportedException("Statistic constructor has no unique complete original literal catalogue.");
        var formats = StatisticFormats(constructor, literal).ToList();
        for (var at = 0; formats.Count == 0 && at + 7 <= constructor.Length; at++)
        {
            if (!StatisticIndexedAddress(constructor, at, 0x8b, out var table) || table != catalogues[0].Table) continue;
            // A separately constructed entry follows its original catalogue
            // argument; do not inspect arbitrary allocator/helper bodies.
            var entry = StatisticEntryConstructor(code, constructors[0], constructor, at);
            if (entry is { } linked) formats.AddRange(StatisticFormats(StatisticFunctionBody(code, linked), literal));
        }
        var uniqueFormats = formats.Distinct(StringComparer.Ordinal).ToArray();
        if (uniqueFormats.Length != 1) throw new NotSupportedException("Statistic constructor has no unique owned setting-name formatter.");
        return new(catalogues[0].Names, uniqueFormats[0], menuId);
    }

    private static int FramePush(ReadOnlySpan<byte> body, int at, out sbyte slot)
    {
        slot = 0;
        if (at + 3 <= body.Length && body[at] == 0xff && body[at + 1] == 0x75)
        { slot = unchecked((sbyte)body[at + 2]); return at + 3; }
        if (at + 4 <= body.Length && body[at] == 0x8b && (body[at + 1] & 0xc7) == 0x45 &&
            body[at + 3] == 0x50 + ((body[at + 1] >> 3) & 7))
        { slot = unchecked((sbyte)body[at + 2]); return at + 4; }
        return -1;
    }
    private static int? StatisticEntryConstructor(byte[] code, int origin, ReadOnlySpan<byte> body, int load)
    {
        // Follow the loaded catalogue argument and frame-owned receiver on
        // instruction boundaries. An operand containing E8 is not a CALL.
        var register = (body[load + 1] >> 3) & 7;
        var cursor = load + 7;
        if (cursor >= body.Length || body[cursor++] != 0x50 + register) return null;
        if (cursor + 3 <= body.Length && body[cursor] == 0x8b && body[cursor + 1] == 0x4d &&
            unchecked((sbyte)body[cursor + 2]) < 0)
            cursor += 3;
        else if (cursor + 6 <= body.Length && body[cursor] == 0x8b && body[cursor + 1] == 0x8d &&
            unchecked((int)U32(body, cursor + 2)) < 0)
            cursor += 6;
        else return null;
        if (cursor + 5 > body.Length || body[cursor] != 0xe8) return null;
        return RelativeStatisticCall(code, checked(origin + cursor));
    }
    private static int RelativeStatisticCall(byte[] code, int at)
    {
        var target = (long)at + 5 + unchecked((int)U32(code, at + 1));
        if (at < 0 || at + 5 > code.Length || code[at] != 0xe8 || target < 0 || target >= code.Length)
            throw new InvalidDataException("Statistic source call exceeds its selected code extent.");
        return (int)target;
    }
    private static ReadOnlySpan<byte> StatisticFunctionBody(byte[] code, int entry)
    {
        if (entry < 0 || entry > code.Length - 3 || !code.AsSpan(entry, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }))
            throw new NotSupportedException("Statistic linked source function has an unowned compiler envelope.");
        var body = code.AsSpan(entry, Math.Min(1024, code.Length - entry));
        for (var at = 3; at + 2 <= body.Length; at++)
            if (body[at] == 0x5d && body[at + 1] is 0xc3 or 0xc2)
            {
                var end = at + (body[at + 1] == 0xc2 ? 4 : 2);
                if (end > body.Length) throw new InvalidDataException("Statistic linked source return operand is truncated.");
                return body[..end];
            }
        throw new NotSupportedException("Statistic linked source function has no bounded owned return.");
    }
    private static int StatisticFrameEntry(byte[] code, int at)
    {
        for (var entry = at; entry >= Math.Max(0, at - 768); entry--)
            if (entry + 3 <= code.Length && code.AsSpan(entry, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec })) return entry;
        throw new NotSupportedException("Statistic constructor has no bounded owned frame entry.");
    }
    private static IEnumerable<int> StatisticBounds(ReadOnlySpan<byte> body)
    {
        var result = new HashSet<int>();
        for (var at = 0; at + 3 <= body.Length; at++)
        {
            if (body[at] is not (0x83 or 0x81) || ((body[at + 1] >> 3) & 7) != 7) continue;
            var mode = body[at + 1] >> 6; var rm = body[at + 1] & 7;
            var immediate = mode == 3 ? at + 2 : mode == 1 && rm == 5 ? at + 3 : -1;
            if (immediate < 0 || immediate + (body[at] == 0x81 ? 4 : 1) > body.Length) continue;
            var count = body[at] == 0x83 ? unchecked((sbyte)body[immediate]) : unchecked((int)U32(body, immediate));
            if (count is > 0 and <= ushort.MaxValue) result.Add(count);
        }
        return result;
    }
    private static IEnumerable<uint> StatisticIndexedLoads(ReadOnlySpan<byte> body)
    {
        var result = new List<uint>();
        for (var at = 0; at + 7 <= body.Length; at++)
            if (StatisticIndexedAddress(body, at, 0x8b, out var address)) result.Add(address);
        return result;
    }
    private static bool StatisticIndexedAddress(ReadOnlySpan<byte> body, int at, byte opcode, out uint address)
    {
        address = 0;
        if (at < 0 || at + 7 > body.Length || body[at] != opcode || (body[at + 1] & 0xc7) != 4 ||
            (body[at + 2] >> 6) != 2 || (body[at + 2] & 7) != 5 || ((body[at + 2] >> 3) & 7) == 4) return false;
        address = U32(body, at + 3); return true;
    }
    private static IEnumerable<string> StatisticFormats(ReadOnlySpan<byte> body, Func<uint, string?> literal)
    {
        var result = new List<string>();
        for (var at = 0; at + 5 <= body.Length; at++)
        {
            if (body[at] != 0x68 || literal(U32(body, at + 1)) is not { } text || !text.Contains('%')) continue;
            try { _ = FalloutMiscellaneousStatisticSource.SettingName(text, 0); result.Add(text); }
            catch (NotSupportedException) { /* Another source literal is not a statistic formatter. */ }
        }
        return result;
    }
    private static bool StatisticImmediateCall(ReadOnlySpan<byte> body, uint value)
    {
        for (var at = 0; at + 7 <= body.Length; at++)
            if (body[at] == 0x6a && unchecked((uint)(sbyte)body[at + 1]) == value && body[at + 2] == 0xe8 ||
                at + 10 <= body.Length && body[at] == 0x68 && U32(body, at + 1) == value && body[at + 5] == 0xe8) return true;
        return false;
    }
    private static uint StatisticMenuId(byte[] code, int entry, ReadOnlySpan<byte> body)
    {
        var values = new HashSet<uint>();
        for (var at = 0; at + 10 <= body.Length; at++)
        {
            if (body[at] == 0x68 && body[at + 5] == 0xe8)
            {
                var value = U32(body, at + 1);
                if (value is >= 1000 and <= ushort.MaxValue) values.Add(value);
            }
            if (body[at] != 0xe8 || body[at + 5] != 0x50 || body[at + 6] != 0xe8) continue;
            var target = RelativeStatisticCall(code, entry + at);
            var provider = StatisticFunctionBody(code, target);
            if (provider.Length == 10 && provider[3] == 0xb8 && provider[8] == 0x5d && provider[9] == 0xc3)
                values.Add(U32(provider, 4));
        }
        return values.Count == 1 ? values.Single() : throw new NotSupportedException("Statistic refresh has no unique source menu selector.");
    }
}
