using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Content;

internal static class ClassicAudioProbe
{
    internal static int Run(string install, string campaign, string[] selected)
    {
        using IFalloutClassicOwnedSource source = campaign switch
        {
            "fallout-1" => Fallout1OwnedContentSource.LoadInstall(install),
            "fallout-2" => Fo2NativeOwnedSource.LoadInstall(install),
            _ => throw new ArgumentException("Select fallout-1 or fallout-2.")
        };
        var paths = selected.Length == 0 ? source.EffectiveLogicalPaths("sound", ".acm").ToArray() : selected;
        if (paths.Length == 0) throw new InvalidDataException("No owned ACM resources were selected.");
        var failures = 0; long samples = 0;
        foreach (var path in paths)
        {
            try
            {
                var bytes = source.Read(path, out _); var sourceHash = SHA256.HashData(bytes);
                var first = Decode(bytes, 4096); var chunked = Decode(bytes, 137);
                if (first != chunked || !SHA256.HashData(bytes).SequenceEqual(sourceHash))
                    throw new InvalidDataException("ACM streaming or owned input identity changed.");
                samples += first.Header.Samples;
                Console.WriteLine(JsonSerializer.Serialize(new { path, first.Header, first.PcmSha256, first.ZeroPaddingBits, sourceUnchanged = true, streaming = true, playback = false }));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { failures++; Console.WriteLine(JsonSerializer.Serialize(new { path, failure = error.Message })); }
        }
        Console.WriteLine($"Classic audio decode: campaign={campaign} resources={paths.Length} samples={samples} failures={failures}; component audit, not playback acceptance.");
        return failures == 0 ? 0 : 1;
    }

    private sealed record Result(ClassicAcmHeader Header, string PcmSha256, long ZeroPaddingBits);
    private static Result Decode(byte[] source, int chunk)
    {
        var decoder = new ClassicAcmDecoder(source);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new short[chunk]; var pcm = new byte[chunk * 2]; var total = 0;
        for (var count = decoder.Read(buffer); count != 0; count = decoder.Read(buffer))
        {
            for (var index = 0; index < count; index++) BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(index * 2), buffer[index]);
            hash.AppendData(pcm.AsSpan(0, count * 2)); total = checked(total + count);
        }
        if (total != decoder.Header.Samples) throw new InvalidDataException("ACM did not produce its declared sample count.");
        return new(decoder.Header, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), decoder.ZeroPaddingBits);
    }
}
