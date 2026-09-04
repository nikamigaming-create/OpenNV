using System.Security.Cryptography;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed class ParityTraceWriter : IDisposable
{
    private static readonly byte[] Magic = "ONVPTR01"u8.ToArray();
    private const int Version = 1;
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;

    internal ParityTraceWriter(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw new IOException($"Refusing to overwrite parity trace: {fullPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            1024 * 1024,
            FileOptions.WriteThrough);
        _writer = new BinaryWriter(_stream);
        _writer.Write(Magic);
        _writer.Write(Version);
        _writer.Flush();
    }

    internal void Append(ReadOnlySpan<byte> packet)
    {
        if (packet.Length == 0)
            throw new InvalidDataException("Parity trace packet is empty.");
        var bytes = packet.ToArray();
        _writer.Write(bytes.Length);
        _writer.Write(SHA256.HashData(bytes));
        _writer.Write(bytes);
        _writer.Flush();
        _stream.Flush(flushToDisk: true);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _stream.Dispose();
    }
}

internal static class ParityTraceReader
{
    private static readonly byte[] Magic = "ONVPTR01"u8.ToArray();
    private const int Version = 1;
    private const int MaximumPacketBytes = 64 * 1024 * 1024;

    internal static IReadOnlyList<byte[]> ReadAll(string path)
    {
        using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var reader = new BinaryReader(stream);
        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic) ||
            reader.ReadInt32() != Version)
            throw new InvalidDataException("Parity trace header is unsupported.");
        var packets = new List<byte[]>();
        while (stream.Position < stream.Length)
        {
            var length = reader.ReadInt32();
            if (length <= 0 || length > MaximumPacketBytes)
                throw new InvalidDataException("Parity trace packet length is invalid.");
            var expectedHash = reader.ReadBytes(32);
            var packet = reader.ReadBytes(length);
            if (packet.Length != length ||
                !CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(packet)))
                throw new InvalidDataException("Parity trace packet is truncated or hash-invalid.");
            _ = ParityTelemetryCodec.Decode(packet);
            packets.Add(packet);
        }
        return packets;
    }
}
