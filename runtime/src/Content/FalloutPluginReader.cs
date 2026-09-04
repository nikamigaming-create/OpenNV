using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace OpenNV.Runtime.Content;

internal sealed class FalloutPluginFormatException : IOException
{
    internal FalloutPluginFormatException(string message)
        : base(message)
    {
    }

    internal FalloutPluginFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal readonly record struct FalloutFormKey(string OwnerPlugin, uint ObjectId)
{
    internal const int ObjectIdBits = 24;
    internal const uint ObjectIdMask = (1u << ObjectIdBits) - 1u;

    public override string ToString() => $"{OwnerPlugin}:{ObjectId:x6}";
}

internal readonly record struct FalloutPluginGroup(byte[] Label, int Type)
{
    internal uint LabelAsUInt32 => BinaryPrimitives.ReadUInt32LittleEndian(Label);
}

internal readonly record struct FalloutPluginSubrecord(string Signature, ReadOnlyMemory<byte> Data);

internal sealed class FalloutPluginRecord
{
    internal const uint CompressedFlag = 0x0004_0000;
    internal const uint DeletedFlag = 0x0000_0020;
    private const int MinimumZlibPayloadBytes = 6;
    private const int BitsPerByte = 8;
    private const int DeflateCompressionMethod = 8;
    private const int ZlibHeaderCheckDivisor = 31;
    private const int ZlibCompressionMethodMask = 0x0f;
    private const int ZlibPresetDictionaryFlag = 0x20;

    private readonly FalloutPlugin _plugin;
    private readonly long _dataOffset;
    private readonly int _storedSize;

    internal FalloutPluginRecord(
        FalloutPlugin plugin,
        string signature,
        uint rawFormId,
        uint flags,
        long headerOffset,
        long dataOffset,
        int storedSize,
        IReadOnlyList<FalloutPluginGroup> groups)
    {
        _plugin = plugin;
        Signature = signature;
        RawFormId = rawFormId;
        Flags = flags;
        HeaderOffset = headerOffset;
        _dataOffset = dataOffset;
        _storedSize = storedSize;
        Groups = groups;
    }

    internal FalloutPlugin Plugin => _plugin;
    internal string Signature { get; }
    internal uint RawFormId { get; }
    internal FalloutFormKey FormKey => _plugin.AdjustFormId(RawFormId);
    internal uint Flags { get; }
    internal bool IsCompressed => (Flags & CompressedFlag) != 0;
    internal bool IsDeleted => (Flags & DeletedFlag) != 0;
    internal long HeaderOffset { get; }
    internal IReadOnlyList<FalloutPluginGroup> Groups { get; }

    internal byte[] ReadData()
    {
        var stored = _plugin.ReadAt(_dataOffset, _storedSize, $"{Signature} record data");
        if (!IsCompressed)
            return stored;
        if (stored.Length < sizeof(uint))
            throw Error("compressed record has no uncompressed-size prefix");

        var expectedSize = BinaryPrimitives.ReadUInt32LittleEndian(stored);
        if (expectedSize > int.MaxValue)
            throw Error($"declares unsupported uncompressed size {expectedSize}");
        try
        {
            return Inflate(stored.AsMemory(sizeof(uint)), expectedSize, zlibFramed: true);
        }
        catch (FalloutPluginFormatException)
        {
            throw;
        }
        catch (InvalidDataException zlibError)
        {
            var payload = stored.AsMemory(sizeof(uint));
            if (!HasSupportedZlibHeader(payload.Span))
                throw new FalloutPluginFormatException(
                    $"{_plugin.Name} {Signature} {RawFormId:x8} has invalid zlib data at 0x{HeaderOffset:x}",
                    zlibError);
            try
            {
                return Inflate(payload[2..^4], expectedSize, zlibFramed: false);
            }
            catch (InvalidDataException deflateError)
            {
                throw new FalloutPluginFormatException(
                    $"{_plugin.Name} {Signature} {RawFormId:x8} has invalid deflate data at 0x{HeaderOffset:x}",
                    deflateError);
            }
        }
    }

