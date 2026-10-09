using System.Buffers.Binary;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

// This storage owns actual strings, list nodes and source metadata in one
// retained native allocation. A pointer fixup is never a retail address.
internal sealed class FalloutNativePluginGraphMetadata
{
    private readonly List<byte> _bytes = [];
    private readonly List<(int At, uint Target)> _relative = [];
    internal int Length => _bytes.Count;
    internal uint Reserve(int length)
    {
        if (length <= 0) throw new InvalidDataException("Native metadata needs a positive owned extent.");
        while ((_bytes.Count & 3) != 0) _bytes.Add(0);
        var at = checked((uint)_bytes.Count);
        _bytes.AddRange(new byte[length]); return at;
    }
    internal uint CString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Contains((byte)0)) throw new InvalidDataException("Native metadata string contains an interior terminator.");
        var at = Reserve(checked(bytes.Length + 1)); Put(at, bytes); return at;
    }
    internal void String(uint at, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= ushort.MaxValue) throw new NotSupportedException("Native source String exceeds its UInt16 buffer-capacity owner.");
        // Empty String construction owns a null pointer and zero lengths.
        // Nonempty strings own their actual retained buffer and capacity.
        if (bytes.IsEmpty) return;
        Relative(at, CString(bytes)); Put16(checked(at + 4), checked((ushort)bytes.Length));
        Put16(checked(at + 6), checked((ushort)(bytes.Length + 1)));
    }
    internal void Relative(uint at, uint target) { Require(at, 4); Require(target, 1); _relative.Add((checked((int)at), target)); }
    internal void Word(uint at, uint value)
    { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); Put(at, bytes); }
    internal void Put16(uint at, ushort value)
    { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, value); Put(at, bytes); }
    internal void Byte(uint at, byte value) { Require(at, 1); _bytes[checked((int)at)] = value; }
    internal void Put(uint at, ReadOnlySpan<byte> bytes)
    { Require(at, bytes.Length); for (var i = 0; i < bytes.Length; ++i) _bytes[checked((int)at + i)] = bytes[i]; }
    internal uint List(IReadOnlyList<uint> pointers, bool relativeTargets = false)
    {
        var head = Reserve(8); var node = head;
        for (var index = 0; index < pointers.Count; ++index)
        {
            if (relativeTargets) Relative(node, pointers[index]); else Word(node, pointers[index]);
            if (index + 1 < pointers.Count) { var next = Reserve(8); Relative(checked(node + 4), next); node = next; }
        }
        return head;
    }
    internal void PointerListValues(uint head, IReadOnlyList<uint> pointers)
    {
        for (var index = 0; index < pointers.Count; ++index) Word(checked(head + (uint)index * 8), pointers[index]);
    }
    internal byte[] Bytes(uint basis)
    {
        var bytes = _bytes.ToArray();
        foreach (var (at, target) in _relative) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), checked(basis + target));
        return bytes;
    }
    internal static NativeNvseDataField Field(int offset, ReadOnlySpan<byte> bytes, string owner) => new(offset, bytes.ToArray(), owner);
    internal static NativeNvseDataField WordField(int offset, uint value, string owner)
    { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); return new(offset, bytes, owner); }
    internal static NativeNvseDataField ByteField(int offset, byte value, string owner) => new(offset, new byte[] { value }, owner);
    internal static NativeNvseDataField PointerListField(int offset, uint first, uint next, string owner)
    {
        var bytes = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, first); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), next);
        return new(offset, bytes, owner);
    }
    private void Require(uint at, int length)
    { if (length < 0 || at > int.MaxValue || at > _bytes.Count - length) throw new InvalidDataException("Native metadata field exceeds its owned extent."); }
}
