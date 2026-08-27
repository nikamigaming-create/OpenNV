using System.Buffers.Binary;

namespace OpenNV.Runtime;

internal sealed class FaceGenLipAnimation
{
    private readonly FaceGenLipConfiguration _configuration;
    private readonly float[][] _frames;

    private FaceGenLipAnimation(
        FaceGenLipConfiguration configuration,
        uint version,
        uint storedSize,
        uint flags,
        uint frameCount,
        int startFrame,
        uint metadataWord,
        float[][] frames)
    {
        _configuration = configuration;
        Version = version;
        StoredSize = storedSize;
        Flags = flags;
        FrameCount = frameCount;
        StartFrame = startFrame;
        MetadataWord = metadataWord;
        _frames = frames;
    }

    internal uint Version { get; }
    internal uint StoredSize { get; }
    internal uint Flags { get; }
    internal uint FrameCount { get; }
    internal int StartFrame { get; }
    internal uint MetadataWord { get; }
    internal IReadOnlyList<string> TargetNames => _configuration.TargetNames;

    internal static FaceGenLipAnimation Load(
        string path,
        FaceGenLipConfiguration configuration)
    {
        if (configuration.IntegerBytes != sizeof(uint) ||
            configuration.ValueBytes != sizeof(float) ||
            configuration.RunLengthBytes != sizeof(ushort))
            throw new InvalidOperationException(
                "Configured FaceGen LIP scalar widths are unsupported.");
        var source = new ByteCursor(File.ReadAllBytes(path));
        var header = configuration.FileHeaderFields.ToDictionary(
            name => name,
            name => source.ReadUInt32(configuration.IntegerBytes, $"LIP {name}"),
            StringComparer.Ordinal);
        var version = header["version"];
        var storedSize = header["storedSize"];
        var flags = header["flags"];
        if (version != configuration.Version)
            throw new InvalidOperationException($"Unsupported FaceGen LIP version: {version}.");
        if ((flags & configuration.BigEndianFlag) != 0)
            throw new InvalidOperationException("Big-endian FaceGen LIP is unsupported.");
        var supportedFlags = configuration.CompressedFlag | configuration.BigEndianFlag;
        if ((flags & ~supportedFlags) != 0)
            throw new InvalidOperationException($"FaceGen LIP flags are unsupported: {flags:x}.");
        if (storedSize < configuration.StoredSizeBiasBytes ||
            storedSize > configuration.MaximumDecodedBytes)
            throw new InvalidOperationException($"FaceGen LIP stored size is invalid: {storedSize}.");
        var expectedDecodedSize = checked((int)storedSize - configuration.StoredSizeBiasBytes);
        byte[] decoded;
        if ((flags & configuration.CompressedFlag) != 0)
        {
            decoded = DecodeZeroRuns(source, expectedDecodedSize, configuration);
        }
        else
        {
            var marker = source.ReadByte("LIP uncompressed marker");
            if (marker != configuration.UncompressedMarker)
                throw new InvalidOperationException("FaceGen LIP uncompressed marker is invalid.");
            decoded = source.ReadBytes(source.Remaining, "LIP uncompressed payload");
            if (decoded.Length != expectedDecodedSize)
                throw new InvalidOperationException(
                    "FaceGen LIP uncompressed payload size differs.");
        }
        if (source.Remaining != 0)
            throw new InvalidOperationException("FaceGen LIP contains unread source bytes.");

        var decodedHeaderBytes = checked(
            configuration.DecodedHeaderFields.Length * configuration.IntegerBytes);
        if (decoded.Length < decodedHeaderBytes)
            throw new InvalidOperationException("FaceGen LIP decoded header is truncated.");
        var decodedSource = new ByteCursor(decoded);
        var decodedHeader = new Dictionary<string, uint>(StringComparer.Ordinal);
        var startFrame = 0;
        foreach (var name in configuration.DecodedHeaderFields)
        {
            if (name == "startFrame")
                startFrame = decodedSource.ReadInt32(configuration.IntegerBytes, $"LIP {name}");
            else
                decodedHeader.Add(
                    name,
                    decodedSource.ReadUInt32(configuration.IntegerBytes, $"LIP {name}"));
        }
        var frameCount = decodedHeader["frameCount"];
        if (frameCount == 0 || frameCount > configuration.MaximumFrames)
            throw new InvalidOperationException($"FaceGen LIP frame count is invalid: {frameCount}.");
        var targetValueBytes = checked(
            (int)frameCount * configuration.TargetNames.Length * configuration.ValueBytes);
        var requiredSize = checked(decodedHeaderBytes + targetValueBytes);
        var omittedBytes = requiredSize - decoded.Length;
        if (omittedBytes != 0 && omittedBytes != configuration.ImplicitTrailingZeroBytes)
            throw new InvalidOperationException(
                "FaceGen LIP decoded target payload size differs: " +
                $"required={requiredSize} actual={decoded.Length}.");
        if (omittedBytes > 0)
        {
            Array.Resize(ref decoded, requiredSize);
            decodedSource = new ByteCursor(decoded);
            decodedSource.ReadBytes(decodedHeaderBytes, "LIP decoded header");
        }

        var frames = new float[frameCount][];
        for (var frame = 0; frame < frames.Length; frame++)
        {
            var values = new float[configuration.TargetNames.Length];
            for (var target = 0; target < values.Length; target++)
            {
                var bits = decodedSource.ReadUInt32(configuration.ValueBytes, "LIP target");
                var value = BitConverter.Int32BitsToSingle(unchecked((int)bits));
                if (!float.IsFinite(value) || MathF.Abs(value) > configuration.MaximumAbsoluteWeight)
                    throw new InvalidOperationException("FaceGen LIP target weight is invalid.");
                values[target] = value;
            }
            frames[frame] = values;
        }
        if (decodedSource.Remaining != 0)
            throw new InvalidOperationException("FaceGen LIP contains trailing decoded bytes.");
        return new FaceGenLipAnimation(
            configuration,
            version,
            storedSize,
            flags,
            frameCount,
            startFrame,
            decodedHeader["metadataWord"],
            frames);
    }