    private byte[] Inflate(ReadOnlyMemory<byte> payload, uint expectedSize, bool zlibFramed)
    {
        using var input = new MemoryStream(payload.ToArray(), writable: false);
        using Stream inflater = zlibFramed
            ? new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true)
            : new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);
        using var output = new MemoryStream((int)expectedSize);
        inflater.CopyTo(output);
        if (input.Position != input.Length)
            throw Error("compressed payload contains trailing data");
        var result = output.ToArray();
        if (result.Length != expectedSize)
            throw Error($"uncompressed size mismatch: expected {expectedSize}, found {result.Length}");
        return result;
    }

    private static bool HasSupportedZlibHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < MinimumZlibPayloadBytes)
            return false;
        var compressionMethod = payload[0] & ZlibCompressionMethodMask;
        var headerCheck = (payload[0] << BitsPerByte) | payload[1];
        var presetDictionary = (payload[1] & ZlibPresetDictionaryFlag) != 0;
        return compressionMethod == DeflateCompressionMethod &&
            headerCheck % ZlibHeaderCheckDivisor == 0 &&
            !presetDictionary;
    }

    internal IEnumerable<FalloutPluginSubrecord> ReadSubrecords()
    {
        var data = ReadData();
        var offset = 0;
        uint? extendedSize = null;
        while (offset < data.Length)
        {
            if (data.Length - offset < FalloutPlugin.SubrecordHeaderSize)
                throw Error($"truncated subrecord header at payload offset 0x{offset:x}");
            var signature = FalloutPlugin.DecodeSubrecordSignature(
                data.AsSpan(offset, FalloutPlugin.SignatureSize),
                Signature,
                HeaderOffset + FalloutPlugin.RecordHeaderSize + offset);
            var declaredSize = BinaryPrimitives.ReadUInt16LittleEndian(
                data.AsSpan(offset + FalloutPlugin.SignatureSize, sizeof(ushort)));
            offset += FalloutPlugin.SubrecordHeaderSize;

            if (signature == FalloutPlugin.ExtendedSizeSignature)
            {
                if (declaredSize != sizeof(uint) || extendedSize.HasValue || data.Length - offset < sizeof(uint))
                    throw Error($"invalid XXXX marker at payload offset 0x{offset - FalloutPlugin.SubrecordHeaderSize:x}");
                extendedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
                offset += sizeof(uint);
                continue;
            }

            var size = extendedSize ?? declaredSize;
            extendedSize = null;
            if (size > int.MaxValue || size > data.Length - offset)
                throw Error($"subrecord {signature} exceeds its record at payload offset 0x{offset:x}");
            yield return new FalloutPluginSubrecord(signature, data.AsMemory(offset, (int)size));
            offset += (int)size;
        }

        if (extendedSize.HasValue)
            throw Error("has a dangling XXXX marker");
    }

    private FalloutPluginFormatException Error(string detail) =>
        new($"{_plugin.Name} {Signature} {RawFormId:x8} {detail} (record 0x{HeaderOffset:x})");
}

internal sealed class FalloutPlugin : IDisposable
{
    private const int Windows1252CodePage = 1252;
    internal const int RecordHeaderSize = 24;
    internal const int SignatureSize = 4;
    internal const int SubrecordHeaderSize = 6;
    internal const string ExtendedSizeSignature = "XXXX";

    private const string GroupSignature = "GRUP";
    private const string HeaderSignature = "TES4";
    private const string MasterSignature = "MAST";
    private const int RecordSizeOffset = 4;
    private const int RecordFlagsOffset = 8;
    private const int RecordFormIdOffset = 12;
    private const int GroupLabelOffset = 8;
    private const int GroupTypeOffset = 12;
    private const int WeatherImageSpaceSamples = 6;
    private const int ImageSpaceModifierChannels = 21;
    private const int ImageSpaceModifierAddOffset = 0x40;
    private const int MaximumGroupDepth = 64;

