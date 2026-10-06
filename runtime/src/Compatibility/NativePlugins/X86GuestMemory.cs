using System.Buffers.Binary;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum X86GuestMemoryAccess
{
    ReadOnly,
    ReadWrite,
}

// Guest addresses never become host pointers. This owns bytes, not an x86
// executor, Windows loader, engine object layout or hook implementation.
internal sealed class X86GuestMemory : IDisposable
{
    private const ulong AddressSpaceLength = 1UL << 32;
    private readonly int _byteBudget;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly List<Region> _regions = [];
    private int _mappedBytes;
    private bool _disposed;

    internal X86GuestMemory(int byteBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteBudget);
        _byteBudget = byteBudget;
    }

    internal void Map(uint address, ReadOnlySpan<byte> bytes, X86GuestMemoryAccess access)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var end = End(address, bytes.Length);
        if (access is not (X86GuestMemoryAccess.ReadOnly or X86GuestMemoryAccess.ReadWrite))
            throw new ArgumentOutOfRangeException(nameof(access));
        if (bytes.Length > _byteBudget - _mappedBytes)
            throw new InvalidOperationException("Guest memory mapping exceeds its explicit byte budget.");

        var insertion = 0;
        foreach (var region in _regions)
        {
            if ((ulong)address < region.End && (ulong)region.Address < end)
                throw new InvalidDataException("Guest memory mappings overlap.");
            if (region.Address < address) ++insertion;
        }

        _regions.Insert(insertion, new Region(address, bytes.ToArray(), access));
        _mappedBytes += bytes.Length;
    }

    internal void Read(uint address, Span<byte> destination)
    {
        var slices = Resolve(address, destination.Length, writing: false);
        var copied = 0;
        foreach (var slice in slices)
        {
            slice.Region.Bytes.AsSpan(slice.Offset, slice.Count).CopyTo(destination[copied..]);
            copied += slice.Count;
        }
    }

    internal void Write(uint address, ReadOnlySpan<byte> source)
    {
        var slices = Resolve(address, source.Length, writing: true);
        var copied = 0;
        foreach (var slice in slices)
        {
            source.Slice(copied, slice.Count).CopyTo(slice.Region.Bytes.AsSpan(slice.Offset, slice.Count));
            copied += slice.Count;
        }
    }

    internal uint ReadUInt32(uint address)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        Read(address, bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    internal void WriteUInt32(uint address, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        Write(address, bytes);
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed) return;
        _disposed = true;
        foreach (var region in _regions) Array.Clear(region.Bytes);
        _regions.Clear();
        _mappedBytes = 0;
    }

    private List<Slice> Resolve(uint address, int count, bool writing)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var end = End(address, count);
        var current = (ulong)address;
        var slices = new List<Slice>();
        foreach (var region in _regions)
        {
            if (region.End <= current) continue;
            if ((ulong)region.Address > current)
                throw new InvalidDataException("Guest memory access crosses an unmapped extent.");
            if (writing && region.Access != X86GuestMemoryAccess.ReadWrite)
                throw new NotSupportedException("Guest memory write has no writable owner.");

            var offset = checked((int)(current - region.Address));
            var available = checked((int)(Math.Min(region.End, end) - current));
            slices.Add(new Slice(region, offset, available));
            current += (uint)available;
            if (current == end) return slices;
        }
        throw new InvalidDataException("Guest memory access has no mapped owner.");
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Guest memory access must remain on its owning thread.");
    }

    private static ulong End(uint address, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (address == 0) throw new InvalidDataException("Null guest memory has no owner.");
        var end = (ulong)address + (uint)count;
        if (end > AddressSpaceLength)
            throw new InvalidDataException("Guest memory extent wraps the x86 address space.");
        return end;
    }

    private sealed record Region(uint Address, byte[] Bytes, X86GuestMemoryAccess Access)
    {
        internal ulong End => (ulong)Address + (uint)Bytes.Length;
    }

    private readonly record struct Slice(Region Region, int Offset, int Count);
}
