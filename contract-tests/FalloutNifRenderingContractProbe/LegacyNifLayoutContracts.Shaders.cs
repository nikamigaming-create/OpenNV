using System.Security.Cryptography;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class LegacyNifLayoutContracts
{
    private static void RunShaderLayouts()
    {
        foreach (var version in new uint[] { 14, 21, 26, 30, 31, 32, 33, 34 })
        {
            (string, byte[])[] blocks = [("BSShaderPPLightingProperty", Lighting(version)),
                ("NiMaterialProperty", Material(version)),
                ("NiBinaryExtraData", Bytes(w => { w.Write(0); w.Write(3U); w.Write(new byte[] { 4, 5, 6 }); }))];
            var bytes = File(version, blocks);
            var originalHash = SHA256.HashData(bytes);
            var ranges = new List<FalloutNifReadRange>();
            var nif = FalloutNifFile.Read(bytes, ranges.Add);
            Require(nif.FileVersion == FalloutNifFile.Version && nif.UserVersion2 == version,
                "Source file and stream identities were changed by header admission.");
            ranges.Clear();
            var shader = (FalloutNifShaderProperty)nif.ReadObject(0);
            Require(ranges.Sum(range => range.Length) == blocks[0].Item2.Length &&
                ranges.First().Offset == nif.Blocks[0].Offset &&
                ranges.Zip(ranges.Skip(1)).All(pair => pair.First.Offset + pair.First.Length == pair.Second.Offset),
                "Conditional shader decode skipped or duplicated source fields.");
            Require(shader.Name == "fixture" && shader.Smooth == 1 && shader.ShaderType == 1 &&
                shader.ShaderFlags == 0x82000000 && shader.ShaderFlags2 == 1 && shader.EnvironmentMapScale == .5f &&
                shader.TextureClampMode == 3 && shader.TextureSet == -1 &&
                shader.RefractionStrength == (version > 14 ? .25f : 0) &&
                shader.RefractionFirePeriod == (version > 14 ? 17 : 0) &&
                shader.UnknownFloat4 == (version > 24 ? 7 : 4) &&
                shader.UnknownFloat5 == (version > 24 ? .125f : 1),
                "Versioned shader fields or absent-field defaults differ.");
            var sibling = (FalloutNifMaterialProperty)nif.ReadObject(1);
            Require(sibling.Specular == new FalloutNifColor3(.7f, .8f, .9f) &&
                sibling.EmissiveMultiple == (version > 21 ? 2.5f : 1) &&
                ReferenceEquals(nif.ReadObject(0), shader),
                "A shader field gate changed a sibling declaration or immutable reuse.");
            Reject(() => nif.ReadObject(2));
            Require(originalHash.SequenceEqual(SHA256.HashData(bytes)), "NIF reader changed original source bytes.");
            foreach (var malformed in new[] { blocks[0].Item2[..^1], blocks[0].Item2.Concat(new byte[] { 0 }).ToArray() })
            {
                var changed = blocks.ToArray(); changed[0] = ("BSShaderPPLightingProperty", malformed);
                Reject(() => FalloutNifFile.Read(File(version, changed)).ReadObject(0));
            }
            var wrongGate = blocks.ToArray();
            wrongGate[0] = ("BSShaderPPLightingProperty", Lighting(version <= 14 ? 26U : 14U));
            Reject(() => FalloutNifFile.Read(File(version, wrongGate)).ReadObject(0));
        }
        var laterFamily = File(34, [("BSLightingShaderProperty", Bytes(w => w.Write(0U)))]);
        var userOffset = "Gamebryo File Format, Version 20.2.0.7\n".Length + sizeof(uint) + sizeof(byte);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(laterFamily.AsSpan(userOffset), 12);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(laterFamily.AsSpan(userOffset + 8), 83);
        Reject(() => FalloutNifFile.Read(laterFamily));
        Console.WriteLine("OPENNV_NIF_SHADER_LAYOUT_CONTRACT_PASS streams=14,21,26,30,31,32,33,34 refractionGate=true parallaxGate=true exactExtents=true sourceImmutable=true unknownBinaryBlockRefused=true laterFamilyRefused=true");
    }

    private static byte[] Lighting(uint version) => Bytes(w =>
    {
        Net(w); w.Write((ushort)1); w.Write(1U); w.Write(0x82000000U); w.Write(1U);
        w.Write(.5f); w.Write(3U); w.Write(-1);
        if (version > 14) { w.Write(.25f); w.Write(17); }
        if (version > 24) { w.Write(7f); w.Write(.125f); }
    });
}