    private readonly long _length;
    private readonly SafeFileHandle _handle;
    private ReadOnlyCollection<string> _masters;
    private ReadOnlyCollection<string> _namespaces;
    private ReadOnlyCollection<FalloutPluginRecord> _records;
    private IReadOnlyDictionary<uint, string> _injectedNamespaces =
        new ReadOnlyDictionary<uint, string>(new Dictionary<uint, string>());
    private bool _usesFallout3EsmSelfNamespaces;

    private FalloutPlugin(
        string path,
        string name,
        long length,
        IReadOnlyList<string> masters,
        IReadOnlyList<string> namespaces,
        IReadOnlyList<FalloutPluginRecord> records)
    {
        Path = path;
        Name = name;
        _length = length;
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        _masters = new ReadOnlyCollection<string>(masters.ToArray());
        _namespaces = new ReadOnlyCollection<string>(namespaces.ToArray());
        _records = new ReadOnlyCollection<FalloutPluginRecord>(records.ToArray());
    }

    internal string Path { get; }
    internal string Name { get; }
    internal IReadOnlyList<string> Masters => _masters;
    internal IReadOnlyList<string> Namespaces => _namespaces;
    internal IReadOnlyList<FalloutPluginRecord> Records => _records;

    internal static FalloutPlugin Open(string path, string? canonicalName = null)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("Fallout plugin does not exist.", fullPath);
        if (info.Length < RecordHeaderSize)
            throw new FalloutPluginFormatException($"Plugin is too small to contain a record: {fullPath}");
        var name = canonicalName ?? info.Name;
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new FalloutPluginFormatException($"Invalid canonical plugin name: {name}");

