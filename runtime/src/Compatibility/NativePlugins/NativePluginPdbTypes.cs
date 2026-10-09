using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginPdbMember(string Name, uint Type, uint Offset);
internal sealed record NativePluginPdbMethod(string Name, uint Type, ushort Attributes, uint? VirtualTableOffset);
internal sealed record NativePluginPdbStructure(uint Type, string Name, uint Extent,
    IReadOnlyList<NativePluginPdbMember> Members, IReadOnlyList<NativePluginPdbMethod> Methods);

// MSF7/TPI is a public declaration format. A matched original PDB contributes
// types only, never machine code, debug addresses or a precomputed object map.
internal sealed class NativePluginPdbTypes
{
    private readonly Dictionary<uint, byte[]> _types = [];
    internal string Sha256 { get; }
    internal NativePluginPdbTypes(string path, NativePluginCodeView expected)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Sha256 = Convert.ToHexString(SHA256.HashData(input)); input.Position = 0;
        Span<byte> header = stackalloc byte[56]; input.ReadExactly(header);
        var signature = "Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8;
        if (!header[..32].SequenceEqual(signature)) throw new NotSupportedException("Selected PDB has no public MSF7 declaration owner.");
        var blockSize = U32(header, 32); var blockCount = U32(header, 40); var directoryBytes = U32(header, 44);
        var blockMap = U32(header, 52);
        if (blockSize is < 512 or > 65536 || (blockSize & (blockSize - 1)) != 0 || blockCount == 0 ||
            checked((ulong)blockSize * blockCount) != (ulong)input.Length || blockMap >= blockCount || directoryBytes > input.Length)
            throw new InvalidDataException("Original PDB superblock extents are invalid.");
        var directoryBlocks = Blocks(directoryBytes); var indices = ReadBlock(blockMap);
        if (directoryBlocks * 4 > indices.Length) throw new NotSupportedException("Original PDB directory block-map continuation is unowned.");
        var directory = Gather(directoryBytes, Enumerable.Range(0, directoryBlocks).Select(index => U32(indices, index * 4)).ToArray());
        if (directory.Length < 4) throw new InvalidDataException("Original PDB stream directory is incomplete.");
        var streamCount = U32(directory, 0);
        if (streamCount > (directory.Length - 4) / 4) throw new InvalidDataException("Original PDB stream-size table is incomplete.");
        var cursor = checked(4 + (int)streamCount * 4); var streams = new Dictionary<int, byte[]>();
        for (var index = 0; index < streamCount; ++index)
        {
            var size = U32(directory, checked(4 + (int)index * 4)); if (size == uint.MaxValue) continue;
            var count = Blocks(size);
            if (count > (directory.Length - cursor) / 4) throw new InvalidDataException("Original PDB stream block table is incomplete.");
            if (index is 1 or 2)
                streams.Add(checked((int)index), Gather(size, Enumerable.Range(0, count).Select(offset => U32(directory, cursor + offset * 4)).ToArray()));
            // Even irrelevant streams retain complete block-range admission.
            for (var offset = 0; offset < count; ++offset)
                if (U32(directory, cursor + offset * 4) >= blockCount) throw new InvalidDataException("Original PDB stream block exceeds the original file.");
            cursor += count * 4;
        }
        if (cursor != directory.Length || !streams.TryGetValue(1, out var identity) || identity.Length < 28 ||
            U32(identity, 8) != expected.Age || new Guid(identity.AsSpan(12, 16)) != expected.Guid)
            throw new InvalidDataException("Selected PDB does not match the unchanged module's actual RSDS GUID/age.");
        if (!streams.TryGetValue(2, out var tpi) || tpi.Length < 56) throw new InvalidDataException("Original PDB has no complete TPI stream.");
        var start = U32(tpi, 4); var first = U32(tpi, 8); var last = U32(tpi, 12); var bytes = U32(tpi, 16);
        if (first < 0x1000 || last < first || start < 56 || (ulong)start + bytes > (ulong)tpi.Length)
            throw new InvalidDataException("Original PDB type stream extent is invalid.");
        var position = checked((int)start); var end = checked(position + (int)bytes);
        for (var type = first; type < last; ++type)
        {
            if (end - position < 2) throw new InvalidDataException("Original PDB type length is missing.");
            var size = BinaryPrimitives.ReadUInt16LittleEndian(tpi.AsSpan(position)); position += 2;
            if (size < 2 || size > end - position) throw new InvalidDataException("Original PDB type record is incomplete.");
            _types.Add(type, tpi.AsSpan(position, size).ToArray()); position += size;
        }
        if (position != end) throw new InvalidDataException("Original PDB type records do not cover their declared extent.");

