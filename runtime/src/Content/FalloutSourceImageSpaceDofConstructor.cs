using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

// Parameter cells are in native declaration order: strength, near/far enables,
// fourth lane, interval lower/upper, focus, render maximum depth, then the
// separate strength/first/second/fourth parameter vector. All are Float32 bits.
internal sealed record FalloutSourceImageSpaceDofConstructor(uint Mode, uint FocusRange, uint[] Parameters,
    string Setting, uint SettingValue, string Source)
{
    internal void Validate()
    {
        if (Parameters.Length != 12 || Setting != "fRenderDepthMaxDepth:Imagespace" || Parameters[7] != SettingValue ||
            Source.Length != 64 || !Source.All(Uri.IsHexDigit))
            throw new InvalidDataException("DOF receiver constructor lost its actual source fields/INI writer.");
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutSourceImageSpaceDofConstructor ReadStandaloneImageSpaceDofConstructor(string path,
        FalloutInstallationSettings settings)
    {
        using (var stream = File.OpenRead(path))
            if (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != FalloutSourceMainFamily.Fallout3)
                throw new NotSupportedException("DOF constructor is not the selected standalone source.");
        var (code, image) = Load(path);
        var candidates = new List<FalloutSourceImageSpaceDofConstructor>();
        for (var at = 0; at <= code.Length - 72; at++)
        {
            var body = code.AsSpan(at, 72);
            if (!body[..4].SequenceEqual(new byte[] { 0xf3, 0x0f, 0x10, 5 }) ||
                !body.Slice(8, 10).SequenceEqual(new byte[] { 0x8b, 0xc6, 0xf3, 0x0f, 0x11, 0x45, 0xec, 0xc7, 0x45, 0xe0 }) ||
                !body.Slice(22, 3).SequenceEqual(new byte[] { 0xc7, 0x45, 0xe4 }) ||
                !body.Slice(29, 3).SequenceEqual(new byte[] { 0xc7, 0x45, 0xe8 }) ||
                !body.Slice(36, 10).SequenceEqual(new byte[] { 0x0f, 0x10, 0x45, 0xe0, 0xc7, 0x86, 0x9c, 0, 0, 0 }) ||
                !body.Slice(50, 3).SequenceEqual(new byte[] { 0xc7, 0x46, 0x58 }) ||
                !body.Slice(57, 7).SequenceEqual(new byte[] { 0x0f, 0x11, 0x46, 0x7c, 0x0f, 0x28, 5 }) ||
                !body.Slice(68, 4).SequenceEqual(new byte[] { 0x0f, 0x11, 0x46, 0x6c })) continue;
            var zero = new List<byte>();
            for (var offset = 0x5c; offset <= 0x98; offset += 4)
            {
                zero.Add(0xc7); zero.Add(offset < 0x80 ? (byte)0x46 : (byte)0x86);
                if (offset < 0x80) zero.Add((byte)offset); else zero.AddRange(BitConverter.GetBytes(offset));
                zero.AddRange(new byte[4]);
            }
            var start = Math.Max(0, at - 300);
            if (code.AsSpan(start, at - start).IndexOf(zero.ToArray()) < 0)
                throw new NotSupportedException("DOF constructor omitted its actual zero field stores.");
            var parameters = new uint[12]; var literal = image.Read(U32(body, 64), 16);
            for (var index = 0; index < 4; index++) parameters[index] = BinaryPrimitives.ReadUInt32LittleEndian(literal.AsSpan(index * 4));
            parameters[4] = U32(body, 18); parameters[5] = U32(body, 25); parameters[6] = U32(body, 32);
            var declared = settings.NumericIni.Find(FalloutIniCollection.Main, "fRenderDepthMaxDepth:Imagespace") ??
                throw new NotSupportedException("DOF constructor has no actual registered render-depth INI setting.");
            if (declared.Number is not { } number || declared.Declaration.Kind != 'f' || declared.Declaration.Payload != U32(image.Read(U32(body, 4), 4), 0))
                throw new InvalidDataException("DOF setting differs from its actual selected loader cell.");
            parameters[7] = BitConverter.SingleToUInt32Bits((float)number);
            var declaration = (uint[])parameters.Clone(); declaration[7] = declared.Declaration.Payload;
            var raw = declaration.SelectMany(BitConverter.GetBytes).Concat(BitConverter.GetBytes(U32(body, 46)))
                .Concat(BitConverter.GetBytes(U32(body, 53))).ToArray();
            var value = new FalloutSourceImageSpaceDofConstructor(U32(body, 53), U32(body, 46), parameters,
                declared.Declaration.Name, parameters[7], Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant());
            value.Validate(); candidates.Add(value);
        }
        return candidates.Count == 1 ? candidates[0] : throw new NotSupportedException("DOF receiver constructor is absent or ambiguous.");
    }
}
