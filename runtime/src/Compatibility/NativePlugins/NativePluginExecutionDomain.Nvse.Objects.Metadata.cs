using System.Buffers.Binary;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Inline tList heads live in Script itself. This bundle owns actual child
// objects, remaining list nodes and terminated strings in one allocation.
internal sealed class NativeNvseScriptMetadata
{
    internal readonly record struct Head(uint Data, uint? Next, bool Relative, bool Empty);
    private readonly MemoryStream _bytes = new();
    private readonly List<(int At, uint Target)> _fixups = [];
    internal Head Contributors { get; }
    internal Head References { get; }
    internal Head Variables { get; }
    internal uint? Text { get; }
    internal uint EditorId { get; }
    internal byte[] Bytes => _bytes.ToArray();

    internal NativeNvseScriptMetadata(NativeNvseScriptSnapshot value)
    {
        EditorId = CString(value.EditorId.Span);
        Text = value.Text is { } text ? CString(text.Span) : null;
        Contributors = BuildList(value.Contributors.Select(row => row.Address).ToArray(), relative: false);
        var references = new List<uint>();
        foreach (var reference in value.References)
        {
            uint? name = reference.Name.IsEmpty ? null : CString(reference.Name.Span); var scalar = new byte[16];
            if (name is not null)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(scalar.AsSpan(4), checked((ushort)reference.Name.Length));
                BinaryPrimitives.WriteUInt16LittleEndian(scalar.AsSpan(6), checked((ushort)(reference.Name.Length + 1)));
            }
            BinaryPrimitives.WriteUInt32LittleEndian(scalar.AsSpan(8), reference.Form?.Address ?? 0);
            BinaryPrimitives.WriteUInt32LittleEndian(scalar.AsSpan(12), reference.Variable);
            var at = Blob(scalar); if (name is { } target) _fixups.Add((checked((int)at), target)); references.Add(at);
        }
        References = BuildList(references, relative: true);
        var variables = new List<uint>();
        foreach (var variable in value.Variables)
        {
            var name = CString(variable.Name.Span); var bytes = new byte[32]; variable.ScalarBytes.Span.CopyTo(bytes);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), checked((ushort)variable.Name.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(30), checked((ushort)(variable.Name.Length + 1)));
            var at = Blob(bytes); _fixups.Add((checked((int)at + 24), name)); variables.Add(at);
        }
        Variables = BuildList(variables, relative: true);
    }

    private uint CString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Contains((byte)0)) throw new InvalidDataException("A native source string contains an interior terminator.");
        var at = checked((uint)_bytes.Length); _bytes.Write(bytes); _bytes.WriteByte(0); return at;
    }
    private uint Blob(ReadOnlySpan<byte> bytes)
    {
        while ((_bytes.Length & 3) != 0) _bytes.WriteByte(0);
        var at = checked((uint)_bytes.Length); _bytes.Write(bytes); return at;
    }
    private Head BuildList(IReadOnlyList<uint> data, bool relative)
    {
        if (data.Count == 0) return new(0, null, relative, true);
        uint? next = null;
        for (var index = data.Count - 1; index > 0; --index)
        {
            var bytes = new byte[8];
            if (!relative) BinaryPrimitives.WriteUInt32LittleEndian(bytes, data[index]);
            var at = Blob(bytes);
            if (relative) _fixups.Add((checked((int)at), data[index]));
            if (next is { } target) _fixups.Add((checked((int)at + 4), target));
            next = at;
        }
        return new(data[0], next, relative, false);
    }
    internal void Relocate(byte[] bytes, uint basis)
    {
        foreach (var (at, target) in _fixups)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), checked(basis + target));
    }
    internal void List(Span<byte> bytes, int offset, Head head, uint basis)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], head.Empty ? 0 : head.Relative ? checked(basis + head.Data) : head.Data);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[(offset + 4)..], head.Next is { } next ? checked(basis + next) : 0);
    }
}
