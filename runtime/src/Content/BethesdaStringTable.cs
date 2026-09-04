using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal enum BethesdaStringTableKind
{
    Strings,
    DlStrings,
    IlStrings,
}

internal sealed class BethesdaStringTable
{
    private const int HeaderBytes = sizeof(uint) * 2;
    private const int DirectoryEntryBytes = sizeof(uint) * 2;
    private const ulong DirectoryEntryBytesUnsigned = sizeof(uint) * 2UL;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IReadOnlyDictionary<uint, string> _values;

    private BethesdaStringTable(IReadOnlyDictionary<uint, string> values) =>
        _values = values;

    internal int Count => _values.Count;

    internal string this[uint id] => _values.TryGetValue(id, out var value)
        ? value
        : throw new KeyNotFoundException($"Localized string 0x{id:x8} is absent.");

    internal bool TryGetValue(uint id, out string value) =>
        _values.TryGetValue(id, out value!);

    internal static BethesdaStringTable Load(
        RuntimeLiveContentSource source,
        string logicalPath,
        string? preferredArchive = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var kind = KindFromPath(logicalPath);
        if (!source.TryRead(logicalPath, preferredArchive, out var payload, out var resolvedSource))
            throw new FileNotFoundException(
                $"Owned localization table is missing: {logicalPath}", logicalPath);
        try
        {
            return Parse(payload, kind);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException(
                $"Owned localization table is invalid: {resolvedSource}", exception);
        }
    }

    internal static BethesdaStringTable Parse(
        ReadOnlySpan<byte> payload,
        BethesdaStringTableKind kind)
    {
        if (payload.Length < HeaderBytes)
            throw new InvalidDataException("Localization table header is truncated.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        var directoryBytes = checked((ulong)count * DirectoryEntryBytesUnsigned);
        var dataStart = checked((ulong)HeaderBytes + directoryBytes);
        var expectedSize = checked(dataStart + dataSize);
        if (expectedSize != (ulong)payload.Length)
            throw new InvalidDataException(
                "Localization table size does not match its header exactly.");
        if (count > int.MaxValue)
            throw new InvalidDataException("Localization table entry count is unsupported.");

        var dataStartInt = checked((int)dataStart);
        var dataSizeInt = checked((int)dataSize);
        var data = payload.Slice(dataStartInt, dataSizeInt);
        var values = new Dictionary<uint, string>(checked((int)count));
        for (var index = 0; index < count; ++index)
        {
            var entry = payload.Slice(
                checked(HeaderBytes + (int)index * DirectoryEntryBytes),
                DirectoryEntryBytes);
            var id = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            if (offset >= dataSize)
                throw new InvalidDataException(
                    $"Localization entry 0x{id:x8} starts outside the data block.");
            if (!values.TryAdd(id, Decode(data, offset, kind)))
                throw new InvalidDataException(
                    $"Localization table repeats ID 0x{id:x8}.");
        }
        return new BethesdaStringTable(values);
    }

    private static string Decode(
        ReadOnlySpan<byte> data,
        uint offset,
        BethesdaStringTableKind kind)
    {
        var start = checked((int)offset);
        ReadOnlySpan<byte> encoded;
        if (kind == BethesdaStringTableKind.Strings)
        {
            var terminator = data[start..].IndexOf((byte)0);
            if (terminator < 0)
                throw new InvalidDataException(
                    "STRINGS entry has no null terminator inside the data block.");
            encoded = data.Slice(start, terminator);
        }
        else
        {
            if (data.Length - start < 4)
                throw new InvalidDataException(
                    "Length-prefixed localization entry is truncated.");
            var encodedBytes = BinaryPrimitives.ReadUInt32LittleEndian(data[start..]);
            if (encodedBytes == 0 || encodedBytes > int.MaxValue ||
                encodedBytes > data.Length - start - 4)
                throw new InvalidDataException(
                    "Length-prefixed localization entry exceeds the data block.");
            encoded = data.Slice(start + 4, checked((int)encodedBytes));
            if (encoded[^1] != 0)
                throw new InvalidDataException(
                    "Length-prefixed localization entry has no final null byte.");
            encoded = encoded[..^1];
        }
        try
        {
            return StrictUtf8.GetString(encoded);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Localization entry is not valid UTF-8.", exception);
        }
    }

    private static BethesdaStringTableKind KindFromPath(string logicalPath) =>
        Path.GetExtension(logicalPath).ToLowerInvariant() switch
        {
            ".strings" => BethesdaStringTableKind.Strings,
            ".dlstrings" => BethesdaStringTableKind.DlStrings,
            ".ilstrings" => BethesdaStringTableKind.IlStrings,
            _ => throw new InvalidDataException(
                $"Unsupported Bethesda localization table extension: {logicalPath}"),
        };
}
