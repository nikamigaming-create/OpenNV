using System.IO.MemoryMappedFiles;
using System.Text;

namespace OpenNV.Runtime.Diagnostics.Parity;

// A latest-frame display transport. Replaced display frames are not a lossless
// event trace and never participate in an exact-state or final-frame parity claim.
internal sealed class LiveHarnessFrameBuffer : IDisposable
{
    private const long Capacity = 64 + 3840L * 2160 * 4;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ONVLF001");
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly EventWaitHandle _ready;
    private long _sequence;

    internal LiveHarnessFrameBuffer(string channel, bool create)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The live desktop harness uses Windows shared surfaces.");
        if (channel.Length is < 1 or > 100 || channel.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            throw new ArgumentException("Invalid live surface channel.");
        var name = "Local\\OpenNV.LiveFrame." + channel;
        _mapping = create ? MemoryMappedFile.CreateOrOpen(name, Capacity, MemoryMappedFileAccess.ReadWrite) : MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
        _view = _mapping.CreateViewAccessor(0, Capacity, create ? MemoryMappedFileAccess.ReadWrite : MemoryMappedFileAccess.Read);
        _ready = create ? new EventWaitHandle(false, EventResetMode.AutoReset, name + ".ready") : EventWaitHandle.OpenExisting(name + ".ready");
        if (create)
        {
            _view.WriteArray(0, Magic, 0, Magic.Length);
            _sequence = _view.ReadInt64(8) & ~1L;
        }
    }

    internal WaitHandle Ready => _ready;

    internal void Publish(ulong draw, long nanoseconds, int width, int height, int pitch, int format, byte[] bytes)
    {
        if (width <= 0 || height <= 0 || pitch <= 0 || (long)width * (format == 1 ? 3 : 4) > pitch || bytes.Length != (long)pitch * height || bytes.Length > Capacity - 64 || format is < 1 or > 4)
            throw new InvalidDataException("Invalid native live surface extent or format.");
        _view.Write(8, ++_sequence);
        Thread.MemoryBarrier();
        _view.Write(16, draw);
        _view.Write(24, nanoseconds);
        _view.Write(32, width);
        _view.Write(36, height);
        _view.Write(40, pitch);
        _view.Write(44, format);
        _view.Write(48, bytes.Length);
        _view.Write(56, 0L);
        _view.WriteArray(64, bytes, 0, bytes.Length);
        Thread.MemoryBarrier();
        _view.Write(8, ++_sequence);
        _ready.Set();
    }

    internal LiveHarnessSurface? ReadLatest()
    {
        for (var attempt = 0; attempt < 4; ++attempt)
        {
            var sequence = _view.ReadInt64(8);
            if (sequence == 0 || (sequence & 1) != 0)
                continue;
            Thread.MemoryBarrier();
            var header = new byte[64];
            _view.ReadArray(0, header, 0, header.Length);
            if (!header.AsSpan(0, 8).SequenceEqual(Magic))
                throw new InvalidDataException("Unknown native live surface header.");
            var width = BitConverter.ToInt32(header, 32);
            var height = BitConverter.ToInt32(header, 36);
            var pitch = BitConverter.ToInt32(header, 40);
            var format = BitConverter.ToInt32(header, 44);
            var length = BitConverter.ToInt32(header, 48);
            if (width <= 0 || height <= 0 || pitch <= 0 || (long)width * (format == 1 ? 3 : 4) > pitch || length <= 0 || length > Capacity - 64 || (long)pitch * height != length || format is < 1 or > 4)
                continue;
            var bytes = new byte[length];
            _view.ReadArray(64, bytes, 0, length);
            Thread.MemoryBarrier();
            if (_view.ReadInt64(8) != sequence)
                continue;
            return new LiveHarnessSurface(sequence, BitConverter.ToUInt64(header, 16), BitConverter.ToInt64(header, 24), width, height, pitch, format, bytes);
        }
        return null;
    }

    public void Dispose() { _ready.Dispose(); _view.Dispose(); _mapping.Dispose(); }
}

internal sealed record LiveHarnessSurface(long Sequence, ulong Draw, long Nanoseconds, int Width, int Height, int Pitch, int Format, byte[] Bytes);
