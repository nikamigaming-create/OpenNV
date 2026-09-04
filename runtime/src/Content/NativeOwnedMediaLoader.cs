using System.Buffers.Binary;
using Godot;

namespace OpenNV.Runtime.Content;

internal static class NativeOwnedMediaLoader
{
    private const string WavExtension = ".wav";
    private const string OggExtension = ".ogg";
    private const int AsciiDigitThree = 0x33;
    private static readonly string Mp3Extension = string.Concat(
        ".mp", char.ConvertFromUtf32(AsciiDigitThree));

    internal static ImageTexture LoadTexture(
        string logicalPath,
        string? preferredArchive = null)
    {
        var payload = Read(logicalPath, preferredArchive, out var source);
        NativeOwnedMediaFormat.ValidateDds(payload);
        var image = new Image();
        var result = image.LoadDdsFromBuffer(payload);
        if (result != Error.Ok || image.IsEmpty())
            throw new InvalidDataException(
                $"Godot rejected owned DDS data from {source}: {result}");
        return ImageTexture.CreateFromImage(image) ??
            throw new InvalidDataException(
                $"Godot could not create a texture from owned DDS data: {source}");
    }

    internal static AudioStream LoadAudio(
        string logicalPath,
        string? preferredArchive = null)
    {
        var payload = Read(logicalPath, preferredArchive, out var source);
        var extension = Path.GetExtension(logicalPath).ToLowerInvariant();
        AudioStream? stream;
        if (extension == WavExtension)
            stream = LoadWav(payload);
        else if (extension == Mp3Extension)
            stream = LoadMp3(payload);
        else if (extension == OggExtension)
            stream = LoadOgg(payload);
        else
        {
            throw new InvalidDataException(
                $"Unsupported owned audio extension: {logicalPath}");
        }
        return stream ?? throw new InvalidDataException(
            $"Godot rejected owned audio data from {source}");
    }

    private static AudioStreamWav? LoadWav(byte[] payload)
    {
        NativeOwnedMediaFormat.ValidateWav(payload);
        return AudioStreamWav.LoadFromBuffer(
            payload, new Godot.Collections.Dictionary());
    }

    private static AudioStreamMP3? LoadMp3(byte[] payload)
    {
        NativeOwnedMediaFormat.ValidateMp3(payload);
        return AudioStreamMP3.LoadFromBuffer(payload);
    }

    private static AudioStreamOggVorbis? LoadOgg(byte[] payload)
    {
        NativeOwnedMediaFormat.ValidateOgg(payload);
        return AudioStreamOggVorbis.LoadFromBuffer(payload);
    }

    private static byte[] Read(
        string logicalPath,
        string? preferredArchive,
        out string source)
    {
        var owned = RuntimeOwnedContentSource.Current ??
            throw new InvalidOperationException(
                "Native owned-data media loading requires a configured source stack.");
        if (!owned.TryRead(logicalPath, preferredArchive, out var payload, out source))
            throw new FileNotFoundException(
                $"Owned media resource is missing: {logicalPath}", logicalPath);
        return payload;
    }
}

internal static class NativeOwnedMediaFormat
{
    private const int DdsBaseHeaderBytes = 128;
    private const int DdsDx10HeaderBytes = 148;
    private const int DdsHeaderSizeOffset = 4;
    private const int DdsExpectedHeaderSize = 124;
    private const int DdsHeightOffset = 12;
    private const int DdsWidthOffset = 16;
    private const int DdsPixelFormatSizeOffset = 76;
    private const int DdsExpectedPixelFormatSize = 32;
    private const int DdsPixelFormatFlagsOffset = 80;
    private const int DdsFourCcOffset = 84;
    private const int FourCcBytes = 4;
    private const int RiffBaseHeaderBytes = 12;
    private const int RiffSizeOffset = 4;
    private const int RiffTypeOffset = 8;
    private const int RiffChunkHeaderBytes = 8;
    private const int RiffChunkSizeOffset = 4;
    private const int WaveMinimumFormatBytes = 16;
    private const int Id3HeaderBytes = 10;
    private const int Id3MagicBytes = 3;
    private const int Id3FlagsOffset = 5;
    private const int Id3SizeOffset = 6;
    private const int Id3FooterBytes = 10;
    private const int SynchsafeHighShift = 21;
    private const int SynchsafeMiddleShift = 14;
    private const int SynchsafeLowShift = 7;
    private const int MpegHeaderBytes = 4;
    private const int OggMinimumHeaderBytes = 27;
    private const int OggHeaderTypeOffset = 5;
    private const int OggSegmentCountOffset = 26;
    private const int VorbisIdentificationBytes = 7;

