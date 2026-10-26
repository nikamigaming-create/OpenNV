using System.Runtime.CompilerServices;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static class FalloutMiscStatisticNames
{
    private sealed record Names(IReadOnlyList<string> Values);
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, Names> Cache = new();

    internal static int Index(RuntimeLiveContentSource source, string name)
    {
        var names = Cache.GetValue(source, content => new(
            FalloutExecutableStringTable.ReadMiscStatisticNames(content.FalloutExecutablePath))).Values;
        for (var index = 0; index < names.Count; index++)
            if (names[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return index;
        throw new NotSupportedException($"Miscellaneous statistic has no owned script declaration: {name}.");
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static IReadOnlyList<string> ReadMiscStatisticNames(string path)
    {
        var (_, image) = Load(path);
        return image.MiscStatisticNames();
    }

    internal static IReadOnlyList<string>? ReadMiscStatisticNames(ReadOnlySpan<byte> table, Func<uint, string?> literal)
    {
        if (table.Length < FalloutMiscStatistics.Count * sizeof(uint) || literal(U32(table, 0)) != "Quests Completed") return null;
        var names = new List<string>(FalloutMiscStatistics.Count);
        for (var index = 0; index < FalloutMiscStatistics.Count; index++)
        {
            var name = literal(U32(table, index * sizeof(uint)));
            if (string.IsNullOrEmpty(name) || name.Any(character => !char.IsAsciiLetter(character) && character is not (' ' or '-'))) return null;
            names.Add(name);
        }
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            throw new InvalidDataException("Owned miscellaneous statistic names are duplicated.");
        return names;
    }

    private sealed partial class Image
    {
        internal IReadOnlyList<string> MiscStatisticNames()
        {
            IReadOnlyList<string>? result = null;
            foreach (var section in headers.SectionHeaders.Where(section =>
                (section.SectionCharacteristics & System.Reflection.PortableExecutable.SectionCharacteristics.MemWrite) != 0))
            {
                var data = bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData);
                for (var at = 0; at <= data.Length - FalloutMiscStatistics.Count * sizeof(uint); at += sizeof(uint))
                {
                    var names = ReadMiscStatisticNames(data[at..], Literal);
                    if (names is null) continue;
                    if (result is not null) throw new InvalidDataException("Owned miscellaneous statistic name table is ambiguous.");
                    result = names;
                }
            }
            return result ?? throw new NotSupportedException("Owned miscellaneous statistic name table is unbound.");
        }
    }
}
