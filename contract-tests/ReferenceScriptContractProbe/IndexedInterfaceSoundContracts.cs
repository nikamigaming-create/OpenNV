using System.Buffers.Binary;
using OpenNV.Runtime.Content;

// Authored transport bytes, not executable game derivatives. This exercises
// the complete source decoder/switch relationship; none of these bytes run.
internal static class IndexedInterfaceSoundContracts
{
    internal static void Run()
    {
        const uint codeBase = 0x10000, selector = codeBase + 256, targets = codeBase + 272;
        var code = new byte[320]; Array.Fill(code, (byte)0xcc);
        var at = 0;
        Put([0x8b, 0x45, 0x08, 0x40, 0x83, 0xf8, 0x03, 0x77]);
        Put([unchecked((byte)(64 - 9))]);
        Put([0x0f, 0xb6, 0x98]); U32(selector);
        Put([0xff, 0x24, 0x9d]); U32(targets);
        code[64] = 0xc3;
        Branch(80, 0x20000); Branch(112, 0x20020); code[160] = 0xc3;
        code[256] = 0; code[257] = 1; code[258] = 1; code[259] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(272), codeBase + 64);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(276), codeBase + 80);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(280), codeBase + 112);
        var names = new Dictionary<uint, string> { [0x20000] = "AuthoredFirst", [0x20020] = "AuthoredSecond" };
        var result = Read(code);
        Require(result.Entries.Count == 4 && result.Resolve(-1).Disposition == FalloutInterfaceSoundDisposition.SourceSilent &&
            result.Resolve(0).EditorId == "AuthoredFirst" && result.Resolve(1).EditorId == "AuthoredFirst" &&
            result.Resolve(2).EditorId == "AuthoredSecond" && result.Resolve(3).Disposition == FalloutInterfaceSoundDisposition.SourceSilent &&
            result.Resolve(int.MinValue).Disposition == FalloutInterfaceSoundDisposition.SourceSilent &&
            result.Resolve(int.MaxValue).Disposition == FalloutInterfaceSoundDisposition.SourceSilent,
            "Source index bias, aliases, named rows and unsigned outside-range silence were lost.");
        var bad = (byte[])code.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(280), codeBase + (uint)bad.Length);
        Refuses(() => Read(bad), "Source table target outside the complete code extent.");
        bad = (byte[])code.Clone(); bad[64] = 0xe8;
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(65), 160 - 69); bad[69] = 0xc3;
        Refuses(() => Read(bad), "A default branch with an unowned source call cannot become silence.");
        bad = (byte[])code.Clone(); bad[112] = 0xff; bad[113] = 0xd0; bad[114] = 0xc3;
        Refuses(() => Read(bad), "An indirect named branch cannot inherit a sibling's successful declaration.");
        names[0x20020] = "unsafe/path";
        Refuses(() => Read(code), "An invalid editor-ID source literal cannot become a menu identity.");
        Console.WriteLine("OPENNV_INDEXED_INTERFACE_CATALOGUE_CONTRACT_PASS sourceTransport=true nativePlayback=unverified");

        FalloutInterfaceSoundCatalogue Read(byte[] bytes) => FalloutExecutableStringTable.ReadInterfaceSoundCatalogue(bytes, codeBase, codeBase,
            new string('a', 64), (pointer, count) =>
            {
                if (pointer < codeBase || (ulong)pointer + (uint)count > (ulong)codeBase + (uint)bytes.Length)
                    throw new InvalidDataException("Authored complete source extent exceeded.");
                return bytes.AsSpan(checked((int)(pointer - codeBase)), count).ToArray();
            }, pointer => names.GetValueOrDefault(pointer), pointer => pointer >= codeBase && pointer - codeBase < bytes.Length);
        void Put(byte[] bytes) { bytes.CopyTo(code, at); at += bytes.Length; }
        void U32(uint value) { BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at), value); at += 4; }
        void Branch(int start, uint literal)
        {
            at = start; Put([0x68]); U32(0x121); Put([0x68]); U32(literal); Put([0xe8]);
            BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(at), 160 - (at + 4)); at += 4; Put([0xc3]);
        }
    }
    private static void Refuses(Action action, string reason)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Indexed source decoder accepted: " + reason);
    }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); }
}
