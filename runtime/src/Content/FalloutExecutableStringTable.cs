using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

// Read owned literals and their compiler-emitted setting associations. Nothing
// from the executable is executed, written back, or retained as a launch file.
internal static partial class FalloutExecutableStringTable
{
    internal static IReadOnlyDictionary<string, string> Read(string path)
    {
        var (code, image) = Load(path);
        var result = ReadInitializers(code, image.Literal, image.IsWritableObject);
        if (result.Count == 0) throw new NotSupportedException("Owned executable setting initializers are unbound.");
        return result;
    }

    internal static IReadOnlyDictionary<string, float> ReadFloatDefaults(string path)
    {
        var (code, image) = Load(path);
        return ReadFloatInitializers(code, image.Literal, image.IsWritableObject,
            address => BitConverter.Int32BitsToSingle(unchecked((int)U32(image.Read(address, 4), 0))));
    }

    internal static IReadOnlyDictionary<string, uint> ReadIntegerDefaults(string path)
    {
        var (code, image) = Load(path);
        var result = ReadIntegerInitializers(code, image.Literal, image.IsWritableObject);
        if (result.Count == 0) throw new NotSupportedException("Owned executable integer setting initializers are unbound.");
        return result;
    }

    internal static IReadOnlyDictionary<string, uint> ReadIntegerInitializers(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, Func<uint, bool> writableObject)
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var at = 0; at <= code.Length - 23; ++at)
        {
            var candidate = code[at..];
            // Immediate DWORD, owned name, global receiver, constructor call.
            // The value is an integer payload, not a pointer to a literal.
            if (!candidate[..4].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x68 }) ||
                candidate[8] != 0x68 || candidate[13] != 0xb9 || candidate[18] != 0xe8 ||
                !writableObject(U32(candidate, 14))) continue;
            var name = literal(U32(candidate, 9));
            if (name is null || !Regex.IsMatch(name, @"^i[A-Z][A-Za-z0-9_]+$", RegexOptions.CultureInvariant)) continue;
            if (!result.TryAdd(name, U32(candidate, 4)))
                throw new InvalidDataException($"Multiple source initializers declare {name}.");
        }
        return result;
    }

    private static (byte[] Code, Image Image) Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var pe = new PEReader(new MemoryStream(bytes, false));
        if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32)
            throw new NotSupportedException("Owned default settings require an admitted Win32 PE layout.");
        var image = new Image(bytes, pe.PEHeaders);
        var codeSections = pe.PEHeaders.SectionHeaders.Where(section => section.Name == ".text").ToArray();
        if (codeSections.Length != 1) throw new InvalidDataException("Owned executable has no unique code section.");
        var section = codeSections[0];
        var code = bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData).ToArray();
        var wrappers = pe.PEHeaders.SectionHeaders.Where(candidate => candidate.Name == ".bind").ToArray();
        if (wrappers.Length > 1) throw new InvalidDataException("Owned executable has ambiguous packed sections.");
        if (wrappers.Length == 1)
            DecodeSection(image, bytes.AsSpan(wrappers[0].PointerToRawData, wrappers[0].SizeOfRawData), section, code);
        return (code, image);
    }

    internal static IReadOnlyDictionary<string, float> ReadFloatInitializers(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, Func<uint, bool> writableObject, Func<uint, float> constant)
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        for (var at = 0; at <= code.Length - 28; ++at)
        {
            var candidate = code[at..];
            // MSVC float-setting initializer: store a referenced Float32 on
            // the argument stack, then pass the name and descriptor to its
            // constructor. Values and identities both belong to the owned PE.
            if (!candidate[..6].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0xd9, 0x05 }) ||
                !candidate.Slice(10, 4).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x68 }) ||
                candidate[18] != 0xb9 || candidate[23] != 0xe8 || !writableObject(U32(candidate, 19))) continue;
            var name = literal(U32(candidate, 14));
            if (name is null || !Regex.IsMatch(name, @"^f[A-Z][A-Za-z0-9_]+(?::[A-Za-z0-9_]+)?$", RegexOptions.CultureInvariant)) continue;
            var value = constant(U32(candidate, 6));
            if (!float.IsFinite(value)) throw new InvalidDataException($"Owned float setting is non-finite: {name}.");
            if (!result.TryAdd(name, value)) throw new InvalidDataException($"Multiple source initializers declare {name}.");
        }
        return result;
    }

    // Admitted MSVC static initializer: frame prologue, two literal arguments,
    // global descriptor receiver, constructor call. The argument relationship
    // handles pooled strings as well as adjacent literals. It is not an index
    // or address list for one executable. Private native descriptor observations
    // verify the name/value order; all addresses come from the selected file.
    internal static IReadOnlyDictionary<string, string> ReadInitializers(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, Func<uint, bool> writableObject)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var at = 0; at <= code.Length - 23; ++at)
        {
            var candidate = code[at..];
            if (!candidate[..4].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x68 }) ||
                candidate[8] != 0x68 || candidate[13] != 0xb9 || candidate[18] != 0xe8)
                continue;
            if (!writableObject(U32(candidate, 14))) continue;
            var name = literal(U32(candidate, 9));
            if (name is null || !Regex.IsMatch(name, @"^s[A-Z][A-Za-z0-9_]+$", RegexOptions.CultureInvariant)) continue;
            var value = literal(U32(candidate, 4));
            if (value is null) continue;
            if (!result.TryAdd(name, value)) throw new InvalidDataException($"Multiple source initializers declare {name}.");
        }
        return result;
    }

    private static void DecodeSection(Image image, ReadOnlySpan<byte> wrapper, SectionHeader section, Span<byte> destination)
    {
        // Admitted Win32 wrapper layout: marked loader, local header address,
        // DWORD extent and rolling-XOR seed. The PE entry point may be an outer
        // environment trampoline, so it is not treated as the resource header.
        ReadOnlySpan<byte> prefix = [0x53, 0x51, 0x52, 0x56, 0x57, 0x55, 0x8b, 0xec, 0x81, 0xec, 0, 0x10, 0, 0, 0xc7, 0x85];
        var start = wrapper.IndexOf(prefix);
        if (start < 4 || U32(wrapper, start - 4) != 0xc0dec0de ||
            wrapper[(start + prefix.Length)..].IndexOf(prefix) >= 0 || wrapper.Length - start < 128)
            throw new NotSupportedException("Owned executable wrapper layout is unbound.");
        var loader = wrapper.Slice(start, 128);
        if (loader[24] != 0x8b || loader[25] != 0xb5 || loader[30] != 0xb9)
            throw new NotSupportedException("Owned wrapper header transport is unbound.");
        var headerSize = checked((int)U32(loader, 31) * 4);
        if (headerSize is < 76 or > 4096 || headerSize == 0xd0 * 4)
            throw new NotSupportedException("Owned wrapper header version is unbound.");
        var seedStore = loader[35..99].IndexOf(new byte[] { 0xc7, 0x85 });
        if (seedStore < 0) throw new InvalidDataException("Owned wrapper has no header seed.");
        var header = image.Read(U32(loader, 20), headerSize);
        var next = DecodeChain(header, U32(loader, 35 + seedStore + 6));
        var payload = image.Read(U32(header, 36), checked((int)U32(header, 40)));
        DecodeChain(payload, next);
        var keyOffset = checked((int)U32(header, 68));
        var key = Enumerable.Range(0, 4).Select(index => U32(payload, keyOffset + index * 4)).ToArray();
        var library = image.Read(U32(payload, checked((int)U32(header, 60))),
            checked((int)U32(payload, checked((int)U32(header, 64)))));
        DecodeBlocks(library, key);
        if (library.Length < 64 || library[0] != 'M' || library[1] != 'Z')
            throw new InvalidDataException("Owned wrapper metadata did not decode to its declared image.");
        var fields = FindPayloadFields(library);
        var sectionAddress = U32(payload, fields[3]);
        var encryptedSize = checked((int)U32(payload, fields[4]));
        if (sectionAddress != image.Base + section.VirtualAddress || encryptedSize <= 0 ||
            encryptedSize > destination.Length || encryptedSize % 16 != 0)
            throw new InvalidDataException("Owned wrapper code extent does not match the PE section.");
        using var aes = Aes.Create();
        aes.Key = payload.AsSpan(fields[5], 32).ToArray();
        var iv = aes.DecryptEcb(payload.AsSpan(fields[6], 16), PaddingMode.None);
        var encrypted = new byte[checked(encryptedSize + 16)];
        payload.AsSpan(fields[7], 16).CopyTo(encrypted);
        image.Read(sectionAddress, encryptedSize).CopyTo(encrypted, 16);
        var decoded = aes.DecryptCbc(encrypted, iv, PaddingMode.None);
        decoded.AsSpan(0, encryptedSize).CopyTo(destination);
    }

    private static int[] FindPayloadFields(ReadOnlySpan<byte> metadata)
    {
        int[]? found = null;
        // Five scalar loads/stores followed by the key and IV address operands.
        // Registers and destinations vary; the source field offsets are data.
        for (var at = 0; at <= metadata.Length - 71; ++at)
        {
            var matches = true;
            for (var index = 0; index < 5; ++index)
                matches &= metadata[at + index * 12] == 0x8b &&
                    (metadata[at + index * 12 + 1] & 0xc7) == 0x80 && metadata[at + index * 12 + 6] == 0x89;
            if (!matches || metadata[at + 60] != 0x8d || metadata[at + 66] != 0x05) continue;
            if (found is not null) throw new InvalidDataException("Owned wrapper field layout is ambiguous.");
            found = new int[8];
            for (var index = 0; index < 5; ++index) found[index] = checked((int)U32(metadata, at + index * 12 + 2));
            found[5] = checked((int)U32(metadata, at + 62));
            found[6] = checked((int)U32(metadata, at + 67));
            found[7] = checked(found[6] + 16);
        }
        return found ?? throw new NotSupportedException("Owned wrapper payload fields are unbound.");
    }

    internal static uint DecodeChain(Span<byte> data, uint previous)
    {
        if (data.Length % 4 != 0) throw new InvalidDataException("Owned XOR extent is not DWORD aligned.");
        for (var at = 0; at < data.Length; at += 4)
        {
            var next = U32(data, at);
            BinaryPrimitives.WriteUInt32LittleEndian(data[at..], next ^ previous);
            previous = next;
        }
        return previous;
    }

    internal static void DecodeBlocks(Span<byte> bytes, IReadOnlyList<uint> key)
    {
        if (bytes.Length % 8 != 0 || key.Count != 4) throw new InvalidDataException("Owned block extent/key is invalid.");
        ulong previous = 0x5555555555555555;
        for (var at = 0; at < bytes.Length; at += 8)
        {
            var cipher = BinaryPrimitives.ReadUInt64LittleEndian(bytes[at..]);
            var low = (uint)cipher; var high = (uint)(cipher >> 32);
            unchecked
            {
                var counter = 0xc6ef3720u;
                do
                {
                    high -= (((low << 4) ^ (low >> 5)) + low) ^ (counter + key[(int)((counter >> 11) & 3)]);
                    counter -= 0x9e3779b9;
                    low -= (((high << 4) ^ (high >> 5)) + high) ^ (counter + key[(int)(counter & 3)]);
                } while (counter != 0);
            }
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[at..], ((ulong)high << 32 | low) ^ previous);
            previous = cipher;
        }
    }

    private static uint U32(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - 4) throw new InvalidDataException("Owned executable field exceeds its byte extent.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    }

    private sealed class Image(byte[] bytes, PEHeaders headers)
    {
        internal uint Base { get; } = checked((uint)headers.PEHeader!.ImageBase);

        internal ReadOnlySpan<byte> ScriptCommandBody(string name, byte[] code)
        {
            var codeBase = checked(Base + (uint)headers.SectionHeaders.Single(section => section.Name == ".text").VirtualAddress);
            uint? execute = null;
            foreach (var section in headers.SectionHeaders.Where(section =>
                         (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0))
            {
                var data = bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData);
                // The Win32 command descriptor declares long/short names,
                // opcode, help, parameter metadata and execute/parse/eval owners.
                for (var at = 0; at <= data.Length - 40; at += 4)
                {
                    var row = data[at..];
                    if (Literal(U32(row, 0)) != name) continue;
                    var handler = U32(row, 24);
                    if (Literal(U32(row, 4)) is null || Literal(U32(row, 12)) is null ||
                        U32(row, 8) is < 0x1000 or > 0xffff || handler < codeBase || handler - codeBase >= code.Length)
                        throw new NotSupportedException("Owned script command descriptor is unbound.");
                    if (execute is not null) throw new InvalidDataException("Owned script command declaration is ambiguous.");
                    execute = handler;
                }
            }
            if (execute is null) throw new NotSupportedException($"Owned script command has no declaration: {name}.");
            var body = code.AsSpan(checked((int)(execute.Value - codeBase)));
            if (!body.StartsWith(new byte[] { 0x55, 0x8b, 0xec }))
                throw new NotSupportedException("Owned script command entry is unbound.");
            var end = body.IndexOf(new byte[] { 0x8b, 0xe5, 0x5d, 0xc3 });
            if (end < 0) throw new NotSupportedException("Owned script command return is unbound.");
            return body[..(end + 4)];
        }

        internal byte[] Read(uint address, int count)
        {
            if (address < Base || count is < 0 or > 64 * 1024 * 1024) throw new InvalidDataException("Owned executable resource extent is invalid.");
            var rva = address - Base;
            foreach (var section in headers.SectionHeaders)
            {
                if (rva < section.VirtualAddress || (ulong)rva + (uint)count > (ulong)section.VirtualAddress + (uint)section.SizeOfRawData) continue;
                var offset = checked((int)(rva - section.VirtualAddress) + section.PointerToRawData);
                if (offset > bytes.Length - count) break;
                return bytes.AsSpan(offset, count).ToArray();
            }
            throw new InvalidDataException("Owned executable resource is not backed by file bytes.");
        }

        internal bool IsWritableObject(uint address) => address >= Base && headers.SectionHeaders.Any(section =>
            (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0 && address - Base >= section.VirtualAddress &&
            (ulong)(address - Base) + 12 <= (ulong)section.VirtualAddress + (uint)section.VirtualSize);

        internal string? Literal(uint address)
        {
            if (address < Base) return null;
            var rva = address - Base;
            foreach (var section in headers.SectionHeaders)
            {
                if (section.Name != ".rdata" || rva < section.VirtualAddress || rva >= section.VirtualAddress + section.SizeOfRawData) continue;
                var count = checked((int)(section.VirtualAddress + section.SizeOfRawData - rva));
                var offset = checked((int)(rva - section.VirtualAddress) + section.PointerToRawData);
                var data = bytes.AsSpan(offset, Math.Min(count, 65536));
                var end = data.IndexOf((byte)0);
                if (end < 0) return null;
                foreach (var character in data[..end])
                    if (character > 126 || character < 32 && character is not (9 or 10 or 13)) return null;
                return Encoding.ASCII.GetString(data[..end]);
            }
            return null;
        }
    }
}