    internal void Sample(double seconds, float[] output)
    {
        if (output.Length != _configuration.TargetNames.Length)
            throw new InvalidOperationException("FaceGen LIP sample width differs from its contract.");
        Array.Clear(output);
        if (!double.IsFinite(seconds) || _frames.Length == 0)
            return;
        var position = seconds * _configuration.SampleRateHz - StartFrame;
        var maximumPosition = _frames.Length - 1;
        if (position < 0.0 || position > maximumPosition)
            return;
        var lower = (int)position;
        var upper = Math.Min(lower + 1, maximumPosition);
        var factor = (float)(position - lower);
        for (var target = 0; target < output.Length; target++)
        {
            var first = _frames[lower][target];
            output[target] = first + (_frames[upper][target] - first) * factor;
        }
    }

    private static byte[] DecodeZeroRuns(
        ByteCursor source,
        int expectedSize,
        FaceGenLipConfiguration configuration)
    {
        var result = new byte[expectedSize];
        var output = 0;
        while (source.Remaining > 0)
        {
            var value = source.ReadByte("LIP compressed byte");
            if (value != configuration.RunMarker)
            {
                if (output >= result.Length)
                    throw new InvalidOperationException(
                        "FaceGen LIP compressed payload exceeds its declared size.");
                result[output++] = value;
                continue;
            }
            var count = source.ReadUInt16(
                configuration.RunLengthBytes,
                "LIP zero-run length");
            if (count == 0 || output + count > result.Length)
                throw new InvalidOperationException("FaceGen LIP contains an invalid zero run.");
            output += count;
        }
        if (output != result.Length)
            throw new InvalidOperationException(
                "FaceGen LIP compressed payload is truncated: " +
                $"expected={result.Length} actual={output}.");
        return result;
    }

    private sealed class ByteCursor(byte[] payload)
    {
        private int _offset;

        internal int Remaining => payload.Length - _offset;

        internal byte ReadByte(string label) => ReadBytes(sizeof(byte), label)[0];

        internal ushort ReadUInt16(int width, string label)
        {
            if (width != sizeof(ushort))
                throw new InvalidOperationException("FaceGen integer width is unsupported.");
            return BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(width, label));
        }

        internal uint ReadUInt32(int width, string label)
        {
            if (width != sizeof(uint))
                throw new InvalidOperationException("FaceGen integer width is unsupported.");
            return BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(width, label));
        }

        internal int ReadInt32(int width, string label)
        {
            if (width != sizeof(int))
                throw new InvalidOperationException("FaceGen integer width is unsupported.");
            return BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(width, label));
        }

        internal byte[] ReadBytes(int count, string label)
        {
            if (count < 0 || count > Remaining)
                throw new InvalidOperationException($"FaceGen {label} is truncated.");
            var result = payload.AsSpan(_offset, count).ToArray();
            _offset += count;
            return result;
        }
    }
}
