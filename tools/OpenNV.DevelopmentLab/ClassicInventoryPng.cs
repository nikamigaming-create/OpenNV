using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using OpenNV.Runtime.Content;

/// <summary>Private lossless reference export of already decoded indexed art, never a runtime input.</summary>
internal static class ClassicInventoryPng
{
    internal static void Write(string path, Fallout1NativeFrmFrame frame, byte[] palette)
    {
        if (palette.Length < 768) throw new InvalidDataException("Source palette is truncated.");
        using var file = File.Create(path); file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] data)
        {
            Span<byte> size = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, data.Length); file.Write(size);
            var kind = Encoding.ASCII.GetBytes(name); file.Write(kind); file.Write(data);
            var crc = 0xffffffffU;
            foreach (var value in kind.Concat(data))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320U);
            }
            BinaryPrimitives.WriteUInt32BigEndian(size, crc ^ 0xffffffffU); file.Write(size);
        }
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), frame.Height); header[8] = 8; header[9] = 3;
        Chunk("IHDR", header); Chunk("PLTE", palette.Take(768).Select(value => (byte)Math.Min(255, value * 4)).ToArray());
        Chunk("tRNS", [0]);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for (var y = 0; y < frame.Height; y++)
            { zlib.WriteByte(0); zlib.Write(frame.PaletteIndexes.AsSpan(y * frame.Width, frame.Width)); }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
    }
}