        var records = new List<FalloutPluginRecord>();
        var provisional = new FalloutPlugin(
            fullPath,
            name,
            info.Length,
            Array.Empty<string>(),
            new[] { name },
            records);
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            ScanRegion(provisional, stream, info.Length, Array.Empty<FalloutPluginGroup>(), records, 0);
        }
        catch
        {
            provisional.Dispose();
            throw;
        }

        try
        {
            var headers = records.Where(record => record.Signature == HeaderSignature).ToArray();
            if (headers.Length != 1 || records.Count == 0 || !ReferenceEquals(records[0], headers[0]))
                throw new FalloutPluginFormatException(
                    $"Plugin must begin with exactly one TES4 record: {fullPath}");
            var masters = headers[0].ReadSubrecords()
                .Where(subrecord => subrecord.Signature == MasterSignature)
                .Select(subrecord => DecodeZeroTerminated(subrecord.Data.Span, $"master name in {name}"))
                .ToArray();
            if (masters.Any(string.IsNullOrWhiteSpace) ||
                masters.Distinct(StringComparer.OrdinalIgnoreCase).Count() != masters.Length ||
                masters.Any(master => master.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new FalloutPluginFormatException($"{name} declares invalid or duplicate masters.");

            provisional._masters = new ReadOnlyCollection<string>(masters);
            provisional._namespaces = new ReadOnlyCollection<string>(masters.Append(name).ToArray());
            provisional._records = new ReadOnlyCollection<FalloutPluginRecord>(records.ToArray());
            return provisional;
        }
        catch
        {
            provisional.Dispose();
            throw;
        }
    }

    internal void SetLoadOrderContext(
        IReadOnlyList<string> masters,
        int loadOrderIndex,
        IReadOnlyList<string> orderedPluginNames)
    {
        if (masters.Count != _masters.Count)
            throw new FalloutPluginFormatException($"Canonical master count differs for {Name}.");
        if (loadOrderIndex < 0 || loadOrderIndex >= orderedPluginNames.Count ||
            !orderedPluginNames[loadOrderIndex].Equals(Name, StringComparison.OrdinalIgnoreCase))
            throw new FalloutPluginFormatException($"Load-order context differs for {Name}.");
        _masters = new ReadOnlyCollection<string>(masters.ToArray());
        _namespaces = new ReadOnlyCollection<string>(masters.Append(Name).ToArray());
        var injected = new Dictionary<uint, string>();
        for (var index = _namespaces.Count; index < loadOrderIndex; index++)
            injected.Add((uint)index, orderedPluginNames[index]);
        _usesFallout3EsmSelfNamespaces =
            System.IO.Path.GetExtension(Path).Equals(".esm", StringComparison.OrdinalIgnoreCase) &&
            (Name.Equals("Fallout3.esm", StringComparison.OrdinalIgnoreCase) ||
             masters.Count == 1 && masters[0].Equals("Fallout3.esm", StringComparison.OrdinalIgnoreCase));
        _injectedNamespaces = new ReadOnlyDictionary<uint, string>(injected);
    }

    internal FalloutFormKey AdjustFormId(uint rawFormId)
    {
        var localIndex = rawFormId >> FalloutFormKey.ObjectIdBits;
        var objectId = rawFormId & FalloutFormKey.ObjectIdMask;
        if (localIndex < _namespaces.Count)
            return new FalloutFormKey(_namespaces[(int)localIndex], objectId);
        // The standalone Fallout 3 GOTY ESM corpus has six sparse records whose
        // stored namespace exceeds MAST+self. Treating those indices as later
        // plugins produces record-type collisions; retail treats them as records
        // of the current Fallout 3 ESM. Keep this gate exact to Fallout3.esm and
        // its one-master official ESMs, and retain the reserved-object rejection.
        if (objectId >= 0x800 && _usesFallout3EsmSelfNamespaces)
            return new FalloutFormKey(Name, objectId);
        if (objectId >= 0x800 && _injectedNamespaces.TryGetValue(localIndex, out var injectedOwner))
            return new FalloutFormKey(injectedOwner, objectId);
        throw new FalloutPluginFormatException(
            $"{Name} form {rawFormId:x8} uses undeclared local namespace {localIndex}; " +
            $"declared namespace count is {_namespaces.Count}, and no safe configured injection target exists.");
    }

    internal FalloutFormKey? AdjustOptionalFormId(uint rawFormId) =>
        rawFormId == 0 ? null : AdjustFormId(rawFormId);

    internal byte[] ReadAt(long offset, int size, string description)
    {
        EnsureUnchanged();
        if (offset < 0 || size < 0 || offset > _length - size)
            throw new FalloutPluginFormatException($"{Name} {description} lies outside the plugin.");
        var result = new byte[size];
        var read = 0;
        while (read < result.Length)
        {
            var count = RandomAccess.Read(_handle, result.AsSpan(read), offset + read);
            if (count == 0)
                throw new FalloutPluginFormatException($"Truncated {description} in {Name}.");
            read += count;
        }
        return result;
    }

    public void Dispose() => _handle.Dispose();

    private void EnsureUnchanged()
    {
        if (RandomAccess.GetLength(_handle) != _length)
            throw new FalloutPluginFormatException($"Plugin changed after indexing: {Path}");
    }

    private static void ScanRegion(
        FalloutPlugin plugin,
        FileStream stream,
        long end,
        IReadOnlyList<FalloutPluginGroup> groups,
        List<FalloutPluginRecord> records,
        int depth)
    {
        var headerBuffer = new byte[RecordHeaderSize];
        while (stream.Position < end)
        {
            var offset = stream.Position;
            if (end - offset < RecordHeaderSize)
                throw new FalloutPluginFormatException($"Trailing bytes in {plugin.Name} at 0x{offset:x}.");
            var header = headerBuffer.AsSpan();
            ReadExactly(stream, header, "record header");
            var signature = DecodeSignature(header[..SignatureSize], offset);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header[RecordSizeOffset..]);

            if (signature == GroupSignature)
            {
                if (size < RecordHeaderSize)
                    throw new FalloutPluginFormatException($"Invalid GRUP size {size} in {plugin.Name} at 0x{offset:x}.");
                if (depth >= MaximumGroupDepth)
                    throw new FalloutPluginFormatException(
                        $"GRUP nesting exceeds {MaximumGroupDepth} in {plugin.Name} at 0x{offset:x}.");
                var groupEnd = checked(offset + size);
                if (groupEnd > end)
                    throw new FalloutPluginFormatException($"GRUP exceeds its parent in {plugin.Name} at 0x{offset:x}.");
                var label = header.Slice(GroupLabelOffset, sizeof(uint)).ToArray();
                var type = BinaryPrimitives.ReadInt32LittleEndian(header[GroupTypeOffset..]);
                var childGroups = new FalloutPluginGroup[groups.Count + 1];
                for (var index = 0; index < groups.Count; index++)
                    childGroups[index] = groups[index];
                childGroups[^1] = new FalloutPluginGroup(label, type);
                ScanRegion(plugin, stream, groupEnd, childGroups, records, depth + 1);
                if (stream.Position != groupEnd)
                    throw new FalloutPluginFormatException($"GRUP ended at the wrong offset in {plugin.Name}: 0x{offset:x}.");
                continue;
            }

            if (size > int.MaxValue)
                throw new FalloutPluginFormatException($"{signature} is too large in {plugin.Name} at 0x{offset:x}.");
            var dataOffset = stream.Position;
            var dataEnd = checked(dataOffset + size);
            if (dataEnd > end)
                throw new FalloutPluginFormatException($"{signature} exceeds its parent in {plugin.Name} at 0x{offset:x}.");
            records.Add(new FalloutPluginRecord(
                plugin,
                signature,
                BinaryPrimitives.ReadUInt32LittleEndian(header[RecordFormIdOffset..]),
                BinaryPrimitives.ReadUInt32LittleEndian(header[RecordFlagsOffset..]),
                offset,
                dataOffset,
                (int)size,
                new ReadOnlyCollection<FalloutPluginGroup>(groups.ToArray())));
            stream.Position = dataEnd;
        }
        if (stream.Position != end)
            throw new FalloutPluginFormatException(
                $"Container overrun in {plugin.Name}: expected 0x{end:x}, found 0x{stream.Position:x}.");
    }

    internal static string DecodeSubrecordSignature(ReadOnlySpan<byte> bytes, string recordSignature, long offset)
    {
        if (bytes.Length == SignatureSize && bytes[1] == (byte)'I' && bytes[2] == (byte)'A' && bytes[3] == (byte)'D')
        {
            var channel = bytes[0];
            var valid = recordSignature == "WTHR" && channel < WeatherImageSpaceSamples ||
                recordSignature == "IMAD" &&
                (channel < ImageSpaceModifierChannels ||
                 channel >= ImageSpaceModifierAddOffset &&
                 channel < ImageSpaceModifierAddOffset + ImageSpaceModifierChannels);
            if (valid)
                return $"{channel}IAD";
            if (recordSignature is "WTHR" or "IMAD")
                throw new FalloutPluginFormatException(
                    $"Invalid binary IAD channel {channel} in {recordSignature} at 0x{offset:x}.");
        }
        return DecodeSignature(bytes, offset);
    }

    private static string DecodeSignature(ReadOnlySpan<byte> bytes, long offset)
    {
        if (bytes.Length != SignatureSize || !HasValidSignatureCharacters(bytes))
            throw new FalloutPluginFormatException($"Invalid record signature at 0x{offset:x}.");
        return Encoding.ASCII.GetString(bytes);
    }

    private static bool HasValidSignatureCharacters(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (!((value >= (byte)'A' && value <= (byte)'Z') ||
                  (value >= (byte)'0' && value <= (byte)'9') || value == (byte)'_'))
                return false;
        }
        return true;
    }

    internal static string DecodeZeroTerminated(ReadOnlySpan<byte> bytes, string description)
    {
        var terminator = bytes.IndexOf((byte)0);
        var value = terminator >= 0 ? bytes[..terminator] : bytes;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(Windows1252CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) // AllowSystemFallback: strict decoding API names.
                .GetString(value);
        }
        catch (DecoderFallbackException error) // AllowSystemFallback: strict decoding exception API name.
        {
            throw new FalloutPluginFormatException($"Invalid Windows-1252 {description}.", error);
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> destination, string description)
    {
        try
        {
            stream.ReadExactly(destination);
        }
        catch (EndOfStreamException error)
        {
            throw new FalloutPluginFormatException($"Truncated {description}.", error);
        }
    }
}
