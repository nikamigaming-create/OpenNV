using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

/// <summary>Owned engine calendar declaration; it has fixed month lengths, independent of the year.</summary>
internal sealed record FalloutCalendar(IReadOnlyList<ushort> MonthDays, string SourceSha256)
{
    internal static FalloutCalendar Read(string executable)
    {
        var bytes = File.ReadAllBytes(executable);
        using var pe = new PEReader(new MemoryStream(bytes, false));
        if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32)
            throw new NotSupportedException("Calendar declarations require the owned Win32 layout.");
        var matches = new List<ushort[]>();
        foreach (var section in pe.PEHeaders.SectionHeaders.Where(section =>
            (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0 && section.SizeOfRawData > 0))
        {
            var found = Decode(bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData));
            if (found is not null) matches.Add(found);
        }
        if (matches.Count != 1) throw new NotSupportedException("Owned calendar declaration is missing or ambiguous.");
        var days = matches[0];
        var declaration = days.SelectMany(BitConverter.GetBytes).ToArray();
        return new(days, Convert.ToHexString(SHA256.HashData(declaration)).ToLowerInvariant());
    }

    internal static ushort[]? Decode(ReadOnlySpan<byte> data)
    {
        ushort[]? result = null;
        for (var offset = 0; offset <= data.Length - 24; offset += 2)
        {
            // The compiler declaration starts with the first two civil months;
            // the remaining month lengths are retained from the owned bytes.
            if (BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]) != 31 ||
                BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]) != 28) continue;
            var values = new ushort[12];
            for (var month = 0; month < values.Length; month++)
                values[month] = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + month * 2)..]);
            if (values.Any(value => value is < 28 or > 31) || values.Sum(value => value) != 365) continue;
            if (result is not null) throw new InvalidDataException("Multiple owned calendar declarations match.");
            result = values;
        }
        return result;
    }
}
