using System.Security.Cryptography;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed record RenderTraceBlob(string Sha256, int Length, string File);

// Private evidence only. Immutable, hash-addressed bytes let the inspector show
// exact buffers without putting binary payloads into the live state JSON.
internal sealed class RenderTraceBlobStore
{
    private readonly string _directory;

    internal RenderTraceBlobStore(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Trace directory must be absolute.");
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    internal RenderTraceBlob Put(ReadOnlySpan<byte> bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var path = Path.Combine(_directory, hash + ".bin");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes.ToArray());
        else if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("Trace blob changed or its hash collided.");
        return new(hash, bytes.Length, path);
    }

}