    internal static void ValidateDds(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < DdsBaseHeaderBytes || !payload[..FourCcBytes].SequenceEqual("DDS "u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload[DdsHeaderSizeOffset..]) != DdsExpectedHeaderSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload[DdsPixelFormatSizeOffset..]) != DdsExpectedPixelFormatSize)
            throw new InvalidDataException("DDS header is invalid or truncated.");
        var height = BinaryPrimitives.ReadUInt32LittleEndian(payload[DdsHeightOffset..]);
        var width = BinaryPrimitives.ReadUInt32LittleEndian(payload[DdsWidthOffset..]);
        if (width == 0 || height == 0)
            throw new InvalidDataException("DDS dimensions must be nonzero.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(payload[DdsPixelFormatFlagsOffset..]) == 0)
            throw new InvalidDataException("DDS pixel format has no declared flags.");
        var fourCc = payload.Slice(DdsFourCcOffset, FourCcBytes);
        var headerBytes = fourCc.SequenceEqual("DX10"u8) ? DdsDx10HeaderBytes : DdsBaseHeaderBytes;
        if (payload.Length <= headerBytes)
            throw new InvalidDataException(
                fourCc.SequenceEqual("DX10"u8)
                    ? "DDS DX10 header or image payload is truncated."
                    : "DDS image payload is empty.");
    }

    internal static void ValidateWav(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < RiffBaseHeaderBytes || !payload[..FourCcBytes].SequenceEqual("RIFF"u8) ||
            !payload.Slice(RiffTypeOffset, FourCcBytes).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("WAV RIFF header is invalid or truncated.");
        var riffBytes = BinaryPrimitives.ReadUInt32LittleEndian(payload[RiffSizeOffset..]);
        if ((ulong)riffBytes + RiffChunkHeaderBytes != (ulong)payload.Length)
            throw new InvalidDataException("WAV RIFF size differs from the resource.");
        var offset = RiffBaseHeaderBytes;
        var hasFormat = false;
        var hasData = false;
        while (offset < payload.Length)
        {
            if (payload.Length - offset < RiffChunkHeaderBytes)
                throw new InvalidDataException("WAV chunk header is truncated.");
            var chunkId = payload.Slice(offset, FourCcBytes);
            var chunkBytes = BinaryPrimitives.ReadUInt32LittleEndian(
                payload[(offset + RiffChunkSizeOffset)..]);
            var next = checked(
                (ulong)offset + RiffChunkHeaderBytes + chunkBytes + (chunkBytes & 1U));
            if (next > (ulong)payload.Length)
                throw new InvalidDataException("WAV chunk exceeds the resource.");
            hasFormat |= chunkId.SequenceEqual("fmt "u8) && chunkBytes >= WaveMinimumFormatBytes;
            hasData |= chunkId.SequenceEqual("data"u8) && chunkBytes > 0;
            offset = checked((int)next);
        }
        if (!hasFormat || !hasData)
            throw new InvalidDataException("WAV requires valid fmt and nonempty data chunks.");
    }

    internal static void ValidateMp3(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < MpegHeaderBytes)
            throw new InvalidDataException("MP3 resource is truncated.");
        var frameOffset = 0;
        if (payload[..Id3MagicBytes].SequenceEqual("ID3"u8))
        {
            if (payload.Length < Id3HeaderBytes || payload[Id3SizeOffset] >= 0x80 ||
                payload[Id3SizeOffset + 1] >= 0x80 || payload[Id3SizeOffset + 2] >= 0x80 ||
                payload[Id3SizeOffset + 3] >= 0x80)
                throw new InvalidDataException("MP3 ID3 header is invalid or truncated.");
            var tagBytes = payload[Id3SizeOffset] << SynchsafeHighShift |
                payload[Id3SizeOffset + 1] << SynchsafeMiddleShift |
                payload[Id3SizeOffset + 2] << SynchsafeLowShift |
                payload[Id3SizeOffset + 3];
            frameOffset = checked(Id3HeaderBytes + tagBytes +
                ((payload[Id3FlagsOffset] & 0x10) != 0 ? Id3FooterBytes : 0));
        }
        if (payload.Length - frameOffset < MpegHeaderBytes || payload[frameOffset] != 0xff ||
            (payload[frameOffset + 1] & 0xe0) != 0xe0 ||
            (payload[frameOffset + 1] & 0x18) == 0x08 ||
            (payload[frameOffset + 1] & 0x06) == 0 ||
            (payload[frameOffset + 2] & 0xf0) is 0 or 0xf0 ||
            (payload[frameOffset + 2] & 0x0c) == 0x0c)
            throw new InvalidDataException("MP3 resource has no valid first MPEG audio frame.");
    }

    internal static void ValidateOgg(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < OggMinimumHeaderBytes ||
            !payload[..FourCcBytes].SequenceEqual("OggS"u8) ||
            payload[FourCcBytes] != 0)
            throw new InvalidDataException("Ogg resource header is invalid or truncated.");
        var segmentCount = payload[OggSegmentCountOffset];
        if ((payload[OggHeaderTypeOffset] & 0x02) == 0 ||
            payload.Length < OggMinimumHeaderBytes + segmentCount)
            throw new InvalidDataException("Ogg segment table is truncated.");
        var bodyBytes = 0;
        for (var index = 0; index < segmentCount; ++index)
            bodyBytes = checked(bodyBytes + payload[OggMinimumHeaderBytes + index]);
        var bodyOffset = OggMinimumHeaderBytes + segmentCount;
        if (payload.Length - bodyOffset < bodyBytes || bodyBytes < VorbisIdentificationBytes ||
            payload[bodyOffset] != 1 ||
            !payload.Slice(bodyOffset + 1, VorbisIdentificationBytes - 1).SequenceEqual("vorbis"u8))
            throw new InvalidDataException(
                "Ogg resource does not begin with a complete Vorbis identification packet.");
    }
}
