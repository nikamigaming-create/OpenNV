using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

// Reads and validates original CPU bytes. No texture, shader or converted asset
// is created, and validation does not claim decoded/final pixel equivalence.
internal sealed class FalloutScreenBloodMediaReader(Func<string, (byte[] Payload, string Source)> read)
{
    private const int HeaderBytes = 128;
    private const int HeightOffset = 12, WidthOffset = 16, DepthOffset = 24, MipCountOffset = 28;
    private const int PitchOffset = 20;
    private const int FormatFlagsOffset = 80, FourCcOffset = 84, BitCountOffset = 88, Caps2Offset = 112;
    private const uint FourCcFlag = 4, RgbFlag = 0x40, LuminanceFlag = 0x20000, AlphaFlag = 2;
    private const uint MipCountFlag = 0x20000;
    private const uint PitchFlag = 8;
    private const int BlockSide = 4, Bc1BlockBytes = 8, OtherBcBlockBytes = 16, BitsPerByte = 8;
    private readonly Dictionary<string, FalloutScreenBloodMedia> _media = new(StringComparer.OrdinalIgnoreCase);
    internal int ValidatedCount => _media.Count;

    internal static FalloutScreenBloodMediaReader ForSource(RuntimeLiveContentSource source) => new(path =>
    {
        if (!source.TryRead(path, null, out var payload, out var identity))
            throw new FileNotFoundException("Owned screen-blood texture is missing.", path);
        return (payload, identity);
    });

    internal FalloutScreenBloodMedia Read(string path)
    {
        var key = FalloutBsaArchive.CanonicalPath(path);
        if (_media.TryGetValue(key, out var retained)) return retained;
        var (payload, identity) = read(path);
        if (string.IsNullOrWhiteSpace(identity)) throw new InvalidDataException("Screen-blood media has no source identity.");
        Validate(payload);
        var media = new FalloutScreenBloodMedia(path, identity, Convert.ToHexString(SHA256.HashData(payload)));
        _media.Add(key, media);
        return media;
    }

    internal static void Validate(ReadOnlySpan<byte> payload)
    {
        NativeOwnedMediaFormat.ValidateDds(payload);
        var width = Word(payload, WidthOffset); var height = Word(payload, HeightOffset);
        if (width < 2 || height < 2) throw new InvalidDataException("Owned blood texture cannot contain its source atlas.");
        if (Word(payload, Caps2Offset) != 0 || Word(payload, DepthOffset) > 1)
            throw new NotSupportedException("Screen-blood cube/volume texture has no 2D atlas owner.");
        var count = Word(payload, MipCountOffset);
        if (count == 0) count = 1;
        var maximumLevels = 1;
        for (var extent = Math.Max(width, height); extent > 1; extent /= 2) ++maximumLevels;
        if (count > maximumLevels || count > 1 && (Word(payload, 8) & MipCountFlag) == 0)
            throw new InvalidDataException("Screen-blood DDS mip count differs from its declared dimensions/flags.");
        var flags = Word(payload, FormatFlagsOffset);
        var fourCc = payload.Slice(FourCcOffset, sizeof(uint));
        var blockBytes = (flags & FourCcFlag) == 0 ? 0 : fourCc.SequenceEqual("DXT1"u8) ? Bc1BlockBytes :
            fourCc.SequenceEqual("DXT2"u8) || fourCc.SequenceEqual("DXT3"u8) || fourCc.SequenceEqual("DXT4"u8) ||
            fourCc.SequenceEqual("DXT5"u8) ? OtherBcBlockBytes :
            throw new NotSupportedException("Screen-blood DDS compression has no validated CPU layout owner.");
        var bits = Word(payload, BitCountOffset);
        if (blockBytes == 0 && ((flags & (RgbFlag | LuminanceFlag | AlphaFlag)) == 0 || bits is not (8 or 16 or 24 or 32)))
            throw new NotSupportedException("Screen-blood DDS scalar pixel layout has no validated CPU owner.");
        if (blockBytes == 0 && (Word(payload, 8) & PitchFlag) != 0 && Word(payload, PitchOffset) != (ulong)width * bits / BitsPerByte)
            throw new NotSupportedException("Screen-blood DDS pitched rows have no validated CPU layout owner.");
        ulong expected = HeaderBytes;
        for (var level = 0; level < count; ++level)
        {
            expected = checked(expected + (blockBytes == 0
                ? (ulong)width * height * bits / BitsPerByte
                : ((ulong)width + BlockSide - 1) / BlockSide * (((ulong)height + BlockSide - 1) / BlockSide) * (uint)blockBytes));
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
        if (expected != (ulong)payload.Length)
            throw new InvalidDataException("Screen-blood DDS payload is truncated or has unexplained trailing bytes.");
    }
    private static uint Word(ReadOnlySpan<byte> payload, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
}