        int Blocks(uint size) => checked((int)(((ulong)size + blockSize - 1) / blockSize));
        byte[] ReadBlock(uint index)
        {
            if (index >= blockCount) throw new InvalidDataException("Original PDB block exceeds the retained file.");
            input.Position = checked((long)index * blockSize); var value = new byte[checked((int)blockSize)]; input.ReadExactly(value); return value;
        }
        byte[] Gather(uint size, IReadOnlyList<uint> indicesToRead)
        {
            if (size > int.MaxValue) throw new NotSupportedException("Original PDB declaration stream exceeds the bounded managed extent.");
            var value = new byte[checked((int)size)]; var written = 0;
            foreach (var index in indicesToRead)
            {
                var block = ReadBlock(index); var count = Math.Min(block.Length, value.Length - written);
                block.AsSpan(0, count).CopyTo(value.AsSpan(written)); written += count;
            }
            if (written != value.Length) throw new InvalidDataException("Original PDB stream bytes are incomplete."); return value;
        }
    }

    internal IReadOnlyList<NativePluginPdbStructure> Structures(string name)
    {
        var result = new List<NativePluginPdbStructure>();
        foreach (var (id, bytes) in _types)
        {
            if (bytes.Length < 20 || U16(bytes, 0) is not (0x1505 or 0x1504) || (U16(bytes, 4) & 0x80) != 0) continue;
            var position = 18; var size = Number(bytes, ref position); var text = Text(bytes, ref position);
            if (text != name) continue;
            var count = U16(bytes, 2); var fields = FieldList(U32(bytes, 6), new HashSet<uint>());
            if (fields.Members.Count + fields.Methods.Count != count)
                throw new InvalidDataException("Original PDB field declarations do not cover their exact member count.");
            result.Add(new(id, text, size, fields.Members, fields.Methods));
        }
        return result.AsReadOnly();
    }
    private (IReadOnlyList<NativePluginPdbMember> Members, IReadOnlyList<NativePluginPdbMethod> Methods)
        FieldList(uint id, HashSet<uint> ancestors)
    {
        if (!ancestors.Add(id)) throw new InvalidDataException("Original PDB field-list continuation has a cycle.");
        var bytes = Type(id, 0x1203); var result = new List<NativePluginPdbMember>();
        var methods = new List<NativePluginPdbMethod>(); var position = 2;
        while (position < bytes.Length)
        {
            if (bytes[position] >= 0xf0)
            {
                var count = bytes[position] & 15;
                if (count == 0 || count > bytes.Length - position) throw new InvalidDataException("Original PDB field padding is invalid.");
                position += count; continue;
            }
            var leaf = U16(bytes, position); position += 2;
            if (leaf == 0x150d)
            {
                _ = U16(bytes, position); position += 2; var type = U32(bytes, position); position += 4;
                var offset = Number(bytes, ref position); var name = Text(bytes, ref position); result.Add(new(name, type, offset));
            }
            else if (leaf == 0x1404)
            {
                if (U16(bytes, position) != 0) throw new InvalidDataException("Original PDB field continuation padding is nonzero.");
                position += 2; var next = U32(bytes, position); position += 4;
                var continuation = FieldList(next, ancestors);
                result.AddRange(continuation.Members); methods.AddRange(continuation.Methods);
            }
            else if (leaf == 0x1511)
            {
                var attributes = U16(bytes, position); position += 2;
                var type = U32(bytes, position); position += 4;
                var methodKind = (attributes >> 2) & 7;
                if (methodKind == 7) throw new InvalidDataException("Original PDB method has a reserved method property.");
                uint? tableOffset = null;
                if (methodKind is 4 or 6) { tableOffset = U32(bytes, position); position += 4; }
                var name = Text(bytes, ref position);
                var signature = Type(type); var signatureLeaf = U16(signature, 0);
                var parameterOffset = signatureLeaf switch
                {
                    0x1008 when signature.Length >= 14 => 8,
                    0x1009 when signature.Length >= 26 => 16,
                    _ => throw new NotSupportedException("Original PDB method has no complete procedure/member-function declaration.")
                };
                var parameters = U16(signature, parameterOffset);
                var arguments = Type(U32(signature, parameterOffset + 2), 0x1201);
                if (arguments.Length < 6 || U32(arguments, 2) != parameters || arguments.Length < 6L + parameters * 4L)
                    throw new InvalidDataException("Original PDB method argument list does not cover its exact declaration.");
                // A method declaration has no instance-data offset. Retain its
                // full field identity separately instead of treating it as a
                // callback slot or discarding it from the class member count.
                methods.Add(new(name, type, attributes, tableOffset));
            }
            else throw new NotSupportedException("Selected PDB callable declaration contains an unowned field-list leaf: " + leaf.ToString("x4"));
        }
        ancestors.Remove(id); return (result.AsReadOnly(), methods.AsReadOnly());
    }
    internal (uint Return, byte Convention, ushort Parameters, IReadOnlyList<uint> Arguments) FunctionPointer(uint id)
    {
        var pointer = Type(id, 0x1002);
        if (pointer.Length < 10 || ((U32(pointer, 6) >> 13) & 0x3f) != 4)
            throw new NotSupportedException("Selected PDB callable member is not a four-byte original pointer.");
        var function = Type(U32(pointer, 2), 0x1008);
        if (function.Length < 14) throw new InvalidDataException("Original PDB procedure signature is incomplete.");
        var count = U16(function, 8); var arguments = Type(U32(function, 10), 0x1201);
        if (arguments.Length < 6 || U32(arguments, 2) != count || arguments.Length < 6L + count * 4L)
            throw new InvalidDataException("Original PDB callable argument list does not cover its exact signature.");
        var values = new uint[count];
        for (var index = 0; index < count; ++index) values[index] = U32(arguments, 6 + index * 4);
        if (function[7] != 0) throw new NotSupportedException("Original procedure options require an independent native ABI owner.");
        return (U32(function, 2), function[6], count, Array.AsReadOnly(values));
    }
    internal string AbiType(uint id)
    {
        var visited = new HashSet<uint>();
        while (id >= 0x1000)
        {
            if (!visited.Add(id)) throw new InvalidDataException("Original callable type alias has a cycle.");
            var bytes = Type(id); var leaf = U16(bytes, 0);
            if (leaf == 0x1001) { id = U32(bytes, 2); continue; }
            if (leaf == 0x1507) { id = U32(bytes, 6); continue; }
            if (leaf == 0x1002 && bytes.Length >= 10 && ((U32(bytes, 6) >> 13) & 0x3f) == 4)
                return "pointer32";
            throw new NotSupportedException("Original callable type lacks its public scalar/pointer ABI owner: " + id.ToString("x"));
        }
        if ((id & 0xf00) == 0x400) return "pointer32";
        return id switch
        {
            0x03 => "void",
            0x30 => "bool8",
            0x20 => "uint8",
            0x22 or 0x75 => "uint32",
            0x41 => "float64",
            _ => throw new NotSupportedException("Original callable scalar ABI is not the declared public utility domain: " + id.ToString("x"))
        };
    }
    internal bool PointerTo(uint id, string name)
    {
        var bytes = Type(id, 0x1002); var target = U32(bytes, 2);
        if (target < 0x1000) return false;
        var structure = Type(target);
        if (U16(structure, 0) is not (0x1504 or 0x1505) || structure.Length < 20) return false;
        var position = 18; _ = Number(structure, ref position); return Text(structure, ref position) == name;
    }
    private byte[] Type(uint id, ushort? leaf = null)
    {
        if (!_types.TryGetValue(id, out var value) || value.Length < 2 || leaf is { } expected && U16(value, 0) != expected)
            throw new NotSupportedException("Original PDB type has no admitted declaration leaf: " + id.ToString("x"));
        return value;
    }
    private static uint Number(byte[] bytes, ref int position)
    {
        var value = U16(bytes, position); position += 2;
        if (value < 0x8000) return value;
        if (value == 0x8002) { var number = U16(bytes, position); position += 2; return number; }
        if (value == 0x8004) { var number = U32(bytes, position); position += 4; return number; }
        throw new NotSupportedException("Original PDB declaration numeric leaf is outside the unsigned extent domain.");
    }
    private static string Text(byte[] bytes, ref int position)
    {
        var end = bytes.AsSpan(position).IndexOf((byte)0);
        if (end < 0) throw new InvalidDataException("Original PDB type name is unterminated.");
        var result = Encoding.UTF8.GetString(bytes.AsSpan(position, end)); position += end + 1; return result;
    }
    private static uint U32(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);
    private static ushort U16(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]);
}
